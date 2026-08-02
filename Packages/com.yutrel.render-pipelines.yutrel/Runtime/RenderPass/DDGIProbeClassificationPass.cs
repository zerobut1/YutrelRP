using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace YutrelRP
{
    internal sealed class DDGIProbeClassificationPass
    {
        private const string KernelName = "ClassifyProbes";
        private const int ThreadGroupSize = 4;

        private static readonly ProfilingSampler sampler = new("DDGI Probe Classification");
        private static readonly int probe_ray_data_ID = Shader.PropertyToID("_DDGIProbeRayData");
        private static readonly int probe_data_ID = Shader.PropertyToID("_DDGIProbeData");
        private static readonly int probe_count_ID = Shader.PropertyToID("_DDGIProbeCount");
        private static readonly int probe_spacing_ID = Shader.PropertyToID("_DDGIProbeSpacing");
        private static readonly int probe_fixed_ray_backface_threshold_ID =
            Shader.PropertyToID("_DDGIProbeFixedRayBackfaceThreshold");
        private static readonly int probe_relocation_enabled_ID = Shader.PropertyToID("_DDGIProbeRelocationEnabled");
        private static readonly int probe_classification_enabled_ID =
            Shader.PropertyToID("_DDGIProbeClassificationEnabled");

        internal static void Record(RenderGraph render_graph, DDGIResources resources,
            ResolvedDDGISettings ddgi_settings)
        {
            if (resources == null || !resources.is_valid || !resources.probe_ray_data.IsValid() ||
                !resources.probe_data.IsValid())
            {
                return;
            }

            var volume = resources.active_volume;
            var relocation_settings = ddgi_settings.relocation;
            var classification_settings = ddgi_settings.classification;
            if (volume == null || !classification_settings.enabled ||
                volume.RaysPerProbe <= DDGIResources.FixedRayCount)
            {
                return;
            }

            if (!TryGetShader(out var shader))
            {
                return;
            }

            using var builder = render_graph.AddComputePass<DDGIProbeClassificationPass>(
                sampler.name, out var pass, sampler);

            pass.shader = shader;
            pass.kernel = shader.FindKernel(KernelName);
            pass.probe_ray_data = resources.probe_ray_data;
            pass.probe_data = resources.probe_data;
            pass.probe_count = volume.ProbeCount;
            pass.probe_spacing = volume.GetWorldProbeSpacing();
            pass.probe_fixed_ray_backface_threshold =
                Mathf.Clamp01(relocation_settings.probeFixedRayBackfaceThreshold);
            pass.probe_relocation_enabled = relocation_settings.enabled ? 1 : 0;
            pass.dispatch_groups = new Vector3Int(
                Mathf.CeilToInt((float)volume.ProbeCount.x / ThreadGroupSize),
                Mathf.CeilToInt((float)volume.ProbeCount.y / ThreadGroupSize),
                Mathf.CeilToInt((float)volume.ProbeCount.z / ThreadGroupSize));

            builder.UseTexture(pass.probe_ray_data, AccessFlags.Read);
            builder.UseTexture(pass.probe_data, AccessFlags.ReadWrite);
            builder.AllowPassCulling(false);
            builder.SetRenderFunc<DDGIProbeClassificationPass>(static (pass, context) => pass.Render(context));
        }

        private ComputeShader shader;
        private int kernel;
        private TextureHandle probe_ray_data;
        private TextureHandle probe_data;
        private Vector3Int probe_count;
        private Vector3 probe_spacing;
        private float probe_fixed_ray_backface_threshold;
        private int probe_relocation_enabled;
        private Vector3Int dispatch_groups;

        private void Render(ComputeGraphContext context)
        {
            var cmd = context.cmd;
            cmd.SetComputeTextureParam(shader, kernel, probe_ray_data_ID, probe_ray_data);
            cmd.SetComputeTextureParam(shader, kernel, probe_data_ID, probe_data);
            cmd.SetComputeVectorParam(shader, probe_count_ID,
                new Vector4(probe_count.x, probe_count.y, probe_count.z, 0.0f));
            cmd.SetComputeVectorParam(shader, probe_spacing_ID,
                new Vector4(probe_spacing.x, probe_spacing.y, probe_spacing.z, 0.0f));
            cmd.SetComputeFloatParam(shader, probe_fixed_ray_backface_threshold_ID,
                probe_fixed_ray_backface_threshold);
            cmd.SetComputeIntParam(shader, probe_relocation_enabled_ID, probe_relocation_enabled);
            cmd.SetComputeIntParam(shader, probe_classification_enabled_ID, 1);
            cmd.DispatchCompute(shader, kernel, dispatch_groups.x, dispatch_groups.y, dispatch_groups.z);
        }

        private static bool TryGetShader(out ComputeShader shader)
        {
            shader = null;

            if (!GraphicsSettings.TryGetRenderPipelineSettings<YutrelDDGIShaderResources>(out var resources))
            {
                return false;
            }

            shader = resources.probe_classification;
            return shader != null;
        }
    }
}
