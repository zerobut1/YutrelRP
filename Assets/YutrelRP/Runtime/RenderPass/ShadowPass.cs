using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RendererUtils;
using UnityEngine.Rendering.RenderGraphModule;

namespace YutrelRP
{
    internal class ShadowPass
    {
        private static readonly ProfilingSampler sampler = new("Shadow Pass");

        static int dir_shadow_atlas_Id = Shader.PropertyToID("_DirectionalShadowAtlas");

        // data
        private ShadowResources.RenderInfo render_info;

        public static void Record(RenderGraph render_graph, ShadowResources shadow_resources)
        {
            if (shadow_resources.shadowed_directional_light_count <= 0)
            {
                return;
            }

            using var builder = render_graph.AddRasterRenderPass<ShadowPass>("Shadow Pass", out var pass, sampler);

            pass.render_info = shadow_resources.directional_render_info[0];

            // builder.SetRenderAttachment(shadow_resources.directional_atlas, 0);
            builder.SetRenderAttachmentDepth(shadow_resources.directional_atlas, AccessFlags.Write);
            builder.UseRendererList(pass.render_info.renderer_list);

            builder.SetRenderFunc<ShadowPass>(static (pass, context) => pass.Render(context));
        }

        private void Render(RasterGraphContext context)
        {
            var cmd = context.cmd;

            cmd.ClearRenderTarget(true, false, Color.clear);
            cmd.SetViewProjectionMatrices(render_info.view, render_info.projection);
            cmd.DrawRendererList(render_info.renderer_list);
        }
    }
}