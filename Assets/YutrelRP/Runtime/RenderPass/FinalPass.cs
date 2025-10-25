using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace YutrelRP
{
    internal class FinalPass
    {
        private static readonly ProfilingSampler sampler = new("Final Pass");

        private static Material material;

        // data
        TextureHandle source_color;

        private void Render(RasterGraphContext context)
        {
            var cmd = context.cmd;

            Blitter.BlitTexture(cmd, source_color, new Vector4(1, 1, 0, 0), material, 0);
        }

        internal static void Record(RenderGraph render_graph, RenderTargets textures)
        {
            using var builder = render_graph.AddRasterRenderPass<FinalPass>(sampler.name, out var pass, sampler);

            if (material == null) material = new Material(Shader.Find("YutrelRP/ToneMapping"));

            pass.source_color = textures.final_color;

            builder.UseTexture(pass.source_color);
            builder.SetRenderAttachment(textures.camera_output, 0);

            builder.SetRenderFunc<FinalPass>(static (pass, context) => { pass.Render(context); });
        }
    }
}