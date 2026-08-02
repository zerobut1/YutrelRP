using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace YutrelRP
{
    internal sealed class DDGIDebugPass
    {
        private const string KernelName = "CSMain";
        private const int ThreadGroupSize = 8;

        private static readonly ProfilingSampler sampler = new("DDGI Debug");
        private static readonly int probe_ray_data_ID = Shader.PropertyToID("_DDGIProbeRayData");
        private static readonly int debug_texture_ID = Shader.PropertyToID("_DDGIDebugTexture");
        private static readonly int debug_texture_dimensions_ID = Shader.PropertyToID("_DDGIDebugTextureDimensions");

        internal static void Record(RenderGraph render_graph, DDGIResources resources)
        {
            if (resources == null || !resources.is_valid || !resources.probe_ray_data.IsValid())
            {
                return;
            }

            if (!TryGetDebugShader(out var shader))
            {
                return;
            }

            var volume = resources.active_volume;
            if (volume == null)
            {
                return;
            }

            var probe_count = volume.ProbeCount;
            var width = volume.RaysPerProbe;
            var height = probe_count.x * probe_count.z;
            if (width <= 0 || height <= 0 || probe_count.y <= 0)
            {
                return;
            }

            var debug_desc = new TextureDesc(width, height)
            {
                colorFormat = GraphicsFormat.R32G32B32A32_SFloat,
                dimension = TextureDimension.Tex2DArray,
                slices = probe_count.y,
                enableRandomWrite = true,
                clearBuffer = true,
                clearColor = Color.black,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                name = "DDGI Debug ProbeRayData"
            };

            using var builder = render_graph.AddComputePass<DDGIDebugPass>(sampler.name, out var pass, sampler);
            pass.shader = shader;
            pass.kernel = shader.FindKernel(KernelName);
            pass.probe_ray_data = resources.probe_ray_data;
            resources.probe_ray_data_debug = render_graph.CreateTexture(debug_desc);
            pass.debug_texture = resources.probe_ray_data_debug;
            pass.debug_texture_dimensions = new Vector3Int(width, height, probe_count.y);

            builder.UseTexture(pass.probe_ray_data, AccessFlags.Read);
            builder.UseTexture(pass.debug_texture, AccessFlags.Write);
            builder.AllowPassCulling(false);
            builder.SetRenderFunc<DDGIDebugPass>(static (pass, context) => pass.Render(context));
        }

        private ComputeShader shader;
        private int kernel;
        private TextureHandle probe_ray_data;
        private TextureHandle debug_texture;
        private Vector3Int debug_texture_dimensions;

        private void Render(ComputeGraphContext context)
        {
            var cmd = context.cmd;
            cmd.SetComputeTextureParam(shader, kernel, probe_ray_data_ID, probe_ray_data);
            cmd.SetComputeTextureParam(shader, kernel, debug_texture_ID, debug_texture);
            cmd.SetComputeVectorParam(shader, debug_texture_dimensions_ID,
                new Vector4(debug_texture_dimensions.x, debug_texture_dimensions.y, debug_texture_dimensions.z,
                    0.0f));

            var group_x = Mathf.CeilToInt((float)debug_texture_dimensions.x / ThreadGroupSize);
            var group_y = Mathf.CeilToInt((float)debug_texture_dimensions.y / ThreadGroupSize);
            cmd.DispatchCompute(shader, kernel, group_x, group_y, debug_texture_dimensions.z);
        }

        private static bool TryGetDebugShader(out ComputeShader shader)
        {
            shader = null;

            if (!GraphicsSettings.TryGetRenderPipelineSettings<YutrelDDGIShaderResources>(out var resources))
            {
                return false;
            }

            shader = resources.debug;
            return shader != null;
        }
    }
}
