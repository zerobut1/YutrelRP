using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace YutrelRP
{
    internal sealed class DDGIProbeRelocationPass
    {
        private const string KernelName = "RelocateProbes";
        private const int ThreadGroupSizeX = 32;

        private static readonly ProfilingSampler sampler = new("DDGI Probe Relocation");
        private static readonly int probeRayDataID = DDGIResources.probe_ray_data_ID;
        private static readonly int probeRayDataFormatID = DDGIResources.probe_ray_data_format_ID;
        private static readonly int probeDataID = DDGIResources.probe_data_ID;
        private static readonly int probeCountID = Shader.PropertyToID("_DDGIProbeCount");
        private static readonly int probeRayDataDimensionsID = DDGIResources.probe_ray_data_dimensions_ID;
        private static readonly int probeRayDataMaxDistanceID = DDGIResources.probe_ray_data_max_distance_ID;
        private static readonly int probeSpacingWSID = DDGIResources.probe_spacing_ws_ID;
        private static readonly int probeFixedRayBackfaceThresholdID =
            DDGIResources.probe_fixed_ray_backface_threshold_ID;
        private static readonly int probeMinFrontfaceDistanceID = DDGIResources.probe_min_frontface_distance_ID;
        private static readonly int probeMaxRelocationOffsetID = DDGIResources.probe_max_relocation_offset_ID;

        private static ComputeShader shader;
        private static int kernel = -1;
        private static string lastStatusKey;

        internal static void Record(RenderGraph renderGraph, YutrelDDGIVolume volume,
            YutrelRPSettings.DDGISettings settings, ref DDGIResources resources)
        {
            resources.probe_relocation_enabled = settings != null && settings.probeRelocationEnabled;
            if (!resources.probe_relocation_enabled)
            {
                return;
            }

            var issue = Validate(resources, volume);
            if (issue == ProbeRelocationIssue.None)
            {
                issue = ValidateShaderResource(settings);
            }

            LogStatus(issue, volume, settings != null && settings.logDiagnostics);
            if (issue != ProbeRelocationIssue.None)
            {
                resources.probe_relocation_enabled = false;
                resources.diagnostic = GetReason(issue);
                return;
            }

            using var builder = renderGraph.AddComputePass<DDGIProbeRelocationPass>(sampler.name, out var pass, sampler);
            pass.probeRayData = resources.probe_ray_data;
            pass.probeData = resources.probe_data;
            pass.computeShader = shader;
            pass.probeCount = resources.probe_count;
            pass.probeRayDataFormat = resources.probe_ray_data_format;
            pass.probeRayDataDimensions = resources.ProbeRayDataDimensions;
            pass.probeMaxRayDistance = Mathf.Max(0.001f, resources.probe_max_ray_distance);
            pass.probeSpacingWS = resources.probe_spacing_ws;
            pass.fixedRayBackfaceThreshold = Mathf.Clamp01(settings.probeFixedRayBackfaceThreshold);
            pass.minFrontfaceDistance = Mathf.Max(0.0f, settings.probeMinFrontfaceDistance);
            pass.maxRelocationOffset = Mathf.Clamp(settings.probeMaxRelocationOffset, 0.0f, 0.49f);
            pass.totalProbeCount = resources.probe_count.x * resources.probe_count.y * resources.probe_count.z;

            builder.UseTexture(pass.probeRayData, AccessFlags.Read);
            builder.UseTexture(pass.probeData, AccessFlags.ReadWrite);
            builder.AllowPassCulling(false);
            builder.SetRenderFunc<DDGIProbeRelocationPass>(static (pass, context) => pass.Render(context));
        }

        private TextureHandle probeRayData;
        private TextureHandle probeData;
        private ComputeShader computeShader;
        private Vector3Int probeCount;
        private int probeRayDataFormat;
        private Vector4 probeRayDataDimensions;
        private float probeMaxRayDistance;
        private Vector3 probeSpacingWS;
        private float fixedRayBackfaceThreshold;
        private float minFrontfaceDistance;
        private float maxRelocationOffset;
        private int totalProbeCount;

        private void Render(ComputeGraphContext context)
        {
            var cmd = context.cmd;
            cmd.SetComputeTextureParam(computeShader, kernel, probeRayDataID, probeRayData);
            cmd.SetComputeTextureParam(computeShader, kernel, probeDataID, probeData);
            cmd.SetComputeVectorParam(computeShader, probeCountID,
                new Vector4(probeCount.x, probeCount.y, probeCount.z, 0.0f));
            cmd.SetComputeIntParam(computeShader, probeRayDataFormatID, probeRayDataFormat);
            cmd.SetComputeVectorParam(computeShader, probeRayDataDimensionsID, probeRayDataDimensions);
            cmd.SetComputeFloatParam(computeShader, probeRayDataMaxDistanceID, probeMaxRayDistance);
            cmd.SetComputeVectorParam(computeShader, probeSpacingWSID, probeSpacingWS);
            cmd.SetComputeFloatParam(computeShader, probeFixedRayBackfaceThresholdID, fixedRayBackfaceThreshold);
            cmd.SetComputeFloatParam(computeShader, probeMinFrontfaceDistanceID, minFrontfaceDistance);
            cmd.SetComputeFloatParam(computeShader, probeMaxRelocationOffsetID, maxRelocationOffset);
            cmd.DispatchCompute(computeShader, kernel, DivRoundUp(totalProbeCount, ThreadGroupSizeX), 1, 1);
        }

        private static ProbeRelocationIssue Validate(DDGIResources resources, YutrelDDGIVolume volume)
        {
            if (volume == null)
            {
                return ProbeRelocationIssue.MissingVolume;
            }
            if (resources == null)
            {
                return ProbeRelocationIssue.MissingResources;
            }
            if (!resources.probe_ray_data.IsValid())
            {
                return ProbeRelocationIssue.MissingProbeRayData;
            }
            if (!resources.has_persistent_atlas || !resources.probe_data.IsValid())
            {
                return ProbeRelocationIssue.MissingProbeData;
            }
            if (resources.probe_count.x <= 0 || resources.probe_count.y <= 0 || resources.probe_count.z <= 0 ||
                resources.rays_per_probe <= 0)
            {
                return ProbeRelocationIssue.InvalidMetadata;
            }

            return ProbeRelocationIssue.None;
        }

        private static ProbeRelocationIssue ValidateShaderResource(YutrelRPSettings.DDGISettings settings)
        {
            var configuredShader = settings?.probeRelocationShader;
            if (shader != configuredShader)
            {
                shader = configuredShader;
                kernel = -1;
            }

            if (shader == null)
            {
                return ProbeRelocationIssue.MissingComputeShader;
            }

            if (kernel >= 0)
            {
                return ProbeRelocationIssue.None;
            }

            try
            {
                kernel = shader.FindKernel(KernelName);
            }
            catch (System.Exception)
            {
                kernel = -1;
                return ProbeRelocationIssue.MissingKernel;
            }

            return kernel >= 0 ? ProbeRelocationIssue.None : ProbeRelocationIssue.MissingKernel;
        }

        private static void LogStatus(ProbeRelocationIssue issue, YutrelDDGIVolume volume, bool logDiagnostics)
        {
            if (!logDiagnostics && issue == ProbeRelocationIssue.None)
            {
                return;
            }

            var volumeKey = volume != null
                ? $"{volume.GetEntityId().GetHashCode()}:{volume.ProbeCount}:{volume.RaysPerProbe}"
                : "none";
            var key = $"{issue}:{volumeKey}";
            if (key == lastStatusKey)
            {
                return;
            }

            lastStatusKey = key;
            if (issue == ProbeRelocationIssue.None)
            {
                Debug.Log($"YutrelRP DDGI ProbeRelocation OK: volume='{volume.name}'.");
                return;
            }

            Debug.LogWarning(
                $"YutrelRP DDGI ProbeRelocation skipped: category={GetCategory(issue)}, reason={GetReason(issue)}.");
        }

        private static string GetCategory(ProbeRelocationIssue issue)
        {
            switch (issue)
            {
                case ProbeRelocationIssue.MissingVolume:
                case ProbeRelocationIssue.InvalidMetadata:
                    return "volume/metadata";
                case ProbeRelocationIssue.MissingComputeShader:
                case ProbeRelocationIssue.MissingKernel:
                    return "resource/loading";
                case ProbeRelocationIssue.MissingProbeRayData:
                case ProbeRelocationIssue.MissingProbeData:
                case ProbeRelocationIssue.MissingResources:
                    return "resource/binding";
                default:
                    return "dispatch/output";
            }
        }

        private static string GetReason(ProbeRelocationIssue issue)
        {
            switch (issue)
            {
                case ProbeRelocationIssue.MissingVolume:
                    return "no active YutrelDDGIVolume was provided";
                case ProbeRelocationIssue.MissingResources:
                    return "DDGIResources is missing";
                case ProbeRelocationIssue.MissingProbeRayData:
                    return "ProbeRayData is missing or invalid";
                case ProbeRelocationIssue.MissingProbeData:
                    return "persistent ProbeData atlas is missing";
                case ProbeRelocationIssue.InvalidMetadata:
                    return "DDGI probe count or ray count metadata is invalid";
                case ProbeRelocationIssue.MissingComputeShader:
                    return "YutrelRPAsset DDGI probeRelocationShader is missing or invalid";
                case ProbeRelocationIssue.MissingKernel:
                    return "DDGIProbeRelocation compute shader is missing the RelocateProbes kernel";
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
            kernel = -1;
            lastStatusKey = null;
        }

        private enum ProbeRelocationIssue
        {
            None = 0,
            MissingVolume = 1,
            MissingResources = 2,
            MissingProbeRayData = 3,
            MissingProbeData = 4,
            InvalidMetadata = 5,
            MissingComputeShader = 6,
            MissingKernel = 7
        }
    }
}
