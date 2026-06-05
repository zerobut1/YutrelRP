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
        private const string ScreenTraceRayGenName = "RayGenDDGIScreenTrace";
        private const string ShaderPassName = "DDGIRayTracing";
        private const string EnvironmentReflectionCubeName = "_EnvironmentReflectionCube";

        private static readonly ProfilingSampler sampler = new("DDGI Probe Trace");
        private static readonly int accelerationStructureID = Shader.PropertyToID("_RaytracingAccelerationStructure");
        private static readonly int probeRayDataID = DDGIResources.probe_ray_data_ID;
        private static readonly int probeIrradianceID = DDGIResources.probe_irradiance_ID;
        private static readonly int probeIrradianceDimensionsID = DDGIResources.probe_irradiance_dimensions_ID;
        private static readonly int probeDistanceID = DDGIResources.probe_distance_ID;
        private static readonly int probeDistanceDimensionsID = DDGIResources.probe_distance_dimensions_ID;
        private static readonly int probeDataID = DDGIResources.probe_data_ID;
        private static readonly int probeDataDimensionsID = DDGIResources.probe_data_dimensions_ID;
        private static readonly int probeRelocationEnabledID = DDGIResources.probe_relocation_enabled_ID;
        private static readonly int volumeMinWSID = Shader.PropertyToID("_DDGIVolumeMinWS");
        private static readonly int volumeMaxWSID = Shader.PropertyToID("_DDGIVolumeMaxWS");
        private static readonly int probeSpacingWSID = Shader.PropertyToID("_DDGIProbeSpacingWS");
        private static readonly int probeNormalBiasID = DDGIResources.probe_normal_bias_ID;
        private static readonly int probeViewBiasID = DDGIResources.probe_view_bias_ID;
        private static readonly int probeIrradianceEncodingGammaID = DDGIResources.probe_irradiance_encoding_gamma_ID;
        private static readonly int probeCountID = Shader.PropertyToID("_DDGIProbeCount");
        private static readonly int raysPerProbeID = Shader.PropertyToID("_DDGIProbeRaysPerProbe");
        private static readonly int probeMaxRayDistanceID = Shader.PropertyToID("_DDGIProbeMaxRayDistance");
        private static readonly int directionalLightColorIlluminanceID = Shader.PropertyToID("_DDGIDirectionalLightColorIlluminance");
        private static readonly int directionalLightDirectionWSID = Shader.PropertyToID("_DDGIDirectionalLightDirectionWS");
        private static readonly int directionalLightVisibilityEnabledID = Shader.PropertyToID("_DDGIDirectionalLightVisibilityEnabled");
        private static readonly int directionalLightLambertEnabledID = Shader.PropertyToID("_DDGIDirectionalLightLambertEnabled");
        private static readonly int environmentReflectionCubeHdrID = LightResources.environment_reflection_cube_hdr_ID;
        private static readonly int environmentIntensityID = LightResources.environment_intensity_ID;
        private static readonly int environmentDiffuseMultiplierID = LightResources.environment_diffuse_multiplier_ID;
        private static readonly int environmentValidID = Shader.PropertyToID("_DDGIEnvironmentValid");
        private static readonly int traceInstanceTriangleRangesID = Shader.PropertyToID("_DDGITraceInstanceTriangleRanges");
        private static readonly int traceTriangleNormalsID = Shader.PropertyToID("_DDGITraceTriangleNormals");
        private static readonly int traceInstanceMaterialFlagsID = Shader.PropertyToID("_DDGITraceInstanceMaterialFlags");
        private static readonly int traceAlbedoID = DDGIResources.trace_albedo_ID;
        private static readonly int screenTraceDebugID = DDGIResources.screen_trace_debug_ID;
        private static readonly int screenTraceDepthID = Shader.PropertyToID("_DDGIScreenTraceDepth");
        private static readonly int screenTraceInvViewProjectionID = Shader.PropertyToID("_DDGIScreenTraceInvViewProjection");
        private static readonly int screenTraceCameraPositionWSID = Shader.PropertyToID("_DDGIScreenTraceCameraPositionWS");
        private static readonly int screenTraceReversedZID = Shader.PropertyToID("_DDGIScreenTraceReversedZ");
        private static readonly int screenTraceProjectionFlipYID = Shader.PropertyToID("_DDGIScreenTraceProjectionFlipY");

        private static readonly int BaseColorTexID = Shader.PropertyToID("_BaseColorTex");
        private static readonly int UseBaseColorTexID = Shader.PropertyToID("_UseBaseColorTex");
        private const uint TraceMaterialHasBaseColorTexture = 1u;
        private const uint TraceMaterialHasUV0 = 2u;

        private static RayTracingShader shader;
        private static RayTracingAccelerationStructure accelerationStructure;
        private static GraphicsBuffer traceInstanceTriangleRangesBuffer;
        private static GraphicsBuffer traceTriangleNormalsBuffer;
        private static GraphicsBuffer traceInstanceMaterialFlagsBuffer;
        private static RTHandle probeIrradianceRT;
        private static RTHandle probeDistanceRT;
        private static RTHandle probeDataRT;
        private static RTHandle fallbackEnvironmentReflectionRT;
        private static DDGIResources.Identity persistentIdentity;
        private static bool hasPersistentIdentity;
        private static string lastStatusKey;

        internal static void Record(RenderGraph renderGraph, Camera camera, YutrelRPSettings.DDGISettings settings,
            LightResources lightResources, bool screenTraceDebug, TextureHandle sceneDepth,
            Vector2Int attachmentSize, ref DDGIResources resources)
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
                SetDiagnostic(ref resources, ProbeTraceIssue.FrameDebuggerActive);
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
                SetDiagnostic(ref resources, issue);
                LogStatus(issue, volume);
                return;
            }

            issue = EnsurePersistentAtlases(renderGraph, volume, ref resources);
            if (issue != ProbeTraceIssue.None)
            {
                ReleasePersistentAtlases();
                resources.Reset();
                SetDiagnostic(ref resources, issue);
                LogStatus(issue, volume);
                return;
            }

            issue = BuildAccelerationStructure(settings);
            LogStatus(issue, volume);
            if (issue != ProbeTraceIssue.None)
            {
                SetDiagnostic(ref resources, issue);
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

            desc.name = "DDGI TraceAlbedo";
            desc.clearColor = Color.black;
            var traceAlbedo = renderGraph.CreateTexture(desc);
            resources.trace_albedo = traceAlbedo;
            resources.SetVolumeMetadata(volume);
            resources.probe_relocation_enabled = settings.probeRelocationEnabled;

            TextureHandle screenTraceDebugOutput = TextureHandle.nullHandle;
            TextureHandle screenTraceDepth = TextureHandle.nullHandle;
            var writesScreenTraceDebug = screenTraceDebug && sceneDepth.IsValid() &&
                                         attachmentSize.x > 0 && attachmentSize.y > 0;
            if (writesScreenTraceDebug)
            {
                screenTraceDepth = DDGIScreenTraceDepthCopyPass.Record(renderGraph, sceneDepth, attachmentSize);

                var screenTraceDesc = new TextureDesc(attachmentSize.x, attachmentSize.y)
                {
                    colorFormat = GraphicsFormat.R16G16B16A16_SFloat,
                    enableRandomWrite = true,
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp,
                    clearBuffer = true,
                    clearColor = Color.black,
                    name = "DDGI Screen Trace Debug"
                };
                screenTraceDebugOutput = renderGraph.CreateTexture(screenTraceDesc);
                resources.screen_trace_debug = screenTraceDebugOutput;
            }
            else
            {
                screenTraceDebugOutput = CreateFallbackScreenTraceDebug(renderGraph);
                screenTraceDepth = CreateFallbackScreenTraceDepth(renderGraph);
            }

            using (var builder = renderGraph.AddComputePass<DDGIProbeTracePass>(sampler.name, out var pass, sampler))
            {
                pass.probeRayData = probeRayData;
                pass.traceAlbedo = traceAlbedo;
                pass.screenTraceDebug = screenTraceDebugOutput;
                pass.screenTraceDepth = screenTraceDepth;
                pass.writesScreenTraceDebug = writesScreenTraceDebug;
                pass.screenTraceWidth = attachmentSize.x;
                pass.screenTraceHeight = attachmentSize.y;
                pass.rayTracingShader = shader;
                pass.rayTracingAccelerationStructure = accelerationStructure;
                pass.probeIrradiance = resources.probe_irradiance;
                pass.probeDistance = resources.probe_distance;
                pass.probeData = resources.probe_data;
                pass.volumeMinWS = volume.WorldBounds.min;
                pass.volumeMaxWS = volume.WorldBounds.max;
                pass.probeSpacingWS = volume.GetWorldProbeSpacing();
                pass.probeNormalBias = volume.ProbeNormalBias;
                pass.probeViewBias = volume.ProbeViewBias;
                pass.probeIrradianceEncodingGamma = volume.IrradianceEncodingGamma;
                pass.probeCount = probeCount;
                pass.raysPerProbe = raysPerProbe;
                pass.planeProbeCount = planeProbeCount;
                pass.probeMaxRayDistance = volume.ProbeMaxRayDistance;
                pass.probeIrradianceDimensions = resources.ProbeIrradianceDimensions;
                pass.probeDistanceDimensions = resources.ProbeDistanceDimensions;
                pass.probeDataDimensions = resources.ProbeDataDimensions;
                pass.probeRelocationEnabled = settings.probeRelocationEnabled ? 1.0f : 0.0f;
                pass.traceInstanceTriangleRanges = traceInstanceTriangleRangesBuffer;
                pass.traceTriangleNormals = traceTriangleNormalsBuffer;
                pass.traceInstanceMaterialFlags = traceInstanceMaterialFlagsBuffer;
                pass.inverseViewProjection = GetInverseViewProjection(camera);
                pass.cameraPositionWS = camera.transform.position;
                pass.reversedZ = SystemInfo.usesReversedZBuffer ? 1 : 0;
                pass.projectionFlipY = GetProjectionFlipY(camera);
                pass.directionalLightVisibilityEnabled = settings.traceDirectionalVisibility ? 1.0f : 0.0f;
                pass.directionalLightLambertEnabled = settings.traceDirectionalLambert ? 1.0f : 0.0f;
                pass.SetDirectionalLight(lightResources);
                pass.SetEnvironment(lightResources);
                if (!pass.hasEnvironmentReflectionCube)
                {
                    pass.environmentReflectionCube = ImportFallbackEnvironmentReflection(renderGraph);
                }

                builder.UseTexture(probeRayData, AccessFlags.Write);
                builder.UseTexture(traceAlbedo, AccessFlags.Write);
                builder.UseTexture(screenTraceDebugOutput, AccessFlags.Write);
                builder.UseTexture(screenTraceDepth, AccessFlags.Read);
                builder.UseTexture(pass.probeIrradiance, AccessFlags.Read);
                builder.UseTexture(pass.probeDistance, AccessFlags.Read);
                builder.UseTexture(pass.probeData, AccessFlags.Read);
                if (pass.environmentReflectionCube.IsValid())
                {
                    builder.UseTexture(pass.environmentReflectionCube);
                }
                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);
                builder.SetRenderFunc<DDGIProbeTracePass>(static (pass, context) => pass.Render(context));
            }

            DDGIProbeRelocationPass.Record(renderGraph, volume, settings, ref resources);
            DDGIProbeBlendPass.Record(renderGraph, volume, settings, ref resources);
        }

        private TextureHandle probeRayData;
        private TextureHandle traceAlbedo;
        private TextureHandle screenTraceDebug;
        private TextureHandle screenTraceDepth;
        private TextureHandle probeIrradiance;
        private TextureHandle probeDistance;
        private TextureHandle probeData;
        private RayTracingShader rayTracingShader;
        private RayTracingAccelerationStructure rayTracingAccelerationStructure;
        private Vector3 volumeMinWS;
        private Vector3 volumeMaxWS;
        private Vector3 probeSpacingWS;
        private float probeNormalBias;
        private float probeViewBias;
        private float probeIrradianceEncodingGamma;
        private Vector3Int probeCount;
        private int raysPerProbe;
        private int planeProbeCount;
        private float probeMaxRayDistance;
        private Vector4 probeIrradianceDimensions;
        private Vector4 probeDistanceDimensions;
        private Vector4 probeDataDimensions;
        private float probeRelocationEnabled;
        private GraphicsBuffer traceInstanceTriangleRanges;
        private GraphicsBuffer traceTriangleNormals;
        private GraphicsBuffer traceInstanceMaterialFlags;
        private Matrix4x4 inverseViewProjection;
        private Vector3 cameraPositionWS;
        private int reversedZ;
        private int projectionFlipY;
        private int screenTraceWidth;
        private int screenTraceHeight;
        private Vector4 directionalLightColorIlluminance;
        private Vector4 directionalLightDirectionWS;
        private float directionalLightVisibilityEnabled;
        private float directionalLightLambertEnabled;
        private TextureHandle environmentReflectionCube;
        private Vector4 environmentReflectionCubeHdr;
        private float environmentIntensity;
        private float environmentDiffuseMultiplier;
        private float environmentValid;
        private bool hasEnvironmentReflectionCube;
        private bool writesScreenTraceDebug;

        private void Render(ComputeGraphContext context)
        {
            var cmd = context.cmd;
            cmd.SetRayTracingShaderPass(rayTracingShader, ShaderPassName);
            cmd.SetRayTracingTextureParam(rayTracingShader, probeRayDataID, probeRayData);
            cmd.SetRayTracingTextureParam(rayTracingShader, traceAlbedoID, traceAlbedo);
            cmd.SetRayTracingTextureParam(rayTracingShader, screenTraceDebugID, screenTraceDebug);
            cmd.SetRayTracingTextureParam(rayTracingShader, screenTraceDepthID, screenTraceDepth);
            if (writesScreenTraceDebug)
            {
                cmd.SetRayTracingMatrixParam(rayTracingShader, screenTraceInvViewProjectionID, inverseViewProjection);
                cmd.SetRayTracingVectorParam(rayTracingShader, screenTraceCameraPositionWSID, cameraPositionWS);
                cmd.SetRayTracingIntParam(rayTracingShader, screenTraceReversedZID, reversedZ);
                cmd.SetRayTracingIntParam(rayTracingShader, screenTraceProjectionFlipYID, projectionFlipY);
            }
            cmd.SetRayTracingTextureParam(rayTracingShader, probeIrradianceID, probeIrradiance);
            cmd.SetRayTracingTextureParam(rayTracingShader, probeDistanceID, probeDistance);
            cmd.SetRayTracingTextureParam(rayTracingShader, probeDataID, probeData);
            cmd.SetRayTracingAccelerationStructure(rayTracingShader, accelerationStructureID, rayTracingAccelerationStructure);
            cmd.SetRayTracingVectorParam(rayTracingShader, volumeMinWSID, volumeMinWS);
            cmd.SetRayTracingVectorParam(rayTracingShader, volumeMaxWSID, volumeMaxWS);
            cmd.SetRayTracingVectorParam(rayTracingShader, probeSpacingWSID, probeSpacingWS);
            cmd.SetRayTracingFloatParam(rayTracingShader, probeNormalBiasID, probeNormalBias);
            cmd.SetRayTracingFloatParam(rayTracingShader, probeViewBiasID, probeViewBias);
            cmd.SetRayTracingFloatParam(rayTracingShader, probeIrradianceEncodingGammaID, probeIrradianceEncodingGamma);
            cmd.SetRayTracingVectorParam(rayTracingShader, probeCountID,
                new Vector4(probeCount.x, probeCount.y, probeCount.z, 0.0f));
            cmd.SetRayTracingIntParam(rayTracingShader, raysPerProbeID, raysPerProbe);
            cmd.SetRayTracingFloatParam(rayTracingShader, probeMaxRayDistanceID, probeMaxRayDistance);
            cmd.SetRayTracingVectorParam(rayTracingShader, probeIrradianceDimensionsID, probeIrradianceDimensions);
            cmd.SetRayTracingVectorParam(rayTracingShader, probeDistanceDimensionsID, probeDistanceDimensions);
            cmd.SetRayTracingVectorParam(rayTracingShader, probeDataDimensionsID, probeDataDimensions);
            cmd.SetRayTracingFloatParam(rayTracingShader, probeRelocationEnabledID, probeRelocationEnabled);
            cmd.SetRayTracingBufferParam(rayTracingShader, traceInstanceTriangleRangesID, traceInstanceTriangleRanges);
            cmd.SetRayTracingBufferParam(rayTracingShader, traceTriangleNormalsID, traceTriangleNormals);
            cmd.SetRayTracingBufferParam(rayTracingShader, traceInstanceMaterialFlagsID, traceInstanceMaterialFlags);
            cmd.SetRayTracingVectorParam(rayTracingShader, directionalLightColorIlluminanceID,
                directionalLightColorIlluminance);
            cmd.SetRayTracingVectorParam(rayTracingShader, directionalLightDirectionWSID, directionalLightDirectionWS);
            cmd.SetRayTracingFloatParam(rayTracingShader, directionalLightVisibilityEnabledID,
                directionalLightVisibilityEnabled);
            cmd.SetRayTracingFloatParam(rayTracingShader, directionalLightLambertEnabledID,
                directionalLightLambertEnabled);
            if (environmentReflectionCube.IsValid())
            {
                cmd.SetRayTracingTextureParam(rayTracingShader, EnvironmentReflectionCubeName, environmentReflectionCube);
            }
            cmd.SetRayTracingVectorParam(rayTracingShader, environmentReflectionCubeHdrID, environmentReflectionCubeHdr);
            cmd.SetRayTracingFloatParam(rayTracingShader, environmentIntensityID, environmentIntensity);
            cmd.SetRayTracingFloatParam(rayTracingShader, environmentDiffuseMultiplierID, environmentDiffuseMultiplier);
            cmd.SetRayTracingFloatParam(rayTracingShader, environmentValidID, environmentValid);
            cmd.DispatchRays(rayTracingShader, RayGenName, (uint)raysPerProbe, (uint)planeProbeCount,
                (uint)probeCount.y, null);
            if (writesScreenTraceDebug)
            {
                cmd.DispatchRays(rayTracingShader, ScreenTraceRayGenName,
                    (uint)Mathf.Max(screenTraceWidth, 1), (uint)Mathf.Max(screenTraceHeight, 1), 1, null);
            }
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

        private void SetEnvironment(LightResources lightResources)
        {
            environmentReflectionCube = TextureHandle.nullHandle;
            environmentReflectionCubeHdr = Vector4.zero;
            environmentIntensity = 1.0f;
            environmentDiffuseMultiplier = 1.0f;
            environmentValid = 0.0f;
            hasEnvironmentReflectionCube = false;

            if (lightResources == null)
            {
                return;
            }

            environmentIntensity = Mathf.Max(0.0f, lightResources.environment_intensity);
            environmentDiffuseMultiplier = Mathf.Max(0.0f, lightResources.environment_diffuse_multiplier);
            if (!lightResources.has_environment_reflection ||
                !lightResources.environment_reflection_cube.IsValid() ||
                environmentIntensity <= 0.0f ||
                environmentDiffuseMultiplier <= 0.0f)
            {
                return;
            }

            environmentReflectionCube = lightResources.environment_reflection_cube;
            environmentReflectionCubeHdr = lightResources.environment_reflection_cube_hdr;
            hasEnvironmentReflectionCube = true;
            environmentValid = 1.0f;
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

        private static Matrix4x4 GetInverseViewProjection(Camera camera)
        {
            var view = camera.worldToCameraMatrix;
            var projection = GL.GetGPUProjectionMatrix(camera.projectionMatrix, true);
            return (projection * view).inverse;
        }

        private static int GetProjectionFlipY(Camera camera)
        {
            var projection = GL.GetGPUProjectionMatrix(camera.projectionMatrix, true);
            return projection.m11 < 0.0f ? 1 : 0;
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
            resources.probe_irradiance_texture = probeIrradianceRT.rt;
            resources.probe_distance_texture = probeDistanceRT.rt;
            resources.probe_data_texture = probeDataRT.rt;
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

        private static TextureHandle ImportFallbackEnvironmentReflection(RenderGraph renderGraph)
        {
            if (fallbackEnvironmentReflectionRT == null)
            {
                fallbackEnvironmentReflectionRT = RTHandles.Alloc(CoreUtils.blackCubeTexture);
            }

            return renderGraph.ImportTexture(fallbackEnvironmentReflectionRT);
        }

        private static TextureHandle CreateFallbackScreenTraceDebug(RenderGraph renderGraph)
        {
            var desc = new TextureDesc(1, 1)
            {
                colorFormat = GraphicsFormat.R16G16B16A16_SFloat,
                enableRandomWrite = true,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                clearBuffer = true,
                clearColor = Color.black,
                name = "DDGI Screen Trace Debug Fallback"
            };
            return renderGraph.CreateTexture(desc);
        }

        private static TextureHandle CreateFallbackScreenTraceDepth(RenderGraph renderGraph)
        {
            var desc = new TextureDesc(1, 1)
            {
                colorFormat = GraphicsFormat.R32_SFloat,
                enableRandomWrite = false,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                clearBuffer = true,
                clearColor = Color.clear,
                name = "DDGI Screen Trace Depth Fallback"
            };
            return renderGraph.CreateTexture(desc);
        }

        private static void ReleaseFallbackEnvironmentReflection()
        {
            RTHandles.Release(fallbackEnvironmentReflectionRT);
            fallbackEnvironmentReflectionRT = null;
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
            var instanceMaterialFlags = new List<uint>();
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

                if (!TryAppendTraceGeometry(renderer, triangleNormals, out var rendererSubMeshes, out reason))
                {
                    if (settings.logDiagnostics)
                    {
                        Debug.LogWarning($"YutrelRP DDGI ProbeTrace skipped Contribute GI renderer '{renderer.name}': {reason}.");
                    }

                    continue;
                }

                try
                {
                    var meshHasUV0 = HasMeshUV0(renderer);
                    foreach (var traceSubMesh in rendererSubMeshes)
                    {
                        if (traceSubMesh.triangleRange.y == 0u)
                        {
                            continue;
                        }

                        var material = GetSubMeshMaterial(renderer, traceSubMesh.subMeshIndex);
                        if (!TryGetSupportedTraceMaterial(material, out reason))
                        {
                            if (settings.logDiagnostics)
                            {
                                Debug.LogWarning($"YutrelRP DDGI ProbeTrace skipped submesh {traceSubMesh.subMeshIndex} on renderer '{renderer.name}': {reason}.");
                            }

                            continue;
                        }

                        var subMeshFlags = new RayTracingSubMeshFlags[Mathf.Max(1, rendererSubMeshes.Count)];
                        for (var i = 0; i < subMeshFlags.Length; i++)
                        {
                            subMeshFlags[i] = i == traceSubMesh.subMeshIndex
                                ? RayTracingSubMeshFlags.Enabled | RayTracingSubMeshFlags.ClosestHitOnly
                                : RayTracingSubMeshFlags.Disabled;
                        }

                        var handle = accelerationStructure.AddInstance(renderer, subMeshFlags: subMeshFlags, enableTriangleCulling: false,
                            frontTriangleCounterClockwise: false, mask: 0xFF, id: (uint)instanceCount);
                        if (handle == 0)
                        {
                            continue;
                        }

                        instanceRanges.Add(traceSubMesh.triangleRange);
                        instanceMaterialFlags.Add(GetTraceMaterialFlags(renderer, material, traceSubMesh.subMeshIndex,
                            meshHasUV0, settings.logDiagnostics));
                        instanceCount++;
                    }
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

            if (!EnsureTraceGeometryBuffers(instanceRanges, instanceMaterialFlags, triangleNormals))
            {
                ReleaseTraceGeometryBuffers();
                return ProbeTraceIssue.ResourceAllocationFailed;
            }

            accelerationStructure.Build();
            return accelerationStructure.GetInstanceCount() > 0 ? ProbeTraceIssue.None : ProbeTraceIssue.EmptyAccelerationStructure;
        }

        private static bool TryAppendTraceGeometry(MeshRenderer renderer, List<Vector4> triangleNormals,
            out List<TraceSubMesh> traceSubMeshes, out string reason)
        {
            reason = null;
            traceSubMeshes = null;
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

            var firstTriangleNormal = triangleNormals.Count;
            var subMeshCount = Mathf.Max(1, mesh.subMeshCount);
            var localToWorld = renderer.localToWorldMatrix;
            traceSubMeshes = new List<TraceSubMesh>(subMeshCount);

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
                    RemoveTraceGeometry(triangleNormals, firstTriangleNormal);
                    traceSubMeshes = null;
                    return false;
                }

                var normalBase = triangleNormals.Count;
                var triangleCount = indices != null ? indices.Length / 3 : 0;
                traceSubMeshes.Add(new TraceSubMesh(subMesh, new UInt2((uint)normalBase, (uint)triangleCount)));

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
                    var normal = Vector3.Cross(p2 - p0, p1 - p0);
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

            return triangleNormals.Count > firstTriangleNormal;
        }

        private static void RemoveTraceGeometry(List<Vector4> triangleNormals, int firstTriangleNormal)
        {
            if (triangleNormals.Count > firstTriangleNormal)
            {
                triangleNormals.RemoveRange(firstTriangleNormal, triangleNormals.Count - firstTriangleNormal);
            }
        }

        private static Material GetSubMeshMaterial(Renderer renderer, int subMeshIndex)
        {
            var materials = renderer.sharedMaterials;
            if (materials == null || materials.Length == 0)
            {
                return null;
            }

            var materialIndex = Mathf.Clamp(subMeshIndex, 0, materials.Length - 1);
            return materials[materialIndex];
        }

        private static bool TryGetSupportedTraceMaterial(Material material, out string reason)
        {
            reason = null;
            if (material == null)
            {
                reason = "material is missing";
                return false;
            }

            if (material.FindPass(ShaderPassName) < 0)
            {
                var shaderName = material.shader != null ? material.shader.name : "<missing shader>";
                reason = $"material '{material.name}' shader '{shaderName}' has no '{ShaderPassName}' ray tracing pass";
                return false;
            }

            return true;
        }

        private static uint GetTraceMaterialFlags(Renderer renderer, Material material, int subMeshIndex, bool meshHasUV0,
            bool logDiagnostics)
        {
            uint flags = 0u;
            var usesBaseColorTexture = MaterialUsesBaseColorTexture(material);

            if (meshHasUV0)
            {
                flags |= TraceMaterialHasUV0;
            }
            else if (usesBaseColorTexture && logDiagnostics)
            {
                Debug.LogWarning($"YutrelRP DDGI ProbeTrace constant albedo for submesh {subMeshIndex} on renderer '{renderer.name}': _BaseColorTex is enabled but mesh has no valid UV0.");
            }

            if (usesBaseColorTexture)
            {
                flags |= TraceMaterialHasBaseColorTexture;
            }

            return flags;
        }

        private static bool MaterialUsesBaseColorTexture(Material material)
        {
            if (material == null || !material.HasTexture(BaseColorTexID))
            {
                return false;
            }

            if (material.HasFloat(UseBaseColorTexID) && material.GetFloat(UseBaseColorTexID) <= 0.5f)
            {
                return false;
            }

            return material.GetTexture(BaseColorTexID) != null;
        }

        private static bool HasMeshUV0(MeshRenderer renderer)
        {
            return renderer != null &&
                   renderer.TryGetComponent<MeshFilter>(out var meshFilter) &&
                   meshFilter.sharedMesh != null &&
                   meshFilter.sharedMesh.HasVertexAttribute(VertexAttribute.TexCoord0);
        }

        private static bool EnsureTraceGeometryBuffers(List<UInt2> instanceRanges, List<uint> instanceMaterialFlags,
            List<Vector4> triangleNormals)
        {
            if (instanceRanges.Count == 0 || instanceMaterialFlags.Count != instanceRanges.Count ||
                triangleNormals.Count == 0)
            {
                return false;
            }

            traceInstanceTriangleRangesBuffer = AllocStructuredBuffer(instanceRanges.Count, Marshal.SizeOf<UInt2>(),
                "DDGI Trace Instance Triangle Ranges");
            traceTriangleNormalsBuffer = AllocStructuredBuffer(triangleNormals.Count, Marshal.SizeOf<Vector4>(),
                "DDGI Trace Triangle Normals");
            traceInstanceMaterialFlagsBuffer = AllocStructuredBuffer(instanceMaterialFlags.Count, Marshal.SizeOf<uint>(),
                "DDGI Trace Instance Material Flags");
            traceInstanceTriangleRangesBuffer.SetData(instanceRanges);
            traceTriangleNormalsBuffer.SetData(triangleNormals);
            traceInstanceMaterialFlagsBuffer.SetData(instanceMaterialFlags);
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
            traceInstanceTriangleRangesBuffer?.Dispose();
            traceTriangleNormalsBuffer?.Dispose();
            traceInstanceMaterialFlagsBuffer?.Dispose();
            traceInstanceTriangleRangesBuffer = null;
            traceTriangleNormalsBuffer = null;
            traceInstanceMaterialFlagsBuffer = null;
        }

        private static bool IsDDGIContributeGIRenderer(Renderer renderer)
        {
            if (renderer == null)
            {
                return false;
            }

#if UNITY_EDITOR
            for (var transform = renderer.transform; transform != null; transform = transform.parent)
            {
                var flags = GameObjectUtility.GetStaticEditorFlags(transform.gameObject);
                if ((flags & StaticEditorFlags.ContributeGI) != 0)
                {
                    return true;
                }
            }

            return false;
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

        private static void SetDiagnostic(ref DDGIResources resources, ProbeTraceIssue issue)
        {
            if (resources != null && issue != ProbeTraceIssue.None)
            {
                resources.diagnostic = GetReason(issue);
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
            ReleaseFallbackEnvironmentReflection();
            DDGIProbeRelocationPass.Cleanup();
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

        private readonly struct TraceSubMesh
        {
            public readonly int subMeshIndex;
            public readonly UInt2 triangleRange;

            public TraceSubMesh(int subMeshIndex, UInt2 triangleRange)
            {
                this.subMeshIndex = subMeshIndex;
                this.triangleRange = triangleRange;
            }
        }
    }
}
