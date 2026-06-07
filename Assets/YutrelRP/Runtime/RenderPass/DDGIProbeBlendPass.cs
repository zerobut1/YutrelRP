using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace YutrelRP
{
    internal sealed class DDGIProbeBlendPass
    {
        private const string IrradianceShaderName = "YutrelRP/DDGIProbeIrradianceBlend";
        private const string DistanceKernelName = "BlendDistance";
        private const string DistanceBorderKernelName = "BlendDistanceBorder";
        private const int ThreadGroupSizeX = 8;
        private const int ThreadGroupSizeY = 8;

        private static readonly ProfilingSampler irradianceSampler = new("DDGI Probe Irradiance Blend");
        private static readonly ProfilingSampler distanceSampler = new("DDGI Probe Distance Blend");
        private static readonly int probeRayDataID = DDGIResources.probe_ray_data_ID;
        private static readonly int probeRayDataFormatID = DDGIResources.probe_ray_data_format_ID;
        private static readonly int probeRayRadianceMaxID = DDGIResources.probe_ray_radiance_max_ID;
        private static readonly int probeIrradianceHistoryID = Shader.PropertyToID("_DDGIProbeIrradianceHistory");
        private static readonly int probeDistanceID = DDGIResources.probe_distance_ID;
        private static readonly int probeRayRotationRow0ID = DDGIResources.probe_ray_rotation_row0_ID;
        private static readonly int probeRayRotationRow1ID = DDGIResources.probe_ray_rotation_row1_ID;
        private static readonly int probeRayRotationRow2ID = DDGIResources.probe_ray_rotation_row2_ID;
        private static readonly int probeRandomRotationEnabledID = DDGIResources.probe_random_rotation_enabled_ID;
        private static readonly int probeFixedRaysEnabledID = DDGIResources.probe_fixed_rays_enabled_ID;
        private static readonly int probeSkipFixedRaysForBlendID =
            DDGIResources.probe_skip_fixed_rays_for_blend_ID;
        private static readonly int probeCountID = Shader.PropertyToID("_DDGIProbeCount");
        private static readonly int probeRayDataDimensionsID = DDGIResources.probe_ray_data_dimensions_ID;
        private static readonly int probeIrradianceDimensionsID = DDGIResources.probe_irradiance_dimensions_ID;
        private static readonly int probeDistanceDimensionsID = DDGIResources.probe_distance_dimensions_ID;
        private static readonly int probeRayDataMaxDistanceID = DDGIResources.probe_ray_data_max_distance_ID;
        private static readonly int probeSpacingWSID = DDGIResources.probe_spacing_ws_ID;
        private static readonly int probeHysteresisID = Shader.PropertyToID("_DDGIProbeHysteresis");
        private static readonly int probeIrradianceEncodingGammaID = DDGIResources.probe_irradiance_encoding_gamma_ID;
        private static readonly int probeIrradianceFormatID = DDGIResources.probe_irradiance_format_ID;
        private static readonly int probeIrradianceThresholdID = Shader.PropertyToID("_DDGIProbeIrradianceThreshold");
        private static readonly int probeBrightnessThresholdID = Shader.PropertyToID("_DDGIProbeBrightnessThreshold");
        private static readonly int probeDistanceExponentID = Shader.PropertyToID("_DDGIProbeDistanceExponent");
        private static readonly int probeRandomRayBackfaceThresholdID =
            DDGIResources.probe_random_ray_backface_threshold_ID;
        private static readonly int probeBlendSliceID = Shader.PropertyToID("_DDGIProbeBlendSlice");

        private static ComputeShader shader;
        private static Material irradianceMaterial;
        private static MaterialPropertyBlock propertyBlock;
        private static int distanceKernel = -1;
        private static int distanceBorderKernel = -1;
        private static string lastStatusKey;

        internal static void Record(RenderGraph renderGraph, YutrelDDGIVolume volume,
            YutrelRPSettings.DDGISettings settings, ref DDGIResources resources)
        {
            var issue = Validate(resources, volume);
            if (issue == ProbeBlendIssue.None)
            {
                issue = ValidateIrradianceShaderResource();
            }
            if (issue == ProbeBlendIssue.None)
            {
                issue = ValidateComputeShaderResource(settings);
            }

            LogStatus(issue, volume, settings != null && settings.logDiagnostics);
            if (issue != ProbeBlendIssue.None)
            {
                resources.has_gather_data = false;
                resources.diagnostic = GetReason(issue);
                return;
            }

            resources.has_gather_data = true;

            for (var slice = 0; slice < resources.probe_count.y; slice++)
            {
                using var builder = renderGraph.AddRasterRenderPass<DDGIProbeBlendPass>(
                    $"{irradianceSampler.name} Slice {slice}", out var pass, irradianceSampler);
                ConfigureCommonPassData(pass, volume, settings, resources);
                pass.probeRayData = resources.probe_ray_data;
                pass.probeIrradianceHistory = resources.probe_irradiance_history;
                pass.probeIrradianceWrite = resources.probe_irradiance_write;
                pass.material = irradianceMaterial;
                pass.probeBlendSlice = slice;

                builder.UseTexture(pass.probeRayData, AccessFlags.Read);
                builder.UseTexture(pass.probeIrradianceHistory, AccessFlags.Read);
                builder.SetRenderAttachment(pass.probeIrradianceWrite, 0, AccessFlags.WriteAll, 0, slice);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc<DDGIProbeBlendPass>(static (pass, context) => pass.RenderIrradiance(context));
            }

            using (var builder = renderGraph.AddComputePass<DDGIProbeBlendPass>(
                       distanceSampler.name, out var pass, distanceSampler))
            {
                ConfigureCommonPassData(pass, volume, settings, resources);
                pass.probeRayData = resources.probe_ray_data;
                pass.probeDistance = resources.probe_distance;
                pass.computeShader = shader;

                builder.UseTexture(pass.probeRayData, AccessFlags.Read);
                builder.UseTexture(pass.probeDistance, AccessFlags.ReadWrite);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc<DDGIProbeBlendPass>(static (pass, context) => pass.RenderDistance(context));
            }
        }

        private TextureHandle probeRayData;
        private TextureHandle probeIrradianceHistory;
        private TextureHandle probeIrradianceWrite;
        private TextureHandle probeDistance;
        private ComputeShader computeShader;
        private Material material;
        private int probeBlendSlice;
        private Vector3Int probeCount;
        private int probeRayDataFormat;
        private float probeRayRadianceMax;
        private Vector4 probeRayDataDimensions;
        private Vector4 probeIrradianceDimensions;
        private Vector4 probeDistanceDimensions;
        private float probeMaxRayDistance;
        private Vector3 probeSpacingWS;
        private float probeHysteresis;
        private float probeIrradianceEncodingGamma;
        private float probeIrradianceThreshold;
        private float probeBrightnessThreshold;
        private float probeDistanceExponent;
        private float probeRandomRayBackfaceThreshold;
        private Vector4 probeRayRotationRow0;
        private Vector4 probeRayRotationRow1;
        private Vector4 probeRayRotationRow2;
        private float probeRandomRotationEnabled;
        private float probeFixedRaysEnabled;
        private float probeSkipFixedRaysForBlend;

        private void RenderIrradiance(RasterGraphContext context)
        {
            if (propertyBlock == null)
            {
                propertyBlock = new MaterialPropertyBlock();
            }
            propertyBlock.Clear();
            SetCommonMaterialParameters(propertyBlock);
            propertyBlock.SetTexture(probeRayDataID, probeRayData);
            propertyBlock.SetTexture(probeIrradianceHistoryID, probeIrradianceHistory);
            propertyBlock.SetInteger(probeBlendSliceID, probeBlendSlice);

            CoreUtils.DrawFullScreen(context.cmd, material, propertyBlock);
        }

        private void RenderDistance(ComputeGraphContext context)
        {
            var cmd = context.cmd;
            SetCommonComputeParameters(cmd, distanceKernel);
            SetCommonComputeParameters(cmd, distanceBorderKernel);
            cmd.SetComputeTextureParam(computeShader, distanceKernel, probeDistanceID, probeDistance);
            cmd.SetComputeTextureParam(computeShader, distanceBorderKernel, probeDistanceID, probeDistance);

            cmd.DispatchCompute(computeShader, distanceKernel,
                DivRoundUp((int)probeDistanceDimensions.x, ThreadGroupSizeX),
                DivRoundUp((int)probeDistanceDimensions.y, ThreadGroupSizeY),
                Mathf.Max(1, (int)probeDistanceDimensions.z));

            cmd.DispatchCompute(computeShader, distanceBorderKernel,
                DivRoundUp((int)probeDistanceDimensions.x, ThreadGroupSizeX),
                DivRoundUp((int)probeDistanceDimensions.y, ThreadGroupSizeY),
                Mathf.Max(1, (int)probeDistanceDimensions.z));
        }

        private static void ConfigureCommonPassData(DDGIProbeBlendPass pass, YutrelDDGIVolume volume,
            YutrelRPSettings.DDGISettings settings, DDGIResources resources)
        {
            pass.probeCount = resources.probe_count;
            pass.probeRayDataFormat = resources.probe_ray_data_format;
            pass.probeRayRadianceMax = Mathf.Max(YutrelDDGIVolume.MinProbeRayRadianceMax, resources.probe_ray_radiance_max);
            pass.probeRayDataDimensions = resources.ProbeRayDataDimensions;
            pass.probeIrradianceDimensions = resources.ProbeIrradianceDimensions;
            pass.probeDistanceDimensions = resources.ProbeDistanceDimensions;
            pass.probeMaxRayDistance = Mathf.Max(0.001f, resources.probe_max_ray_distance);
            pass.probeSpacingWS = resources.probe_spacing_ws;
            pass.probeHysteresis = Mathf.Clamp01(volume.ProbeHysteresis);
            pass.probeIrradianceEncodingGamma = Mathf.Max(0.01f, volume.IrradianceEncodingGamma);
            pass.probeIrradianceThreshold = Mathf.Max(0.0f, volume.IrradianceThreshold);
            pass.probeBrightnessThreshold = Mathf.Max(0.0f, volume.BrightnessThreshold);
            pass.probeDistanceExponent = Mathf.Max(0.01f, resources.probe_distance_exponent);
            pass.probeRandomRayBackfaceThreshold =
                Mathf.Clamp01(settings != null ? settings.probeRandomRayBackfaceThreshold : 0.1f);
            pass.probeRayRotationRow0 = resources.probe_ray_rotation_row0;
            pass.probeRayRotationRow1 = resources.probe_ray_rotation_row1;
            pass.probeRayRotationRow2 = resources.probe_ray_rotation_row2;
            pass.probeRandomRotationEnabled = resources.probe_random_rotation_enabled;
            pass.probeFixedRaysEnabled = resources.probe_fixed_rays_enabled ? 1.0f : 0.0f;
            pass.probeSkipFixedRaysForBlend = resources.probe_skip_fixed_rays_for_blend ? 1.0f : 0.0f;
        }

        private void SetCommonComputeParameters(ComputeCommandBuffer cmd, int kernel)
        {
            cmd.SetComputeTextureParam(computeShader, kernel, probeRayDataID, probeRayData);
            cmd.SetComputeVectorParam(computeShader, probeCountID,
                new Vector4(probeCount.x, probeCount.y, probeCount.z, 0.0f));
            cmd.SetComputeIntParam(computeShader, probeRayDataFormatID, probeRayDataFormat);
            cmd.SetComputeFloatParam(computeShader, probeRayRadianceMaxID, probeRayRadianceMax);
            cmd.SetComputeVectorParam(computeShader, probeRayDataDimensionsID, probeRayDataDimensions);
            cmd.SetComputeVectorParam(computeShader, probeIrradianceDimensionsID, probeIrradianceDimensions);
            cmd.SetComputeVectorParam(computeShader, probeDistanceDimensionsID, probeDistanceDimensions);
            cmd.SetComputeFloatParam(computeShader, probeRayDataMaxDistanceID, probeMaxRayDistance);
            cmd.SetComputeVectorParam(computeShader, probeSpacingWSID, probeSpacingWS);
            cmd.SetComputeFloatParam(computeShader, probeHysteresisID, probeHysteresis);
            cmd.SetComputeFloatParam(computeShader, probeIrradianceEncodingGammaID, probeIrradianceEncodingGamma);
            cmd.SetComputeIntParam(computeShader, probeIrradianceFormatID, DDGIResources.ProbeIrradianceFormatU32);
            cmd.SetComputeFloatParam(computeShader, probeIrradianceThresholdID, probeIrradianceThreshold);
            cmd.SetComputeFloatParam(computeShader, probeBrightnessThresholdID, probeBrightnessThreshold);
            cmd.SetComputeFloatParam(computeShader, probeDistanceExponentID, probeDistanceExponent);
            cmd.SetComputeFloatParam(computeShader, probeRandomRayBackfaceThresholdID, probeRandomRayBackfaceThreshold);
            cmd.SetComputeVectorParam(computeShader, probeRayRotationRow0ID, probeRayRotationRow0);
            cmd.SetComputeVectorParam(computeShader, probeRayRotationRow1ID, probeRayRotationRow1);
            cmd.SetComputeVectorParam(computeShader, probeRayRotationRow2ID, probeRayRotationRow2);
            cmd.SetComputeFloatParam(computeShader, probeRandomRotationEnabledID, probeRandomRotationEnabled);
            cmd.SetComputeFloatParam(computeShader, probeFixedRaysEnabledID, probeFixedRaysEnabled);
            cmd.SetComputeFloatParam(computeShader, probeSkipFixedRaysForBlendID, probeSkipFixedRaysForBlend);
        }

        private void SetCommonMaterialParameters(MaterialPropertyBlock properties)
        {
            properties.SetVector(probeCountID,
                new Vector4(probeCount.x, probeCount.y, probeCount.z, 0.0f));
            properties.SetInteger(probeRayDataFormatID, probeRayDataFormat);
            properties.SetFloat(probeRayRadianceMaxID, probeRayRadianceMax);
            properties.SetVector(probeRayDataDimensionsID, probeRayDataDimensions);
            properties.SetVector(probeIrradianceDimensionsID, probeIrradianceDimensions);
            properties.SetVector(probeDistanceDimensionsID, probeDistanceDimensions);
            properties.SetFloat(probeRayDataMaxDistanceID, probeMaxRayDistance);
            properties.SetVector(probeSpacingWSID, probeSpacingWS);
            properties.SetFloat(probeHysteresisID, probeHysteresis);
            properties.SetFloat(probeIrradianceEncodingGammaID, probeIrradianceEncodingGamma);
            properties.SetInteger(probeIrradianceFormatID, DDGIResources.ProbeIrradianceFormatU32);
            properties.SetFloat(probeIrradianceThresholdID, probeIrradianceThreshold);
            properties.SetFloat(probeBrightnessThresholdID, probeBrightnessThreshold);
            properties.SetFloat(probeDistanceExponentID, probeDistanceExponent);
            properties.SetFloat(probeRandomRayBackfaceThresholdID, probeRandomRayBackfaceThreshold);
            properties.SetVector(probeRayRotationRow0ID, probeRayRotationRow0);
            properties.SetVector(probeRayRotationRow1ID, probeRayRotationRow1);
            properties.SetVector(probeRayRotationRow2ID, probeRayRotationRow2);
            properties.SetFloat(probeRandomRotationEnabledID, probeRandomRotationEnabled);
            properties.SetFloat(probeFixedRaysEnabledID, probeFixedRaysEnabled);
            properties.SetFloat(probeSkipFixedRaysForBlendID, probeSkipFixedRaysForBlend);
        }

        private static ProbeBlendIssue Validate(DDGIResources resources, YutrelDDGIVolume volume)
        {
            if (volume == null)
            {
                return ProbeBlendIssue.MissingVolume;
            }
            if (resources == null)
            {
                return ProbeBlendIssue.MissingResources;
            }
            if (!resources.probe_ray_data.IsValid())
            {
                return ProbeBlendIssue.MissingProbeRayData;
            }
            if (!resources.has_persistent_atlas ||
                !resources.probe_irradiance_history.IsValid() ||
                !resources.probe_irradiance_write.IsValid() ||
                !resources.probe_distance.IsValid())
            {
                return ProbeBlendIssue.MissingAtlas;
            }
            if (resources.probe_count.x <= 0 || resources.probe_count.y <= 0 || resources.probe_count.z <= 0 ||
                resources.rays_per_probe <= 0 || resources.probe_irradiance_interior_texels <= 0 ||
                resources.probe_distance_interior_texels <= 0)
            {
                return ProbeBlendIssue.InvalidMetadata;
            }

            return ProbeBlendIssue.None;
        }

        private static ProbeBlendIssue ValidateIrradianceShaderResource()
        {
            if (irradianceMaterial != null && irradianceMaterial.shader != null)
            {
                return ProbeBlendIssue.None;
            }

            CoreUtils.Destroy(irradianceMaterial);
            irradianceMaterial = null;

            var rasterShader = Shader.Find(IrradianceShaderName);
            if (rasterShader == null)
            {
                return ProbeBlendIssue.MissingIrradianceShader;
            }

            irradianceMaterial = CoreUtils.CreateEngineMaterial(rasterShader);
            return irradianceMaterial != null ? ProbeBlendIssue.None : ProbeBlendIssue.MissingIrradianceShader;
        }

        private static ProbeBlendIssue ValidateComputeShaderResource(YutrelRPSettings.DDGISettings settings)
        {
            var configuredShader = settings?.probeBlendShader;
            if (shader != configuredShader)
            {
                shader = configuredShader;
                distanceKernel = -1;
                distanceBorderKernel = -1;
            }

            if (shader == null)
            {
                return ProbeBlendIssue.MissingComputeShader;
            }

            if (distanceKernel >= 0 && distanceBorderKernel >= 0)
            {
                return ProbeBlendIssue.None;
            }

            try
            {
                distanceKernel = shader.FindKernel(DistanceKernelName);
                distanceBorderKernel = shader.FindKernel(DistanceBorderKernelName);
            }
            catch (System.Exception)
            {
                distanceKernel = -1;
                distanceBorderKernel = -1;
                return ProbeBlendIssue.MissingKernel;
            }

            return distanceKernel >= 0 && distanceBorderKernel >= 0
                ? ProbeBlendIssue.None
                : ProbeBlendIssue.MissingKernel;
        }

        private static void LogStatus(ProbeBlendIssue issue, YutrelDDGIVolume volume, bool logDiagnostics)
        {
            if (!logDiagnostics && issue == ProbeBlendIssue.None)
            {
                return;
            }

            var volumeKey = volume != null
                ? $"{volume.GetEntityId().GetHashCode()}:{volume.ProbeCount}:{volume.RaysPerProbe}:{volume.ProbeHysteresis}"
                : "none";
            var key = $"{issue}:{volumeKey}";
            if (key == lastStatusKey)
            {
                return;
            }

            lastStatusKey = key;
            if (issue == ProbeBlendIssue.None)
            {
                Debug.Log($"YutrelRP DDGI ProbeBlend OK: volume='{volume.name}', hysteresis={volume.ProbeHysteresis:0.###}.");
                return;
            }

            Debug.LogWarning($"YutrelRP DDGI ProbeBlend skipped: category={GetCategory(issue)}, reason={GetReason(issue)}.");
        }

        private static string GetCategory(ProbeBlendIssue issue)
        {
            switch (issue)
            {
                case ProbeBlendIssue.MissingVolume:
                case ProbeBlendIssue.InvalidMetadata:
                    return "volume/metadata";
                case ProbeBlendIssue.MissingComputeShader:
                case ProbeBlendIssue.MissingIrradianceShader:
                case ProbeBlendIssue.MissingKernel:
                    return "resource/loading";
                case ProbeBlendIssue.MissingProbeRayData:
                case ProbeBlendIssue.MissingAtlas:
                case ProbeBlendIssue.MissingResources:
                    return "resource/binding";
                default:
                    return "dispatch/output";
            }
        }

        private static string GetReason(ProbeBlendIssue issue)
        {
            switch (issue)
            {
                case ProbeBlendIssue.MissingVolume:
                    return "no active YutrelDDGIVolume was provided";
                case ProbeBlendIssue.MissingResources:
                    return "DDGIResources is missing";
                case ProbeBlendIssue.MissingProbeRayData:
                    return "ProbeRayData is missing or invalid";
                case ProbeBlendIssue.MissingAtlas:
                    return "persistent ProbeIrradiance history/write or ProbeDistance atlas is missing";
                case ProbeBlendIssue.InvalidMetadata:
                    return "DDGI probe count, ray count, or atlas texel metadata is invalid";
                case ProbeBlendIssue.MissingComputeShader:
                    return "YutrelRPAsset DDGI probeBlendShader is missing or invalid";
                case ProbeBlendIssue.MissingIrradianceShader:
                    return $"shader '{IrradianceShaderName}' is missing or invalid";
                case ProbeBlendIssue.MissingKernel:
                    return "DDGIProbeBlend compute shader is missing a required distance kernel";
                default:
                    return "unknown failure";
            }
        }

        private static int DivRoundUp(int value, int divisor)
        {
            return Mathf.Max(1, (value + divisor - 1) / divisor);
        }

        internal static void Cleanup()
        {
            shader = null;
            CoreUtils.Destroy(irradianceMaterial);
            irradianceMaterial = null;
            propertyBlock = null;
            distanceKernel = -1;
            distanceBorderKernel = -1;
            lastStatusKey = null;
        }

        private enum ProbeBlendIssue
        {
            None = 0,
            MissingVolume = 1,
            MissingResources = 2,
            MissingProbeRayData = 3,
            MissingAtlas = 4,
            InvalidMetadata = 5,
            MissingComputeShader = 6,
            MissingIrradianceShader = 7,
            MissingKernel = 8
        }
    }
}
