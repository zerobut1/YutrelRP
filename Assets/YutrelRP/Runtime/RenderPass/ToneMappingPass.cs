using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace YutrelRP
{
    internal class ToneMappingPass
    {
        private static readonly ProfilingSampler sampler = new("Tone Mapping Pass");
        private static readonly int
            source_color_ID = Shader.PropertyToID("_SourceColor"),
            source_scale_bias_ID = Shader.PropertyToID("_SourceScaleBias");
        private static Material material;
        private static readonly Vector4 identity_source_scale_bias = new(1.0f, 1.0f, 0.0f, 0.0f);

        internal static void Record(RenderGraph render_graph, RenderTargets textures, PostProcessSettings settings)
        {
            using var builder = render_graph.AddRasterRenderPass<ToneMappingPass>(sampler.name, out var pass, sampler);
            if (material == null) material = CoreUtils.CreateEngineMaterial(Shader.Find("YutrelRP/ToneMapping"));

            pass.source_color = textures.scene_color;
            pass.pass_id = settings == null
                ? (int)PostProcessSettings.ToneMappingSettings.Mode.None
                : (int)settings.tone_mapping.mode;
            builder.UseTexture(pass.source_color);
            builder.SetRenderAttachment(textures.final_color, 0);

            builder.SetRenderFunc<ToneMappingPass>(static (pass, context) => { pass.Render(context); });
        }

        // data
        private TextureHandle source_color;

        private int pass_id;

        private void Render(RasterGraphContext context)
        {
            var cmd = context.cmd;

            material.SetTexture(source_color_ID, source_color);
            material.SetVector(source_scale_bias_ID, identity_source_scale_bias);

            CoreUtils.DrawFullScreen(cmd, material, null, pass_id);
        }

        public static void Cleanup()
        {
            CoreUtils.Destroy(material);
            material = null;
        }
    }
}

