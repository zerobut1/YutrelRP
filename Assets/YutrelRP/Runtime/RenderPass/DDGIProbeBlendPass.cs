using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace YutrelRP
{
    internal sealed class DDGIProbeBlendPass
    {
        private const string IrradianceKernelName = "BlendIrradiance";
        private const string DistanceKernelName = "BlendDistance";
        private const int ThreadGroupSizeX = 8;
        private const int ThreadGroupSizeY = 8;

        private static readonly ProfilingSampler sampler = new("DDGI Probe Blend");
        private static readonly int probeRayDataID = DDGIResources.probe_ray_data_ID;
        private static readonly int probeIrradianceID = DDGIResources.probe_irradiance_ID;
        private static readonly int probeDistanceID = DDGIResources.probe_distance_ID;
        private static readonly int probeCountID = Shader.PropertyToID("_DDGIProbeCount");
        private static readonly int probeRayDataDimensionsID = DDGIResources.probe_ray_data_dimensions_ID;
        private static readonly int probeIrradianceDimensionsID = DDGIResources.probe_irradiance_dimensions_ID;
        private static readonly int probeDistanceDimensionsID = DDGIResources.probe_distance_dimensions_ID;
        private static readonly int probeRayDataMaxDistanceID = DDGIResources.probe_ray_data_max_distance_ID;
        private static readonly int probeHysteresisID = Shader.PropertyToID("_DDGIProbeHysteresis");
        private static readonly int probeDistanceExponentID = Shader.PropertyToID("_DDGIProbeDistanceExponent");
        private static readonly int probeFixedRayBackfaceThresholdID =
            DDGIResources.probe_fixed_ray_backface_threshold_ID;

        private static ComputeShader shader;
        private static int irradianceKernel = -1;
        private static int distanceKernel = -1;
        private static string lastStatusKey;

        internal static void Record(RenderGraph renderGraph, YutrelDDGIVolume volume,
            YutrelRPSettings.DDGISettings settings, ref DDGIResources resources)
        {
            var issue = Validate(resources, volume);
            if (issue == ProbeBlendIssue.None)
            {
                issue = ValidateShaderResource(settings);
            }

            LogStatus(issue, volume, settings != null && settings.logDiagnostics);
            if (issue != ProbeBlendIssue.None)
            {
                resources.has_gather_data = false;
                resources.diagnostic = GetReason(issue);
                return;
            }

            resources.has_gather_data = true;

            using var builder = renderGraph.AddComputePass<DDGIProbeBlendPass>(sampler.name, out var pass, sampler);
            pass.probeRayData = resources.probe_ray_data;
            pass.probeIrradiance = resources.probe_irradiance;
            pass.probeDistance = resources.probe_distance;
            pass.computeShader = shader;
            pass.probeCount = resources.probe_count;
            pass.probeRayDataDimensions = resources.ProbeRayDataDimensions;
            pass.probeIrradianceDimensions = resources.ProbeIrradianceDimensions;
            pass.probeDistanceDimensions = resources.ProbeDistanceDimensions;
            pass.probeMaxRayDistance = Mathf.Max(0.001f, resources.probe_max_ray_distance);
            pass.probeHysteresis = Mathf.Clamp01(volume.ProbeHysteresis);
            pass.probeDistanceExponent = Mathf.Max(0.01f, resources.probe_distance_exponent);
            pass.probeFixedRayBackfaceThreshold = Mathf.Clamp01(settings.probeFixedRayBackfaceThreshold);

            builder.UseTexture(pass.probeRayData, AccessFlags.Read);
            builder.UseTexture(pass.probeIrradiance, AccessFlags.ReadWrite);
            builder.UseTexture(pass.probeDistance, AccessFlags.ReadWrite);
            builder.AllowPassCulling(false);
            builder.SetRenderFunc<DDGIProbeBlendPass>(static (pass, context) => pass.Render(context));
        }

        private TextureHandle probeRayData;
        private TextureHandle probeIrradiance;
        private TextureHandle probeDistance;
        private ComputeShader computeShader;
        private Vector3Int probeCount;
        private Vector4 probeRayDataDimensions;
        private Vector4 probeIrradianceDimensions;
        private Vector4 probeDistanceDimensions;
        private float probeMaxRayDistance;
        private float probeHysteresis;
        private float probeDistanceExponent;
        private float probeFixedRayBackfaceThreshold;

        private void Render(ComputeGraphContext context)
        {
            var cmd = context.cmd;
            SetCommonParameters(cmd, irradianceKernel);
            SetCommonParameters(cmd, distanceKernel);
            cmd.SetComputeTextureParam(computeShader, irradianceKernel, probeIrradianceID, probeIrradiance);
            cmd.SetComputeTextureParam(computeShader, distanceKernel, probeDistanceID, probeDistance);

            cmd.DispatchCompute(computeShader, irradianceKernel,
                DivRoundUp((int)probeIrradianceDimensions.x, ThreadGroupSizeX),
                DivRoundUp((int)probeIrradianceDimensions.y, ThreadGroupSizeY),
                Mathf.Max(1, (int)probeIrradianceDimensions.z));

            cmd.DispatchCompute(computeShader, distanceKernel,
                DivRoundUp((int)probeDistanceDimensions.x, ThreadGroupSizeX),
                DivRoundUp((int)probeDistanceDimensions.y, ThreadGroupSizeY),
                Mathf.Max(1, (int)probeDistanceDimensions.z));
        }

        private void SetCommonParameters(ComputeCommandBuffer cmd, int kernel)
        {
            cmd.SetComputeTextureParam(computeShader, kernel, probeRayDataID, probeRayData);
            cmd.SetComputeVectorParam(computeShader, probeCountID,
                new Vector4(probeCount.x, probeCount.y, probeCount.z, 0.0f));
            cmd.SetComputeVectorParam(computeShader, probeRayDataDimensionsID, probeRayDataDimensions);
            cmd.SetComputeVectorParam(computeShader, probeIrradianceDimensionsID, probeIrradianceDimensions);
            cmd.SetComputeVectorParam(computeShader, probeDistanceDimensionsID, probeDistanceDimensions);
            cmd.SetComputeFloatParam(computeShader, probeRayDataMaxDistanceID, probeMaxRayDistance);
            cmd.SetComputeFloatParam(computeShader, probeHysteresisID, probeHysteresis);
            cmd.SetComputeFloatParam(computeShader, probeDistanceExponentID, probeDistanceExponent);
            cmd.SetComputeFloatParam(computeShader, probeFixedRayBackfaceThresholdID, probeFixedRayBackfaceThreshold);
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
            if (!resources.has_persistent_atlas || !resources.probe_irradiance.IsValid() ||
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

        private static ProbeBlendIssue ValidateShaderResource(YutrelRPSettings.DDGISettings settings)
        {
            var configuredShader = settings?.probeBlendShader;
            if (shader != configuredShader)
            {
                shader = configuredShader;
                irradianceKernel = -1;
                distanceKernel = -1;
            }

            if (shader == null)
            {
                return ProbeBlendIssue.MissingComputeShader;
            }

            if (irradianceKernel >= 0 && distanceKernel >= 0)
            {
                return ProbeBlendIssue.None;
            }

            try
            {
                irradianceKernel = shader.FindKernel(IrradianceKernelName);
                distanceKernel = shader.FindKernel(DistanceKernelName);
            }
            catch (System.Exception)
            {
                irradianceKernel = -1;
                distanceKernel = -1;
                return ProbeBlendIssue.MissingKernel;
            }

            return irradianceKernel >= 0 && distanceKernel >= 0 ? ProbeBlendIssue.None : ProbeBlendIssue.MissingKernel;
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
                    return "persistent ProbeIrradiance or ProbeDistance atlas is missing";
                case ProbeBlendIssue.InvalidMetadata:
                    return "DDGI probe count, ray count, or atlas texel metadata is invalid";
                case ProbeBlendIssue.MissingComputeShader:
                    return "YutrelRPAsset DDGI probeBlendShader is missing or invalid";
                case ProbeBlendIssue.MissingKernel:
                    return "DDGIProbeBlend compute shader is missing a required kernel";
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
            irradianceKernel = -1;
            distanceKernel = -1;
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
            MissingKernel = 7
        }
    }
}
