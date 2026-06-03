using System.Collections.Generic;
using System.Runtime.InteropServices;
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
        private static readonly int directionalLightColorIlluminanceID = Shader.PropertyToID("_DDGIDirectionalLightColorIlluminance");
        private static readonly int directionalLightDirectionWSID = Shader.PropertyToID("_DDGIDirectionalLightDirectionWS");
        private static readonly int traceInstanceSubMeshRangesID = Shader.PropertyToID("_DDGITraceInstanceSubMeshRanges");
        private static readonly int traceSubMeshTriangleRangesID = Shader.PropertyToID("_DDGITraceSubMeshTriangleRanges");
        private static readonly int traceTriangleNormalsID = Shader.PropertyToID("_DDGITraceTriangleNormals");

        private static RayTracingShader shader;
        private static RayTracingAccelerationStructure accelerationStructure;
        private static GraphicsBuffer traceInstanceSubMeshRangesBuffer;
        private static GraphicsBuffer traceSubMeshTriangleRangesBuffer;
        private static GraphicsBuffer traceTriangleNormalsBuffer;
        private static RTHandle probeIrradianceRT;
        private static RTHandle probeDistanceRT;
        private static RTHandle probeDataRT;
        private static DDGIResources.Identity persistentIdentity;
        private static bool hasPersistentIdentity;
        private static string lastStatusKey;

        internal static void Record(RenderGraph renderGraph, Camera camera, YutrelRPSettings.DDGISettings settings,
            LightResources lightResources, ref DDGIResources resources)
        {
            resources.Reset();

            if (settings == null || !settings.enabled)
            {
                ReleasePersistentAtlases();
                return;
            }

#if UNITY_EDITOR
            if (IsUnityFrameDebuggerActive())
            {
                ReleasePersistentAtlases();
                LogStatus(ProbeTraceIssue.FrameDebuggerActive, null);
                return;
            }
#endif

            if (camera.cameraType != CameraType.SceneView && camera.cameraType != CameraType.Game)
            {
                return;
            }

            var issue = ValidateCapability();
            var volume = issue == ProbeTraceIssue.None ? ResolveActiveVolume(out issue) : null;
            if (issue == ProbeTraceIssue.None)
            {
                issue = ValidateShaderResource(settings);
            }

            if (issue == ProbeTraceIssue.None)
            {
                issue = ValidateResourceDimensions(volume);
            }

            if (issue != ProbeTraceIssue.None)
            {
                ReleasePersistentAtlases();
                LogStatus(issue, volume);
                return;
            }

            issue = EnsurePersistentAtlases(renderGraph, volume, ref resources);
            if (issue != ProbeTraceIssue.None)
            {
                ReleasePersistentAtlases();
                resources.Reset();
                LogStatus(issue, volume);
                return;
            }

            issue = BuildAccelerationStructure(settings);
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
                clearColor = new Color(0.0f, 0.0f, 0.0f, -volume.ProbeMaxRayDistance - 2.0f),
                name = "DDGI ProbeRayData"
            };

            var probeRayData = renderGraph.CreateTexture(desc);
            resources.probe_ray_data = probeRayData;
            resources.SetVolumeMetadata(volume);

            using (var builder = renderGraph.AddComputePass<DDGIProbeTracePass>(sampler.name, out var pass, sampler))
            {
                pass.probeRayData = probeRayData;
                pass.rayTracingShader = shader;
                pass.rayTracingAccelerationStructure = accelerationStructure;
                pass.volumeMinWS = volume.WorldBounds.min;
                pass.probeSpacingWS = volume.GetWorldProbeSpacing();
                pass.probeCount = probeCount;
                pass.raysPerProbe = raysPerProbe;
                pass.planeProbeCount = planeProbeCount;
                pass.probeMaxRayDistance = volume.ProbeMaxRayDistance;
                pass.traceInstanceSubMeshRanges = traceInstanceSubMeshRangesBuffer;
                pass.traceSubMeshTriangleRanges = traceSubMeshTriangleRangesBuffer;
                pass.traceTriangleNormals = traceTriangleNormalsBuffer;
                pass.SetDirectionalLight(lightResources);

                builder.UseTexture(probeRayData, AccessFlags.Write);
                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);
                builder.SetRenderFunc<DDGIProbeTracePass>(static (pass, context) => pass.Render(context));
            }

            DDGIProbeBlendPass.Record(renderGraph, volume, settings, ref resources);
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
        private GraphicsBuffer traceInstanceSubMeshRanges;
        private GraphicsBuffer traceSubMeshTriangleRanges;
        private GraphicsBuffer traceTriangleNormals;
        private Vector4 directionalLightColorIlluminance;
        private Vector4 directionalLightDirectionWS;

        private void Render(ComputeGraphContext context)
        {
            var cmd = context.cmd;
            cmd.SetRayTracingShaderPass(rayTracingShader, ShaderPassName);
            cmd.SetRayTracingTextureParam(rayTracingShader, probeRayDataID, probeRayData);
            cmd.SetRayTracingAccelerationStructure(rayTracingShader, accelerationStructureID, rayTracingAccelerationStructure);
            cmd.SetRayTracingVectorParam(rayTracingShader, volumeMinWSID, volumeMinWS);
            cmd.SetRayTracingVectorParam(rayTracingShader, probeSpacingWSID, probeSpacingWS);
            cmd.SetRayTracingVectorParam(rayTracingShader, probeCountID,
                new Vector4(probeCount.x, probeCount.y, probeCount.z, 0.0f));
            cmd.SetRayTracingIntParam(rayTracingShader, raysPerProbeID, raysPerProbe);
            cmd.SetRayTracingFloatParam(rayTracingShader, probeMaxRayDistanceID, probeMaxRayDistance);
            cmd.SetRayTracingBufferParam(rayTracingShader, traceInstanceSubMeshRangesID, traceInstanceSubMeshRanges);
            cmd.SetRayTracingBufferParam(rayTracingShader, traceSubMeshTriangleRangesID, traceSubMeshTriangleRanges);
            cmd.SetRayTracingBufferParam(rayTracingShader, traceTriangleNormalsID, traceTriangleNormals);
            cmd.SetRayTracingVectorParam(rayTracingShader, directionalLightColorIlluminanceID,
                directionalLightColorIlluminance);
            cmd.SetRayTracingVectorParam(rayTracingShader, directionalLightDirectionWSID, directionalLightDirectionWS);
            cmd.DispatchRays(rayTracingShader, RayGenName, (uint)raysPerProbe, (uint)planeProbeCount,
                (uint)probeCount.y, null);
        }

        private void SetDirectionalLight(LightResources lightResources)
        {
            directionalLightColorIlluminance = Vector4.zero;
            directionalLightDirectionWS = Vector4.zero;
            if (lightResources == null || lightResources.directional_light_count <= 0)
            {
                return;
            }

            var light = lightResources.directional_light_data[0];
            directionalLightColorIlluminance = new Vector4(light.color.x, light.color.y, light.color.z,
                Mathf.Max(0.0f, light.illuminance));
            directionalLightDirectionWS = new Vector4(light.direction.x, light.direction.y, light.direction.z, 1.0f);
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

        private static ProbeTraceIssue ValidateShaderResource(YutrelRPSettings.DDGISettings settings)
        {
            shader = settings?.probeTraceShader;
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

            if (!ValidateAtlasDimensions(count, volume.ProbeIrradianceInteriorTexels + 2) ||
                !ValidateAtlasDimensions(count, volume.ProbeDistanceInteriorTexels + 2) ||
                count.x > SystemInfo.maxTextureSize || count.z > SystemInfo.maxTextureSize)
            {
                return ProbeTraceIssue.ResourceTooLarge;
            }

            return ProbeTraceIssue.None;
        }


        private static bool ValidateAtlasDimensions(Vector3Int count, int tileSize)
        {
            return tileSize > 0 &&
                   count.x <= SystemInfo.maxTextureSize / tileSize &&
                   count.z <= SystemInfo.maxTextureSize / tileSize &&
                   count.y <= SystemInfo.maxTextureArraySlices;
        }

        private static ProbeTraceIssue EnsurePersistentAtlases(RenderGraph renderGraph, YutrelDDGIVolume volume, ref DDGIResources resources)
        {
            var identity = new DDGIResources.Identity(volume);
            if (!hasPersistentIdentity || !persistentIdentity.Equals(identity) ||
                probeIrradianceRT == null || probeDistanceRT == null || probeDataRT == null)
            {
                ReleasePersistentAtlases();
                try
                {
                    probeIrradianceRT = AllocAtlasRT(
                        volume.ProbeCount.x * (volume.ProbeIrradianceInteriorTexels + 2),
                        volume.ProbeCount.z * (volume.ProbeIrradianceInteriorTexels + 2),
                        volume.ProbeCount.y,
                        GraphicsFormat.R16G16B16A16_SFloat,
                        "DDGI ProbeIrradiance");
                    probeDistanceRT = AllocAtlasRT(
                        volume.ProbeCount.x * (volume.ProbeDistanceInteriorTexels + 2),
                        volume.ProbeCount.z * (volume.ProbeDistanceInteriorTexels + 2),
                        volume.ProbeCount.y,
                        GraphicsFormat.R16G16B16A16_SFloat,
                        "DDGI ProbeDistance");
                    probeDataRT = AllocAtlasRT(
                        volume.ProbeCount.x,
                        volume.ProbeCount.z,
                        volume.ProbeCount.y,
                        GraphicsFormat.R16G16B16A16_SFloat,
                        "DDGI ProbeData");

                    ClearPersistentAtlas(probeIrradianceRT, Color.black);
                    ClearPersistentAtlas(probeDistanceRT, Color.black);
                    ClearPersistentAtlas(probeDataRT, new Color(0.0f, 0.0f, 0.0f, 1.0f));
                    persistentIdentity = identity;
                    hasPersistentIdentity = true;
                    Debug.Log($"YutrelRP DDGI persistent atlases initialized: {identity}.");
                }
                catch (System.Exception exception)
                {
                    Debug.LogWarning($"YutrelRP DDGI persistent atlas allocation failed: {exception.Message}");
                    ReleasePersistentAtlases();
                    return ProbeTraceIssue.ResourceAllocationFailed;
                }
            }

            resources.SetVolumeMetadata(volume);
            resources.probe_irradiance = renderGraph.ImportTexture(probeIrradianceRT);
            resources.probe_distance = renderGraph.ImportTexture(probeDistanceRT);
            resources.probe_data = renderGraph.ImportTexture(probeDataRT);
            resources.has_persistent_atlas = resources.probe_irradiance.IsValid() &&
                                             resources.probe_distance.IsValid() &&
                                             resources.probe_data.IsValid();
            return resources.has_persistent_atlas ? ProbeTraceIssue.None : ProbeTraceIssue.ResourceAllocationFailed;
        }

        private static RTHandle AllocAtlasRT(int width, int height, int slices, GraphicsFormat format, string name)
        {
            return RTHandles.Alloc(width, height, slices: slices, dimension: TextureDimension.Tex2DArray,
                colorFormat: format, enableRandomWrite: true, filterMode: FilterMode.Point,
                wrapMode: TextureWrapMode.Clamp, name: name);
        }

        private static void ClearPersistentAtlas(RTHandle handle, Color color)
        {
            var cmd = CommandBufferPool.Get("Clear DDGI Persistent Atlas");
            for (var slice = 0; slice < handle.rt.volumeDepth; slice++)
            {
                cmd.SetRenderTarget(handle, 0, CubemapFace.Unknown, slice);
                cmd.ClearRenderTarget(false, true, color);
            }
            Graphics.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        private static void ReleasePersistentAtlases()
        {
            RTHandles.Release(probeIrradianceRT);
            RTHandles.Release(probeDistanceRT);
            RTHandles.Release(probeDataRT);
            probeIrradianceRT = null;
            probeDistanceRT = null;
            probeDataRT = null;
            hasPersistentIdentity = false;
        }

        private static ProbeTraceIssue BuildAccelerationStructure(YutrelRPSettings.DDGISettings settings)
        {
            ReleaseTraceGeometryBuffers();

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
            var instanceRanges = new List<UInt2>();
            var subMeshRanges = new List<UInt2>();
            var triangleNormals = new List<Vector4>();

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

                if (!TryAppendTraceGeometry(renderer, instanceRanges, subMeshRanges, triangleNormals, out reason))
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

                    var handle = accelerationStructure.AddInstance(renderer, subMeshFlags: subMeshFlags, enableTriangleCulling: false,
                        frontTriangleCounterClockwise: false, mask: 0xFF, id: (uint)instanceCount);
                    if (handle == 0)
                    {
                        RemoveLastTraceGeometry(instanceRanges, subMeshRanges, triangleNormals);
                        continue;
                    }

                    instanceCount++;
                }
                catch (System.Exception exception)
                {
                    Debug.LogWarning($"YutrelRP DDGI ProbeTrace RTAS AddInstance failed for '{renderer.name}': {exception.Message}");
                    RemoveLastTraceGeometry(instanceRanges, subMeshRanges, triangleNormals);
                }
            }

            if (instanceCount == 0)
            {
                return ProbeTraceIssue.NoContributors;
            }

            if (!EnsureTraceGeometryBuffers(instanceRanges, subMeshRanges, triangleNormals))
            {
                ReleaseTraceGeometryBuffers();
                return ProbeTraceIssue.ResourceAllocationFailed;
            }

            accelerationStructure.Build();
            return accelerationStructure.GetInstanceCount() > 0 ? ProbeTraceIssue.None : ProbeTraceIssue.EmptyAccelerationStructure;
        }

        private static bool TryAppendTraceGeometry(MeshRenderer renderer, List<UInt2> instanceRanges,
            List<UInt2> subMeshRanges, List<Vector4> triangleNormals, out string reason)
        {
            reason = null;
            if (!renderer.TryGetComponent<MeshFilter>(out var meshFilter) || meshFilter.sharedMesh == null)
            {
                reason = "MeshRenderer has no shared mesh";
                return false;
            }

            var mesh = meshFilter.sharedMesh;
            Vector3[] vertices;
            try
            {
                vertices = mesh.vertices;
            }
            catch (System.Exception exception)
            {
                reason = $"mesh vertex data is not CPU-readable for DDGI trace normals ({exception.Message})";
                return false;
            }

            if (vertices == null || vertices.Length == 0)
            {
                reason = "mesh has no vertex positions";
                return false;
            }

            var firstSubMeshRange = subMeshRanges.Count;
            var firstTriangleNormal = triangleNormals.Count;
            var subMeshCount = Mathf.Max(1, mesh.subMeshCount);
            var localToWorld = renderer.localToWorldMatrix;

            for (var subMesh = 0; subMesh < subMeshCount; subMesh++)
            {
                int[] indices;
                try
                {
                    indices = mesh.GetTriangles(subMesh);
                }
                catch (System.Exception exception)
                {
                    reason = $"mesh submesh index data is not CPU-readable for DDGI trace normals ({exception.Message})";
                    RemoveTraceGeometry(instanceRanges, subMeshRanges, triangleNormals, firstSubMeshRange, firstTriangleNormal);
                    return false;
                }

                var normalBase = triangleNormals.Count;
                var triangleCount = indices != null ? indices.Length / 3 : 0;
                subMeshRanges.Add(new UInt2((uint)normalBase, (uint)triangleCount));

                for (var triangle = 0; triangle < triangleCount; triangle++)
                {
                    var i0 = indices[triangle * 3 + 0];
                    var i1 = indices[triangle * 3 + 1];
                    var i2 = indices[triangle * 3 + 2];
                    if ((uint)i0 >= vertices.Length || (uint)i1 >= vertices.Length || (uint)i2 >= vertices.Length)
                    {
                        triangleNormals.Add(Vector4.zero);
                        continue;
                    }

                    var p0 = localToWorld.MultiplyPoint3x4(vertices[i0]);
                    var p1 = localToWorld.MultiplyPoint3x4(vertices[i1]);
                    var p2 = localToWorld.MultiplyPoint3x4(vertices[i2]);
                    var normal = Vector3.Cross(p1 - p0, p2 - p0);
                    if (normal.sqrMagnitude <= 1.0e-10f)
                    {
                        triangleNormals.Add(Vector4.zero);
                    }
                    else
                    {
                        normal.Normalize();
                        triangleNormals.Add(new Vector4(normal.x, normal.y, normal.z, 1.0f));
                    }
                }
            }

            instanceRanges.Add(new UInt2((uint)firstSubMeshRange, (uint)subMeshCount));
            return triangleNormals.Count > firstTriangleNormal;
        }

        private static void RemoveLastTraceGeometry(List<UInt2> instanceRanges, List<UInt2> subMeshRanges,
            List<Vector4> triangleNormals)
        {
            if (instanceRanges.Count == 0)
            {
                return;
            }

            var range = instanceRanges[^1];
            var firstSubMeshRange = (int)range.x;
            var firstTriangleNormal = firstSubMeshRange < subMeshRanges.Count ? (int)subMeshRanges[firstSubMeshRange].x : triangleNormals.Count;
            instanceRanges.RemoveAt(instanceRanges.Count - 1);
            RemoveTraceGeometry(instanceRanges, subMeshRanges, triangleNormals, firstSubMeshRange, firstTriangleNormal);
        }

        private static void RemoveTraceGeometry(List<UInt2> instanceRanges, List<UInt2> subMeshRanges,
            List<Vector4> triangleNormals, int firstSubMeshRange, int firstTriangleNormal)
        {
            if (subMeshRanges.Count > firstSubMeshRange)
            {
                subMeshRanges.RemoveRange(firstSubMeshRange, subMeshRanges.Count - firstSubMeshRange);
            }

            if (triangleNormals.Count > firstTriangleNormal)
            {
                triangleNormals.RemoveRange(firstTriangleNormal, triangleNormals.Count - firstTriangleNormal);
            }
        }

        private static bool EnsureTraceGeometryBuffers(List<UInt2> instanceRanges, List<UInt2> subMeshRanges,
            List<Vector4> triangleNormals)
        {
            if (instanceRanges.Count == 0 || subMeshRanges.Count == 0 || triangleNormals.Count == 0)
            {
                return false;
            }

            traceInstanceSubMeshRangesBuffer = AllocStructuredBuffer(instanceRanges.Count, Marshal.SizeOf<UInt2>(),
                "DDGI Trace Instance SubMesh Ranges");
            traceSubMeshTriangleRangesBuffer = AllocStructuredBuffer(subMeshRanges.Count, Marshal.SizeOf<UInt2>(),
                "DDGI Trace SubMesh Triangle Ranges");
            traceTriangleNormalsBuffer = AllocStructuredBuffer(triangleNormals.Count, Marshal.SizeOf<Vector4>(),
                "DDGI Trace Triangle Normals");
            traceInstanceSubMeshRangesBuffer.SetData(instanceRanges);
            traceSubMeshTriangleRangesBuffer.SetData(subMeshRanges);
            traceTriangleNormalsBuffer.SetData(triangleNormals);
            return true;
        }

        private static GraphicsBuffer AllocStructuredBuffer(int count, int stride, string name)
        {
            var buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, Mathf.Max(count, 1), stride)
            {
                name = name
            };
            return buffer;
        }

        private static void ReleaseTraceGeometryBuffers()
        {
            traceInstanceSubMeshRangesBuffer?.Dispose();
            traceSubMeshTriangleRangesBuffer?.Dispose();
            traceTriangleNormalsBuffer?.Dispose();
            traceInstanceSubMeshRangesBuffer = null;
            traceSubMeshTriangleRangesBuffer = null;
            traceTriangleNormalsBuffer = null;
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
                case ProbeTraceIssue.ResourceAllocationFailed:
                    return "resource/allocation";
                case ProbeTraceIssue.FrameDebuggerActive:
                    return "editor/debugger";
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
                    return "YutrelRPAsset DDGI probeTraceShader is missing or invalid";
                case ProbeTraceIssue.NoContributors:
                    return "no enabled opaque MeshRenderer with Unity Contribute GI was found";
                case ProbeTraceIssue.EmptyAccelerationStructure:
                    return "DDGI RTAS build produced no instances";
                case ProbeTraceIssue.ResourceTooLarge:
                    return "DDGI texture dimensions exceed platform texture limits";
                case ProbeTraceIssue.ResourceAllocationFailed:
                    return "persistent DDGI atlas allocation or import failed";
                case ProbeTraceIssue.FrameDebuggerActive:
                    return "Unity Frame Debugger is active; DDGI ProbeTrace uses D3D12 ray tracing commands that are skipped to avoid editor device-loss crashes";
                default:
                    return "unknown failure";
            }
        }

