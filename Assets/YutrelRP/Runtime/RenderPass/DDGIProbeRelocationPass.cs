using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace YutrelRP
{
    internal sealed class DDGIProbeRelocationPass
    {
        private const string KernelName = "RelocateProbes";
        private const int ThreadGroupSize = 32;

        private static readonly ProfilingSampler sampler = new("DDGI Probe Relocation");
        private static readonly int probe_ray_data_ID = Shader.PropertyToID("_DDGIProbeRayData");
        private static readonly int probe_data_ID = Shader.PropertyToID("_DDGIProbeData");
        private static readonly int probe_count_ID = Shader.PropertyToID("_DDGIProbeCount");
        private static readonly int rays_per_probe_ID = Shader.PropertyToID("_DDGIRaysPerProbe");
        private static readonly int total_probe_count_ID = Shader.PropertyToID("_DDGITotalProbeCount");
        private static readonly int probe_spacing_ID = Shader.PropertyToID("_DDGIProbeSpacing");
        private static readonly int probe_min_frontface_distance_ID =
            Shader.PropertyToID("_DDGIProbeMinFrontfaceDistance");
        private static readonly int probe_fixed_ray_backface_threshold_ID =
            Shader.PropertyToID("_DDGIProbeFixedRayBackfaceThreshold");
        private static readonly int probe_relocation_enabled_ID = Shader.PropertyToID("_DDGIProbeRelocationEnabled");

        internal static void Record(RenderGraph render_graph, DDGIResources resources)
        {
            if (resources == null || !resources.is_valid || !resources.probe_ray_data.IsValid() ||
                !resources.probe_data.IsValid())
            {
                return;
            }

            var volume = resources.active_volume;
            if (volume == null || !volume.ProbeRelocationEnabled ||
                volume.RaysPerProbe <= DDGIResources.FixedRayCount)
            {
                return;
            }

            if (!TryGetShader(out var shader))
            {
                return;
            }

            using var builder = render_graph.AddComputePass<DDGIProbeRelocationPass>(
                sampler.name, out var pass, sampler);

            pass.shader = shader;
            pass.kernel = shader.FindKernel(KernelName);
            pass.probe_ray_data = resources.probe_ray_data;
            pass.probe_data = resources.probe_data;
            pass.probe_count = volume.ProbeCount;
            pass.rays_per_probe = volume.RaysPerProbe;
            pass.total_probe_count = volume.TotalProbeCount;
            pass.probe_spacing = volume.GetWorldProbeSpacing();
            pass.probe_min_frontface_distance = volume.ProbeMinFrontfaceDistance;
            pass.probe_fixed_ray_backface_threshold = volume.ProbeFixedRayBackfaceThreshold;
            pass.dispatch_groups = Mathf.CeilToInt((float)volume.TotalProbeCount / ThreadGroupSize);

            builder.UseTexture(pass.probe_ray_data, AccessFlags.Read);
            builder.UseTexture(pass.probe_data, AccessFlags.ReadWrite);
            builder.AllowPassCulling(false);
            builder.SetRenderFunc<DDGIProbeRelocationPass>(static (pass, context) => pass.Render(context));
        }

        private ComputeShader shader;
        private int kernel;
        private TextureHandle probe_ray_data;
        private TextureHandle probe_data;
        private Vector3Int probe_count;
        private int rays_per_probe;
        private int total_probe_count;
        private Vector3 probe_spacing;
        private float probe_min_frontface_distance;
        private float probe_fixed_ray_backface_threshold;
        private int dispatch_groups;

        private void Render(ComputeGraphContext context)
        {
            var cmd = context.cmd;
            cmd.SetComputeTextureParam(shader, kernel, probe_ray_data_ID, probe_ray_data);
            cmd.SetComputeTextureParam(shader, kernel, probe_data_ID, probe_data);
            cmd.SetComputeVectorParam(shader, probe_count_ID,
                new Vector4(probe_count.x, probe_count.y, probe_count.z, 0.0f));
            cmd.SetComputeIntParam(shader, rays_per_probe_ID, rays_per_probe);
            cmd.SetComputeIntParam(shader, total_probe_count_ID, total_probe_count);
            cmd.SetComputeVectorParam(shader, probe_spacing_ID,
                new Vector4(probe_spacing.x, probe_spacing.y, probe_spacing.z, 0.0f));
            cmd.SetComputeFloatParam(shader, probe_min_frontface_distance_ID, probe_min_frontface_distance);
            cmd.SetComputeFloatParam(shader, probe_fixed_ray_backface_threshold_ID,
                probe_fixed_ray_backface_threshold);
            cmd.SetComputeIntParam(shader, probe_relocation_enabled_ID, 1);
            cmd.DispatchCompute(shader, kernel, dispatch_groups, 1, 1);
        }

        private static bool TryGetShader(out ComputeShader shader)
        {
            shader = null;

            if (!GraphicsSettings.TryGetRenderPipelineSettings<YutrelDDGIShaderResources>(out var resources))
            {
                return false;
            }

            shader = resources.probe_relocation;
            return shader != null;
        }
    }
}
