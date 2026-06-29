using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace YutrelRP
{
    internal sealed class DDGIProbeTracePass
    {
        private const string ShaderPassName = "DDGIRayTracing";
        private const string RayGenName = "RayGenDDGIProbeTrace";

        private static readonly ProfilingSampler sampler = new("DDGI Probe Trace");
        private static readonly int probe_ray_data_ID = Shader.PropertyToID("_DDGIProbeRayData");

        internal static void Record(RenderGraph render_graph, DDGIResources resources)
        {
            if (resources == null || !resources.is_valid)
            {
                return;
            }

            var volume = resources.active_volume;
            if (volume == null)
            {
                return;
            }

            var probe_count = volume.ProbeCount;
            var ray_data_desc = new TextureDesc(volume.RaysPerProbe, probe_count.x * probe_count.z)
            {
                colorFormat = GraphicsFormat.R32G32_SFloat,
                dimension = TextureDimension.Tex2DArray,
                slices = probe_count.y,
                enableRandomWrite = true,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                clearBuffer = true,
                clearColor = new Color(0.0f, 1.0e27f, 0.0f, 0.0f),
                name = "DDGI Probe Ray Data"
            };
            resources.probe_ray_data = render_graph.CreateTexture(ray_data_desc);

            if (!TryGetProbeTraceShader(out var shader))
            {
                return;
            }

            using var builder = render_graph.AddComputePass<DDGIProbeTracePass>(sampler.name, out var pass, sampler);
            pass.ray_tracing_shader = shader;
            pass.probe_ray_data = resources.probe_ray_data;
            pass.rays_per_probe = volume.RaysPerProbe;
            pass.plane_probe_count = probe_count.x * probe_count.z;
            pass.probe_count_y = probe_count.y;
            builder.UseTexture(resources.probe_ray_data, AccessFlags.Write);
            builder.AllowPassCulling(false);
            builder.SetRenderFunc<DDGIProbeTracePass>(static (pass, context) => pass.Render(context));
        }

        private RayTracingShader ray_tracing_shader;
        private TextureHandle probe_ray_data;
        private int rays_per_probe;
        private int plane_probe_count;
        private int probe_count_y;

        private void Render(ComputeGraphContext context)
        {
            var cmd = context.cmd;
            cmd.SetRayTracingShaderPass(ray_tracing_shader, ShaderPassName);
            cmd.SetRayTracingTextureParam(ray_tracing_shader, probe_ray_data_ID, probe_ray_data);
            cmd.DispatchRays(ray_tracing_shader, RayGenName, (uint)rays_per_probe, (uint)plane_probe_count,
                (uint)probe_count_y, null);
        }

        private static bool TryGetProbeTraceShader(out RayTracingShader shader)
        {
            shader = null;

            if (!GraphicsSettings.TryGetRenderPipelineSettings<YutrelDDGIShaderResources>(out var resources))
            {
                return false;
            }

            shader = resources.probe_trace;
            return shader != null;
        }
    }
}