#if UNITY_EDITOR
        private static bool IsUnityFrameDebuggerActive()
        {
            var frameDebuggerType =
                typeof(EditorApplication).Assembly.GetType("UnityEditorInternal.FrameDebuggerUtility");
            if (frameDebuggerType == null)
            {
                return false;
            }

            var enabledProperty = frameDebuggerType.GetProperty("enabled",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic);
            if (enabledProperty != null && enabledProperty.PropertyType == typeof(bool))
            {
                return (bool)enabledProperty.GetValue(null);
            }

            var isLocalEnabledMethod = frameDebuggerType.GetMethod("IsLocalEnabled",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic,
                null, System.Type.EmptyTypes, null);
            return isLocalEnabledMethod != null && (bool)isLocalEnabledMethod.Invoke(null, null);
        }
#endif

        internal static void ReleasePersistentAtlasesForDisabled()
        {
            ReleasePersistentAtlases();
        }

        public static void Cleanup()
        {
            accelerationStructure?.Dispose();
            accelerationStructure = null;
            ReleaseTraceGeometryBuffers();
            ReleasePersistentAtlases();
            DDGIProbeBlendPass.Cleanup();
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
            ResourceTooLarge = 8,
            FrameDebuggerActive = 9,
            ResourceAllocationFailed = 10
        }

        [StructLayout(LayoutKind.Sequential)]
        private readonly struct UInt2
        {
            public readonly uint x;
            public readonly uint y;

            public UInt2(uint x, uint y)
            {
                this.x = x;
                this.y = y;
            }
        }
    }
}
