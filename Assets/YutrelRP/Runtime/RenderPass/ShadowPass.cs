using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RendererUtils;
using UnityEngine.Rendering.RenderGraphModule;

namespace YutrelRP
{
    internal class ShadowPass
    {
        // private static readonly ProfilingSampler sampler = new("Shadow Pass");

        public static void Record(RenderGraph render_graph, ShadowResources shadow_resources, ShadowSettings settings)
        {
            if (shadow_resources.shadowed_directional_light_count <= 0)
            {
                return;
            }

            var cascade_count = settings.directional.cascade_count;
            for (int cascade_index = 0; cascade_index < cascade_count; cascade_index++)
            {
                ProfilingSampler sampler = new("Shadow Pass" + cascade_index.ToString());

                using var builder = render_graph.AddRasterRenderPass<ShadowPass>(sampler.name, out var pass, sampler);

                pass.render_info = shadow_resources.directional_render_info[cascade_index];

                builder.SetRenderAttachmentDepth(shadow_resources.directional_atlas, AccessFlags.Write, 1,
                    1);

                builder.UseRendererList(pass.render_info.renderer_list);
                
                builder.AllowPassCulling(false);
                builder.SetRenderFunc<ShadowPass>(static (pass, context) => pass.Render(context));
            }
        }

        // data
        private ShadowResources.RenderInfo render_info;

        private void Render(RasterGraphContext context)
        {
            var cmd = context.cmd;
            
            cmd.ClearRenderTarget(true, false, Color.clear);
            cmd.SetViewProjectionMatrices(render_info.view, render_info.projection);
            cmd.DrawRendererList(render_info.renderer_list);
        }
    }
}