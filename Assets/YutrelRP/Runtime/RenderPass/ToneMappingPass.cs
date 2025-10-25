using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace YutrelRP
{
    internal class ToneMappingPass
    {
        private static readonly ProfilingSampler sampler = new("Tone Mapping Pass");

        private static Material material;

        // data
        TextureHandle source_color;

        int pass_id;

        private void Render(RasterGraphContext context)
        {
            var cmd = context.cmd;

            Blitter.BlitTexture(cmd, source_color, new Vector4(1, 1, 0, 0), material, pass_id);
        }

        internal static void Record(RenderGraph render_graph, RenderTargets textures, PostProcessSettings settings)
        {
            using var builder = render_graph.AddRasterRenderPass<ToneMappingPass>(sampler.name, out var pass, sampler);

            if (material == null) material = new Material(Shader.Find("YutrelRP/ToneMapping"));

            pass.source_color = textures.scene_color;
            pass.pass_id = (int)settings.tone_mapping.mode;
            builder.UseTexture(pass.source_color);
            builder.SetRenderAttachment(textures.final_color, 0);

            builder.SetRenderFunc<ToneMappingPass>(static (pass, context) => { pass.Render(context); });
        }
    }
}