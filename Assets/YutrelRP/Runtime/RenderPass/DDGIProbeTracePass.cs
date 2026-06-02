using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace YutrelRP
{
    internal sealed class DDGIProbeTracePass
    {
        private const string ShaderResourcePath = "Shader/DDGIProbeTrace";
        private const string RayGenName = "RayGenDDGIProbeTrace";
        private const string ShaderPassName = "YutrelRPDDGIProbeTrace";

        private static readonly ProfilingSampler sampler = new("DDGI Probe Trace");
        private static readonly int accelerationStructureID = Shader.PropertyToID("_RaytracingAccelerationStructure");
        private static readonly int probeRayDataID = DDGIResources.probe_ray_data_ID;
        private static readonly int volumeMinWSID = Shader.PropertyToID("_DDGIVolumeMinWS");
        private static readonly int probeSpacingWSID = Shader.PropertyToID("_DDGIProbeSpacingWS");
        private static readonly int probeCountID = Shader.PropertyToID("_DDGIProbeCount");
        private static readonly int raysPerProbeID = Shader.PropertyToID("_DDGIProbeRaysPerProbe");
        private static readonly int probeMaxRayDistanceID = Shader.PropertyToID("_DDGIProbeMaxRayDistance");

        private static RayTracingShader shader;
        private static RayTracingAccelerationStructure accelerationStructure;
        private static string lastStatusKey;

        internal static void Record(RenderGraph renderGraph, Camera camera, YutrelRPSettings.DDGISettings settings,
            ref DDGIResources resources)
        {
            resources.Reset();

            if (settings == null || !settings.enabled)
            {
                return;
            }

            if (camera.cameraType != CameraType.SceneView && camera.cameraType != CameraType.Game)
            {
                return;
            }

            var issue = ValidateCapability();
            var volume = issue == ProbeTraceIssue.None ? ResolveActiveVolume(out issue) : null;
            if (issue == ProbeTraceIssue.None)
            {
                issue = ValidateShaderResource();
            }

            if (issue == ProbeTraceIssue.None)
            {
                issue = ValidateResourceDimensions(volume);
            }

            if (issue == ProbeTraceIssue.None)
            {
                issue = BuildAccelerationStructure(settings);
            }

            LogStatus(issue, volume);
            if (issue != ProbeTraceIssue.None)
            {
                return;
            }

            var probeCount = volume.ProbeCount;
            var raysPerProbe = volume.RaysPerProbe;
            var planeProbeCount = probeCount.x * probeCount.z;
            var desc = new TextureDesc(raysPerProbe, planeProbeCount)
            {
                colorFormat = GraphicsFormat.R16G16B16A16_SFloat,
                dimension = TextureDimension.Tex2DArray,
                slices = probeCount.y,
                enableRandomWrite = true,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                clearBuffer = true,
                clearColor = Color.black,
                name = "DDGI ProbeRayData"
            };

            var probeRayData = renderGraph.CreateTexture(desc);
            resources.probe_ray_data = probeRayData;
            resources.probe_count = probeCount;
            resources.rays_per_probe = raysPerProbe;
            resources.probe_max_ray_distance = volume.ProbeMaxRayDistance;

            using var builder = renderGraph.AddUnsafePass<DDGIProbeTracePass>(sampler.name, out var pass, sampler);
            pass.probeRayData = probeRayData;
            pass.rayTracingShader = shader;
            pass.rayTracingAccelerationStructure = accelerationStructure;
            pass.volumeMinWS = volume.WorldBounds.min;
            pass.probeSpacingWS = volume.GetWorldProbeSpacing();
            pass.probeCount = probeCount;
            pass.raysPerProbe = raysPerProbe;
            pass.planeProbeCount = planeProbeCount;
            pass.probeMaxRayDistance = volume.ProbeMaxRayDistance;

            builder.UseTexture(probeRayData, AccessFlags.Write);
            builder.AllowPassCulling(false);
            builder.AllowGlobalStateModification(true);
            builder.SetRenderFunc<DDGIProbeTracePass>(static (pass, context) => pass.Render(context));
        }

        private TextureHandle probeRayData;
        private RayTracingShader rayTracingShader;
        private RayTracingAccelerationStructure rayTracingAccelerationStructure;
        private Vector3 volumeMinWS;
        private Vector3 probeSpacingWS;
        private Vector3Int probeCount;
        private int raysPerProbe;
        private int planeProbeCount;
        private float probeMaxRayDistance;

        private void Render(UnsafeGraphContext context)
        {
            var cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
            cmd.SetRayTracingShaderPass(rayTracingShader, ShaderPassName);
            cmd.SetRayTracingTextureParam(rayTracingShader, probeRayDataID, probeRayData);
            cmd.SetRayTracingAccelerationStructure(rayTracingShader, accelerationStructureID, rayTracingAccelerationStructure);
            cmd.SetRayTracingVectorParam(rayTracingShader, volumeMinWSID, volumeMinWS);
            cmd.SetRayTracingVectorParam(rayTracingShader, probeSpacingWSID, probeSpacingWS);
            cmd.SetRayTracingVectorParam(rayTracingShader, probeCountID,
                new Vector4(probeCount.x, probeCount.y, probeCount.z, 0.0f));
            cmd.SetRayTracingIntParam(rayTracingShader, raysPerProbeID, raysPerProbe);
            cmd.SetRayTracingFloatParam(rayTracingShader, probeMaxRayDistanceID, probeMaxRayDistance);
            cmd.DispatchRays(rayTracingShader, RayGenName, (uint)raysPerProbe, (uint)planeProbeCount,
                (uint)probeCount.y, null);
        }

        private static ProbeTraceIssue ValidateCapability()
        {
            var device = SystemInfo.graphicsDeviceType;
            if (device != GraphicsDeviceType.Direct3D12)
            {
                return ProbeTraceIssue.UnsupportedGraphicsAPI;
            }

            if (!SystemInfo.supportsRayTracing)
            {
                return ProbeTraceIssue.UnsupportedRayTracing;
            }

            return ProbeTraceIssue.None;
        }

        private static YutrelDDGIVolume ResolveActiveVolume(out ProbeTraceIssue issue)
        {
            issue = ProbeTraceIssue.None;
            YutrelDDGIVolume selected = null;
            var activeVolumeCount = 0;
            var volumes = Object.FindObjectsByType<YutrelDDGIVolume>();
            foreach (var volume in volumes)
            {
                if (volume == null || !volume.isActiveAndEnabled)
                {
                    continue;
                }

                activeVolumeCount++;
                if (selected == null || GetStableVolumeKey(volume) < GetStableVolumeKey(selected))
                {
                    selected = volume;
                }
            }

            if (selected == null)
            {
                issue = ProbeTraceIssue.MissingVolume;
            }
            else if (activeVolumeCount > 1)
            {
                Debug.LogWarning($"YutrelRP DDGI ProbeTrace found {activeVolumeCount} active DDGI Volumes. First version uses '{selected.name}' by instance ID.");
            }

            return selected;
        }

        private static ProbeTraceIssue ValidateShaderResource()
        {
            if (shader == null)
            {
                shader = Resources.Load<RayTracingShader>(ShaderResourcePath);
            }

            return shader == null ? ProbeTraceIssue.MissingRayTracingShader : ProbeTraceIssue.None;
        }

        private static ProbeTraceIssue ValidateResourceDimensions(YutrelDDGIVolume volume)
        {
            if (volume == null)
            {
                return ProbeTraceIssue.MissingVolume;
            }

            var count = volume.ProbeCount;
            if (count.x < YutrelDDGIVolume.MinProbeCountPerAxis ||
                count.y < YutrelDDGIVolume.MinProbeCountPerAxis ||
                count.z < YutrelDDGIVolume.MinProbeCountPerAxis ||
                volume.RaysPerProbe < YutrelDDGIVolume.MinRaysPerProbe ||
                volume.ProbeMaxRayDistance < YutrelDDGIVolume.MinProbeMaxRayDistance ||
                volume.WorldBounds.size.x <= 0.0f ||
                volume.WorldBounds.size.y <= 0.0f ||
                volume.WorldBounds.size.z <= 0.0f)
            {
                return ProbeTraceIssue.InvalidVolume;
            }

            var width = volume.RaysPerProbe;
            var height = count.x * count.z;
            if (width > SystemInfo.maxTextureSize || height > SystemInfo.maxTextureSize ||
                count.y > SystemInfo.maxTextureArraySlices)
            {
                return ProbeTraceIssue.ResourceTooLarge;
            }

            return ProbeTraceIssue.None;
        }

        private static ProbeTraceIssue BuildAccelerationStructure(YutrelRPSettings.DDGISettings settings)
        {
            if (accelerationStructure == null)
            {
                accelerationStructure = new RayTracingAccelerationStructure();
            }
            else
            {
                accelerationStructure.ClearInstances();
            }

            var renderers = Object.FindObjectsByType<MeshRenderer>();
            var instanceCount = 0;

            foreach (var renderer in renderers)
            {
                if (!IsDDGIContributeGIRenderer(renderer))
                {
                    continue;
                }

                if (!TryGetEligibleRenderer(renderer, out var reason))
                {
                    if (settings.logDiagnostics)
                    {
                        Debug.LogWarning($"YutrelRP DDGI ProbeTrace skipped Contribute GI renderer '{renderer.name}': {reason}.");
                    }

                    continue;
                }

                try
                {
                    var subMeshCount = GetSubMeshCount(renderer);
                    var subMeshFlags = new RayTracingSubMeshFlags[Mathf.Max(1, subMeshCount)];
                    for (var i = 0; i < subMeshFlags.Length; i++)
                    {
                        subMeshFlags[i] = RayTracingSubMeshFlags.Enabled | RayTracingSubMeshFlags.ClosestHitOnly;
                    }

                    accelerationStructure.AddInstance(renderer, subMeshFlags: subMeshFlags, enableTriangleCulling: false,
                        frontTriangleCounterClockwise: false, mask: 0xFF);
                    instanceCount++;
                }
                catch (System.Exception exception)
                {
                    Debug.LogWarning($"YutrelRP DDGI ProbeTrace RTAS AddInstance failed for '{renderer.name}': {exception.Message}");
                }
            }

            if (instanceCount == 0)
            {
                return ProbeTraceIssue.NoContributors;
            }

            accelerationStructure.Build();
            return accelerationStructure.GetInstanceCount() > 0 ? ProbeTraceIssue.None : ProbeTraceIssue.EmptyAccelerationStructure;
        }

        private static bool IsDDGIContributeGIRenderer(Renderer renderer)
        {
            if (renderer == null)
            {
                return false;
            }

#if UNITY_EDITOR
            var flags = GameObjectUtility.GetStaticEditorFlags(renderer.gameObject);
            return (flags & StaticEditorFlags.ContributeGI) != 0;
#else
            return false;
#endif
        }

        private static bool TryGetEligibleRenderer(MeshRenderer renderer, out string reason)
        {
            reason = null;

            if (renderer == null)
            {
                reason = "renderer is missing";
                return false;
            }

            if (!renderer.enabled || !renderer.gameObject.activeInHierarchy)
            {
                reason = "renderer is disabled or inactive";
                return false;
            }

            if (!renderer.TryGetComponent<MeshFilter>(out var meshFilter) || meshFilter.sharedMesh == null)
            {
                reason = "MeshRenderer has no shared mesh";
                return false;
            }

            if (HasTransparentMaterial(renderer))
            {
                reason = "transparent materials are not part of first-stage DDGI capture";
                return false;
            }

            return true;
        }

        private static int GetSubMeshCount(Renderer renderer)
        {
            if (renderer is MeshRenderer && renderer.TryGetComponent<MeshFilter>(out var meshFilter) &&
                meshFilter.sharedMesh != null)
            {
                return meshFilter.sharedMesh.subMeshCount;
            }

            return renderer.sharedMaterials != null ? renderer.sharedMaterials.Length : 1;
        }

        private static bool HasTransparentMaterial(Renderer renderer)
        {
            var materials = renderer.sharedMaterials;
            if (materials == null)
            {
                return false;
            }

            foreach (var material in materials)
            {
                if (material != null && material.renderQueue >= (int)RenderQueue.Transparent)
                {
                    return true;
                }
            }

            return false;
        }

        private static void LogStatus(ProbeTraceIssue issue, YutrelDDGIVolume volume)
        {
            var device = SystemInfo.graphicsDeviceType;
            var volumeKey = volume != null
                ? $"{GetStableVolumeKey(volume)}:{volume.ProbeCount}:{volume.RaysPerProbe}:{volume.ProbeMaxRayDistance}"
                : "none";
            var key = $"{issue}:{device}:{SystemInfo.supportsRayTracing}:{volumeKey}";
            if (key == lastStatusKey)
            {
                return;
            }

            lastStatusKey = key;

            if (issue == ProbeTraceIssue.None)
            {
                Debug.Log($"YutrelRP DDGI ProbeTrace OK: volume='{volume.name}', probes={volume.ProbeCount}, raysPerProbe={volume.RaysPerProbe}, layout={volume.RaysPerProbe} x {volume.ProbeCount.x * volume.ProbeCount.z} x {volume.ProbeCount.y}.");
                return;
            }

            Debug.LogWarning($"YutrelRP DDGI ProbeTrace skipped: category={GetCategory(issue)}, reason={GetReason(issue)}, api={device}, supportsRayTracing={SystemInfo.supportsRayTracing}.");
        }

        private static string GetCategory(ProbeTraceIssue issue)
        {
            switch (issue)
            {
                case ProbeTraceIssue.UnsupportedGraphicsAPI:
                case ProbeTraceIssue.UnsupportedRayTracing:
                    return "platform/API";
                case ProbeTraceIssue.MissingVolume:
                case ProbeTraceIssue.InvalidVolume:
                    return "volume";
                case ProbeTraceIssue.MissingRayTracingShader:
                    return "resource/loading";
                case ProbeTraceIssue.NoContributors:
                case ProbeTraceIssue.EmptyAccelerationStructure:
                    return "acceleration-structure/geometry";
                case ProbeTraceIssue.ResourceTooLarge:
                    return "resource/dimensions";
                default:
                    return "dispatch/output";
            }
        }

        private static int GetStableVolumeKey(YutrelDDGIVolume volume)
        {
            return volume != null ? volume.GetEntityId().GetHashCode() : 0;
        }

        private static string GetReason(ProbeTraceIssue issue)
        {
            switch (issue)
            {
                case ProbeTraceIssue.UnsupportedGraphicsAPI:
                    return "DDGI probe trace first version requires Direct3D12";
                case ProbeTraceIssue.UnsupportedRayTracing:
                    return "SystemInfo.supportsRayTracing is false";
                case ProbeTraceIssue.MissingVolume:
                    return "no active YutrelDDGIVolume was found";
                case ProbeTraceIssue.InvalidVolume:
                    return "active DDGI Volume has invalid bounds, probe counts, ray count, or ray distance";
                case ProbeTraceIssue.MissingRayTracingShader:
                    return "Resources/Shader/DDGIProbeTrace RayTracingShader asset is missing or invalid";
                case ProbeTraceIssue.NoContributors:
                    return "no enabled opaque MeshRenderer with Unity Contribute GI was found";
                case ProbeTraceIssue.EmptyAccelerationStructure:
                    return "DDGI RTAS build produced no instances";
                case ProbeTraceIssue.ResourceTooLarge:
                    return "ProbeRayData dimensions exceed platform texture limits";
                default:
                    return "unknown failure";
            }
        }

        public static void Cleanup()
        {
            accelerationStructure?.Dispose();
            accelerationStructure = null;
            shader = null;
            lastStatusKey = null;
        }

        private enum ProbeTraceIssue
        {
            None = 0,
            UnsupportedGraphicsAPI = 1,
            UnsupportedRayTracing = 2,
            MissingVolume = 3,
            InvalidVolume = 4,
            MissingRayTracingShader = 5,
            NoContributors = 6,
            EmptyAccelerationStructure = 7,
            ResourceTooLarge = 8
        }
    }
}
