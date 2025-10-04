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
        private TextureHandle final_color;

        private void Render(RasterGraphContext context)
        {
            var cmd = context.cmd;

            Blitter.BlitTexture(cmd, final_color, new Vector4(1, 1, 0, 0), material, 0);
        }

        internal static void Record(RenderGraph render_graph, in RenderTargets textures)
        {
            using var builder = render_graph.AddRasterRenderPass<FinalPass>(sampler.name, out var pass, sampler);

            if (material == null) material = new Material(Shader.Find("YutrelRP/FinalPass"));

            pass.final_color = textures.scene_color;

            builder.UseTexture(pass.final_color);
            builder.SetRenderAttachment(textures.camera_output, 0);

            builder.SetRenderFunc<FinalPass>(static (pass, context) => { pass.Render(context); });
        }
    }
}