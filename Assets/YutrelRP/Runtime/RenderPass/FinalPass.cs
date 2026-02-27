using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace YutrelRP
{
    internal class FinalPass
    {
        private static readonly ProfilingSampler sampler = new("Final Pass");
        private static readonly int source_color_ID = Shader.PropertyToID("_SourceColor");
        private static Material material;

        internal static void Record(RenderGraph render_graph, RenderTargets textures)
        {
            using var builder = render_graph.AddRasterRenderPass<FinalPass>(sampler.name, out var pass, sampler);
            if (material == null) material = CoreUtils.CreateEngineMaterial(Shader.Find("YutrelRP/ToneMapping"));

            pass.source_color = textures.final_color;

            builder.UseTexture(pass.source_color);
            builder.SetRenderAttachment(textures.camera_output, 0);

            builder.SetRenderFunc<FinalPass>(static (pass, context) => { pass.Render(context); });
        }

        // data
        private TextureHandle source_color;

        private void Render(RasterGraphContext context)
        {
            var cmd = context.cmd;

            material.SetTexture(source_color_ID, source_color);

            CoreUtils.DrawFullScreen(cmd, material);
        }

        public static void Cleanup()
        {
            CoreUtils.Destroy(material);
            material = null;
        }
    }
}
