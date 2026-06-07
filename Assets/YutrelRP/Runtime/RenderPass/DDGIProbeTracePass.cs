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
        private const int FixedRayCount = 32;
        private const GraphicsFormat ProbeIrradianceFormat = DDGIResources.ProbeIrradianceGraphicsFormat;

        private static readonly ProfilingSampler sampler = new("DDGI Probe Trace");
        private static readonly int accelerationStructureID = Shader.PropertyToID("_RaytracingAccelerationStructure");
        private static readonly int probeRayDataID = DDGIResources.probe_ray_data_ID;
        private static readonly int probeRayDataFormatID = DDGIResources.probe_ray_data_format_ID;
        private static readonly int probeRayRotationRow0ID = DDGIResources.probe_ray_rotation_row0_ID;
        private static readonly int probeRayRotationRow1ID = DDGIResources.probe_ray_rotation_row1_ID;
        private static readonly int probeRayRotationRow2ID = DDGIResources.probe_ray_rotation_row2_ID;
        private static readonly int probeRandomRotationEnabledID = DDGIResources.probe_random_rotation_enabled_ID;
        private static readonly int probeFixedRaysEnabledID = DDGIResources.probe_fixed_rays_enabled_ID;
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
        private static readonly int probeIrradianceFormatID = DDGIResources.probe_irradiance_format_ID;
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
        private static readonly int traceAlbedoID = DDGIResources.trace_albedo_ID;
        private static readonly int screenTraceDebugID = DDGIResources.screen_trace_debug_ID;
        private static readonly int screenTraceDepthID = Shader.PropertyToID("_DDGIScreenTraceDepth");
        private static readonly int screenTraceInvViewProjectionID = Shader.PropertyToID("_DDGIScreenTraceInvViewProjection");
        private static readonly int screenTraceCameraPositionWSID = Shader.PropertyToID("_DDGIScreenTraceCameraPositionWS");
        private static readonly int screenTraceReversedZID = Shader.PropertyToID("_DDGIScreenTraceReversedZ");
        private static readonly int screenTraceProjectionFlipYID = Shader.PropertyToID("_DDGIScreenTraceProjectionFlipY");

        private static RayTracingShader shader;
        private static RayTracingAccelerationStructure accelerationStructure;
        private static RTHandle probeIrradianceHistoryRT;
        private static RTHandle probeIrradianceWriteRT;
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
                issue = ValidateProbeRayDataFormat(volume);
            }
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
            var probeRayDataFormat = DDGIResources.ProbeRayDataFormat(volume.RayDataFormat);
            var probeRayDataGraphicsFormat = DDGIResources.ProbeRayDataGraphicsFormat(volume.RayDataFormat);
            var desc = new TextureDesc(raysPerProbe, planeProbeCount)
            {
                colorFormat = probeRayDataGraphicsFormat,
                dimension = TextureDimension.Tex2DArray,
                slices = probeCount.y,
                enableRandomWrite = true,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                clearBuffer = true,
                clearColor = ProbeRayDataClearColor(probeRayDataFormat),
                name = "DDGI ProbeRayData"
            };

            var probeRayData = renderGraph.CreateTexture(desc);
            resources.probe_ray_data = probeRayData;

            desc.name = "DDGI TraceAlbedo";
            desc.colorFormat = GraphicsFormat.R16G16B16A16_SFloat;
            desc.clearColor = Color.black;
            var traceAlbedo = renderGraph.CreateTexture(desc);
            resources.trace_albedo = traceAlbedo;
            resources.SetVolumeMetadata(volume);
            resources.probe_relocation_enabled = settings.probeRelocationEnabled;
            var probeRayRotation = ComputeProbeRayRotation(volume, settings);
            resources.SetProbeRayRotation(probeRayRotation.row0, probeRayRotation.row1, probeRayRotation.row2,
                probeRayRotation.randomRotationEnabled, probeRayRotation.fixedRaysEnabled,
                probeRayRotation.skipFixedRaysForBlend);

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
                pass.probeRayDataFormat = probeRayDataFormat;
                pass.planeProbeCount = planeProbeCount;
                pass.probeMaxRayDistance = volume.ProbeMaxRayDistance;
                pass.probeIrradianceDimensions = resources.ProbeIrradianceDimensions;
                pass.probeDistanceDimensions = resources.ProbeDistanceDimensions;
                pass.probeDataDimensions = resources.ProbeDataDimensions;
                pass.probeRelocationEnabled = settings.probeRelocationEnabled ? 1.0f : 0.0f;
                pass.probeRayRotationRow0 = resources.probe_ray_rotation_row0;
                pass.probeRayRotationRow1 = resources.probe_ray_rotation_row1;
                pass.probeRayRotationRow2 = resources.probe_ray_rotation_row2;
                pass.probeRandomRotationEnabled = resources.probe_random_rotation_enabled;
                pass.probeFixedRaysEnabled = resources.probe_fixed_rays_enabled ? 1.0f : 0.0f;
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
            if (resources.has_gather_data)
            {
                PublishProbeIrradianceWriteAtlas(ref resources);
            }
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
        private int probeRayDataFormat;
        private int planeProbeCount;
        private float probeMaxRayDistance;
        private Vector4 probeIrradianceDimensions;
        private Vector4 probeDistanceDimensions;
        private Vector4 probeDataDimensions;
        private float probeRelocationEnabled;
        private Vector4 probeRayRotationRow0;
        private Vector4 probeRayRotationRow1;
        private Vector4 probeRayRotationRow2;
        private float probeRandomRotationEnabled;
        private float probeFixedRaysEnabled;
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
            cmd.BuildRayTracingAccelerationStructure(rayTracingAccelerationStructure);
            cmd.SetRayTracingAccelerationStructure(rayTracingShader, accelerationStructureID, rayTracingAccelerationStructure);
            cmd.SetRayTracingVectorParam(rayTracingShader, volumeMinWSID, volumeMinWS);
            cmd.SetRayTracingVectorParam(rayTracingShader, volumeMaxWSID, volumeMaxWS);
            cmd.SetRayTracingVectorParam(rayTracingShader, probeSpacingWSID, probeSpacingWS);
            cmd.SetRayTracingFloatParam(rayTracingShader, probeNormalBiasID, probeNormalBias);
            cmd.SetRayTracingFloatParam(rayTracingShader, probeViewBiasID, probeViewBias);
            cmd.SetRayTracingFloatParam(rayTracingShader, probeIrradianceEncodingGammaID, probeIrradianceEncodingGamma);
            cmd.SetRayTracingIntParam(rayTracingShader, probeIrradianceFormatID, DDGIResources.ProbeIrradianceFormatU32);
            cmd.SetRayTracingVectorParam(rayTracingShader, probeCountID,
                new Vector4(probeCount.x, probeCount.y, probeCount.z, 0.0f));
            cmd.SetRayTracingIntParam(rayTracingShader, raysPerProbeID, raysPerProbe);
            cmd.SetRayTracingIntParam(rayTracingShader, probeRayDataFormatID, probeRayDataFormat);
            cmd.SetRayTracingFloatParam(rayTracingShader, probeMaxRayDistanceID, probeMaxRayDistance);
            cmd.SetRayTracingVectorParam(rayTracingShader, probeIrradianceDimensionsID, probeIrradianceDimensions);
            cmd.SetRayTracingVectorParam(rayTracingShader, probeDistanceDimensionsID, probeDistanceDimensions);
            cmd.SetRayTracingVectorParam(rayTracingShader, probeDataDimensionsID, probeDataDimensions);
            cmd.SetRayTracingFloatParam(rayTracingShader, probeRelocationEnabledID, probeRelocationEnabled);
            cmd.SetRayTracingVectorParam(rayTracingShader, probeRayRotationRow0ID, probeRayRotationRow0);
            cmd.SetRayTracingVectorParam(rayTracingShader, probeRayRotationRow1ID, probeRayRotationRow1);
            cmd.SetRayTracingVectorParam(rayTracingShader, probeRayRotationRow2ID, probeRayRotationRow2);
            cmd.SetRayTracingFloatParam(rayTracingShader, probeRandomRotationEnabledID, probeRandomRotationEnabled);
            cmd.SetRayTracingFloatParam(rayTracingShader, probeFixedRaysEnabledID, probeFixedRaysEnabled);
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

            if (!SupportsProbeIrradianceFormat())
            {
                return ProbeTraceIssue.UnsupportedProbeIrradianceFormat;
            }

            return ProbeTraceIssue.None;
        }

        private static ProbeTraceIssue ValidateProbeRayDataFormat(YutrelDDGIVolume volume)
        {
            if (volume == null)
            {
                return ProbeTraceIssue.MissingVolume;
            }

            var format = DDGIResources.ProbeRayDataGraphicsFormat(volume.RayDataFormat);
            return SystemInfo.IsFormatSupported(format, GraphicsFormatUsage.Sample | GraphicsFormatUsage.LoadStore)
                ? ProbeTraceIssue.None
                : ProbeTraceIssue.UnsupportedProbeRayDataFormat;
        }

        private static Color ProbeRayDataClearColor(int format)
        {
            return format == DDGIResources.ProbeRayDataFormatF32x4
                ? new Color(0.0f, 0.0f, 0.0f, 1.0e27f)
                : new Color(0.0f, 1.0e27f, 0.0f, 0.0f);
        }

        private static bool SupportsProbeIrradianceFormat()
        {
            return SupportsProbeIrradianceRenderTargetFormat();
        }

        private static bool SupportsProbeIrradianceSampleFormat()
        {
            return SystemInfo.IsFormatSupported(ProbeIrradianceFormat, GraphicsFormatUsage.Sample);
        }

        private static bool SupportsProbeIrradianceRenderTargetFormat()
        {
            // Unity's URP checks Blend for UNorm render textures because direct Render support can
            // reject A2B10G10R10 on D3D12 even when it is usable as a render target. The Sample
            // usage query is also unreliable for this packed render texture, so do not use it as
            // a startup blocker; shader SRV binding is validated by actual RenderGraph usage.
            return SystemInfo.IsFormatSupported(ProbeIrradianceFormat, GraphicsFormatUsage.Blend);
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

        private static ProbeRayRotation ComputeProbeRayRotation(YutrelDDGIVolume volume,
            YutrelRPSettings.DDGISettings settings)
        {
            var fixedRaysEnabled = settings != null && settings.probeRelocationEnabled;
            var randomRotationEnabled = settings != null && settings.probeRandomRotationEnabled &&
                                        volume != null && volume.RaysPerProbe > FixedRayCount;
            var skipFixedRaysForBlend = fixedRaysEnabled;
            if (!randomRotationEnabled)
            {
                return ProbeRayRotation.Identity(fixedRaysEnabled, skipFixedRaysForBlend);
            }

            var random = RandomState((uint)Time.frameCount, (uint)GetStableVolumeKey(volume));
            var u1 = 2.0f * Mathf.PI * NextFloat01(ref random);
            var u2 = 2.0f * Mathf.PI * NextFloat01(ref random);
            var u3 = NextFloat01(ref random);

            var cos1 = Mathf.Cos(u1);
            var sin1 = Mathf.Sin(u1);
            var cos2 = Mathf.Cos(u2);
            var sin2 = Mathf.Sin(u2);
            var sq3 = 2.0f * Mathf.Sqrt(u3 * (1.0f - u3));
            var s2 = 2.0f * u3 * sin2 * sin2 - 1.0f;
            var c2 = 2.0f * u3 * cos2 * cos2 - 1.0f;
            var sc = 2.0f * u3 * sin2 * cos2;

            return new ProbeRayRotation(
                new Vector4(cos1 * c2 - sin1 * sc, sin1 * c2 + cos1 * sc, sq3 * cos2, 0.0f),
                new Vector4(cos1 * sc - sin1 * s2, sin1 * sc + cos1 * s2, sq3 * sin2, 0.0f),
                new Vector4(cos1 * (sq3 * cos2) - sin1 * (sq3 * sin2),
                    sin1 * (sq3 * cos2) + cos1 * (sq3 * sin2), 1.0f - 2.0f * u3, 0.0f),
                true,
                fixedRaysEnabled,
                skipFixedRaysForBlend);
        }

        private static uint RandomState(uint frameIndex, uint volumeKey)
        {
            var state = frameIndex * 747796405u + volumeKey * 2891336453u + 277803737u;
            return state != 0u ? state : 1u;
        }

        private static float NextFloat01(ref uint state)
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            return (state & 0x00FFFFFFu) * (1.0f / 16777216.0f);
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
                probeIrradianceHistoryRT == null || probeIrradianceHistoryRT.rt == null ||
                probeIrradianceHistoryRT.rt.graphicsFormat != ProbeIrradianceFormat ||
                probeIrradianceWriteRT == null || probeIrradianceWriteRT.rt == null ||
                probeIrradianceWriteRT.rt.graphicsFormat != ProbeIrradianceFormat ||
                probeDistanceRT == null || probeDataRT == null)
            {
                ReleasePersistentAtlases();
                try
                {
                    probeIrradianceHistoryRT = AllocAtlasRT(
                        volume.ProbeCount.x * (volume.ProbeIrradianceInteriorTexels + 2),
                        volume.ProbeCount.z * (volume.ProbeIrradianceInteriorTexels + 2),
                        volume.ProbeCount.y,
                        ProbeIrradianceFormat,
                        "DDGI ProbeIrradiance History",
                        FilterMode.Bilinear,
                        false);
                    probeIrradianceWriteRT = AllocAtlasRT(
                        volume.ProbeCount.x * (volume.ProbeIrradianceInteriorTexels + 2),
                        volume.ProbeCount.z * (volume.ProbeIrradianceInteriorTexels + 2),
                        volume.ProbeCount.y,
                        ProbeIrradianceFormat,
                        "DDGI ProbeIrradiance Write",
                        FilterMode.Bilinear,
                        false);
                    probeDistanceRT = AllocAtlasRT(
                        volume.ProbeCount.x * (volume.ProbeDistanceInteriorTexels + 2),
                        volume.ProbeCount.z * (volume.ProbeDistanceInteriorTexels + 2),
                        volume.ProbeCount.y,
                        GraphicsFormat.R16G16B16A16_SFloat,
                        "DDGI ProbeDistance",
                        FilterMode.Bilinear);
                    probeDataRT = AllocAtlasRT(
                        volume.ProbeCount.x,
                        volume.ProbeCount.z,
                        volume.ProbeCount.y,
                        GraphicsFormat.R16G16B16A16_SFloat,
                        "DDGI ProbeData",
                        FilterMode.Point);

                    ClearPersistentAtlas(probeIrradianceHistoryRT, new Color(0.0f, 0.0f, 0.0f, 1.0f));
                    ClearPersistentAtlas(probeIrradianceWriteRT, new Color(0.0f, 0.0f, 0.0f, 1.0f));
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
            resources.probe_irradiance_history = renderGraph.ImportTexture(probeIrradianceHistoryRT);
            resources.probe_irradiance_write = renderGraph.ImportTexture(probeIrradianceWriteRT);
            resources.probe_irradiance = resources.probe_irradiance_history;
            resources.probe_distance = renderGraph.ImportTexture(probeDistanceRT);
            resources.probe_data = renderGraph.ImportTexture(probeDataRT);
            resources.probe_irradiance_texture = probeIrradianceHistoryRT.rt;
            resources.probe_irradiance_write_texture = probeIrradianceWriteRT.rt;
            resources.probe_distance_texture = probeDistanceRT.rt;
            resources.probe_data_texture = probeDataRT.rt;
            resources.has_persistent_atlas = resources.probe_irradiance_history.IsValid() &&
                                             resources.probe_irradiance_write.IsValid() &&
                                             resources.probe_distance.IsValid() &&
                                             resources.probe_data.IsValid();
            return resources.has_persistent_atlas ? ProbeTraceIssue.None : ProbeTraceIssue.ResourceAllocationFailed;
        }

        private static RTHandle AllocAtlasRT(int width, int height, int slices, GraphicsFormat format, string name,
            FilterMode filterMode, bool enableRandomWrite = true)
        {
            return RTHandles.Alloc(width, height, slices: slices, dimension: TextureDimension.Tex2DArray,
                colorFormat: format, enableRandomWrite: enableRandomWrite, filterMode: filterMode,
                wrapMode: TextureWrapMode.Clamp, name: name);
        }

        private static void PublishProbeIrradianceWriteAtlas(ref DDGIResources resources)
        {
            if (resources == null ||
                !resources.probe_irradiance_write.IsValid() ||
                resources.probe_irradiance_write_texture == null)
            {
                return;
            }

            resources.probe_irradiance = resources.probe_irradiance_write;
            resources.probe_irradiance_texture = resources.probe_irradiance_write_texture;
            if (probeIrradianceHistoryRT != null &&
                probeIrradianceWriteRT != null)
            {
                (probeIrradianceHistoryRT, probeIrradianceWriteRT) = (probeIrradianceWriteRT, probeIrradianceHistoryRT);
            }
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
            RTHandles.Release(probeIrradianceHistoryRT);
            RTHandles.Release(probeIrradianceWriteRT);
            RTHandles.Release(probeDistanceRT);
            RTHandles.Release(probeDataRT);
            probeIrradianceHistoryRT = null;
            probeIrradianceWriteRT = null;
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
                if (!TryGetEligibleRenderer(renderer, out var reason))
                {
                    LogSkippedRenderer(settings, renderer, reason);
                    continue;
                }

                if (!TryBuildSubMeshFlags(renderer, settings, out var subMeshFlags))
                {
                    LogSkippedRenderer(settings, renderer, "no submesh has a material with DDGIRayTracing pass");
                    continue;
                }

                try
                {
                    var handle = accelerationStructure.AddInstance(renderer, subMeshFlags: subMeshFlags,
                        enableTriangleCulling: false, frontTriangleCounterClockwise: false, mask: 0xFF,
                        id: (uint)instanceCount);
                    if (handle == 0)
                    {
                        LogSkippedRenderer(settings, renderer, "RTAS AddInstance returned an invalid handle");
                        continue;
                    }

                    instanceCount++;
                }
                catch (System.Exception exception)
                {
                    Debug.LogWarning($"YutrelRP DDGI ProbeTrace RTAS AddInstance failed for '{renderer.name}': {exception.Message}");
                }
            }

            return instanceCount > 0 ? ProbeTraceIssue.None : ProbeTraceIssue.NoContributors;
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

        private static bool TryBuildSubMeshFlags(MeshRenderer renderer, YutrelRPSettings.DDGISettings settings,
            out RayTracingSubMeshFlags[] subMeshFlags)
        {
            var subMeshCount = GetRendererSubMeshCount(renderer);
            subMeshFlags = new RayTracingSubMeshFlags[Mathf.Max(1, subMeshCount)];
            var supportedSubMeshCount = 0;

            for (var subMesh = 0; subMesh < subMeshFlags.Length; subMesh++)
            {
                var material = GetSubMeshMaterial(renderer, subMesh);
                if (TryGetSupportedTraceMaterial(material, out var reason))
                {
                    subMeshFlags[subMesh] = RayTracingSubMeshFlags.Enabled | RayTracingSubMeshFlags.ClosestHitOnly;
                    supportedSubMeshCount++;
                }
                else
                {
                    subMeshFlags[subMesh] = RayTracingSubMeshFlags.Disabled;
                    LogSkippedSubMesh(settings, renderer, subMesh, reason);
                }
            }

            return supportedSubMeshCount > 0;
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

            if (renderer.rayTracingMode == RayTracingMode.Off)
            {
                reason = "renderer RayTracingMode is Off";
                return false;
            }

            if (HasTransparentMaterial(renderer))
            {
                reason = "transparent materials are not part of first-stage DDGI capture";
                return false;
            }

            return true;
        }

        private static int GetRendererSubMeshCount(MeshRenderer renderer)
        {
            if (renderer == null ||
                !renderer.TryGetComponent<MeshFilter>(out var meshFilter) ||
                meshFilter.sharedMesh == null)
            {
                return 0;
            }

            return Mathf.Max(1, meshFilter.sharedMesh.subMeshCount);
        }

        private static void LogSkippedRenderer(YutrelRPSettings.DDGISettings settings, Renderer renderer, string reason)
        {
            if (settings == null || !settings.logDiagnostics)
            {
                return;
            }

            var rendererName = renderer != null ? renderer.name : "<missing renderer>";
            Debug.LogWarning($"YutrelRP DDGI ProbeTrace skipped renderer '{rendererName}': {reason}.");
        }

        private static void LogSkippedSubMesh(YutrelRPSettings.DDGISettings settings, Renderer renderer,
            int subMeshIndex, string reason)
        {
            if (settings == null || !settings.logDiagnostics)
            {
                return;
            }

            var rendererName = renderer != null ? renderer.name : "<missing renderer>";
            Debug.LogWarning($"YutrelRP DDGI ProbeTrace skipped submesh {subMeshIndex} on renderer '{rendererName}': {reason}.");
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
                ? $"{GetStableVolumeKey(volume)}:{volume.ProbeCount}:{volume.RaysPerProbe}:{volume.RayDataFormat}:{volume.ProbeMaxRayDistance}"
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
                case ProbeTraceIssue.UnsupportedProbeRayDataFormat:
                case ProbeTraceIssue.UnsupportedProbeIrradianceFormat:
                    return "resource/format";
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

        private readonly struct ProbeRayRotation
        {
            public readonly Vector4 row0;
            public readonly Vector4 row1;
            public readonly Vector4 row2;
            public readonly bool randomRotationEnabled;
            public readonly bool fixedRaysEnabled;
            public readonly bool skipFixedRaysForBlend;

            public ProbeRayRotation(Vector4 row0, Vector4 row1, Vector4 row2, bool randomRotationEnabled,
                bool fixedRaysEnabled, bool skipFixedRaysForBlend)
            {
                this.row0 = row0;
                this.row1 = row1;
                this.row2 = row2;
                this.randomRotationEnabled = randomRotationEnabled;
                this.fixedRaysEnabled = fixedRaysEnabled;
                this.skipFixedRaysForBlend = skipFixedRaysForBlend;
            }

            public static ProbeRayRotation Identity(bool fixedRaysEnabled, bool skipFixedRaysForBlend)
            {
                return new ProbeRayRotation(
                    new Vector4(1.0f, 0.0f, 0.0f, 0.0f),
                    new Vector4(0.0f, 1.0f, 0.0f, 0.0f),
                    new Vector4(0.0f, 0.0f, 1.0f, 0.0f),
                    false,
                    fixedRaysEnabled,
                    skipFixedRaysForBlend);
            }
        }

        private static string GetReason(ProbeTraceIssue issue)
        {
            switch (issue)
            {
                case ProbeTraceIssue.UnsupportedGraphicsAPI:
                    return "DDGI probe trace first version requires Direct3D12";
                case ProbeTraceIssue.UnsupportedRayTracing:
                    return "SystemInfo.supportsRayTracing is false";
                case ProbeTraceIssue.UnsupportedProbeRayDataFormat:
                    return "DDGI ProbeRayData requires the selected RTXGI F32x2/F32x4 GraphicsFormat with sample and load/store support";
                case ProbeTraceIssue.UnsupportedProbeIrradianceFormat:
                    return $"DDGI ProbeIrradiance requires {ProbeIrradianceFormat} ({DDGIResources.ProbeIrradianceStorageFormatName}) with render-target/blend support for RTXGI U32 parity (blend={SupportsProbeIrradianceRenderTargetFormat()}, render={SystemInfo.IsFormatSupported(ProbeIrradianceFormat, GraphicsFormatUsage.Render)}, sampleQuery={SupportsProbeIrradianceSampleFormat()})";
                case ProbeTraceIssue.MissingVolume:
                    return "no active YutrelDDGIVolume was found";
                case ProbeTraceIssue.InvalidVolume:
                    return "active DDGI Volume has invalid bounds, probe counts, ray count, or ray distance";
                case ProbeTraceIssue.MissingRayTracingShader:
                    return "YutrelRPAsset DDGI probeTraceShader is missing or invalid";
                case ProbeTraceIssue.NoContributors:
                    return "no enabled opaque MeshRenderer with RayTracingMode enabled and a DDGIRayTracing material pass was found";
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
            ResourceAllocationFailed = 10,
            UnsupportedProbeRayDataFormat = 11,
            UnsupportedProbeIrradianceFormat = 12
        }
    }
}
