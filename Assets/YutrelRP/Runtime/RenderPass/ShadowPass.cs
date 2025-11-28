using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RendererUtils;
using UnityEngine.Rendering.RenderGraphModule;

namespace YutrelRP
{
    internal class ShadowPass
    {
        private static readonly ProfilingSampler sampler = new("Shadow Pass");

        public static void Record(RenderGraph render_graph, ShadowResources shadow_resources, ShadowSettings settings)
        {
            if (shadow_resources.shadowed_directional_light_count <= 0)
            {
                return;
            }

            using var builder = render_graph.AddRasterRenderPass<ShadowPass>(sampler.name, out var pass, sampler);

            pass.cascade_count = settings.directional.cascade_count;
            pass.tile_size = (int)settings.directional.atlas_tile_size;
            pass.render_infos = shadow_resources.directional_render_info;

            builder.SetRenderAttachmentDepth(shadow_resources.directional_atlas);

            for (int cascade_index = 0; cascade_index < pass.cascade_count; cascade_index++)
            {
                builder.UseRendererList(pass.render_infos[cascade_index].renderer_list);
            }

            builder.AllowPassCulling(false);
            builder.SetRenderFunc<ShadowPass>(static (pass, context) => pass.Render(context));
        }

        // data
        private int cascade_count;
        private int tile_size;
        private ShadowResources.RenderInfo[] render_infos;

        private void Render(RasterGraphContext context)
        {
            var cmd = context.cmd;

            cmd.ClearRenderTarget(true, false, Color.clear);

            cmd.SetGlobalDepthBias(0.0f, 2.0f);

            for (int cascade_index = 0; cascade_index < cascade_count; cascade_index++)
            {
                var render_info = render_infos[cascade_index];

                cmd.SetViewport(new Rect(0, cascade_index * tile_size, tile_size, tile_size));
                cmd.SetViewProjectionMatrices(render_info.view, render_info.projection);
                cmd.DrawRendererList(render_info.renderer_list);
            }

            cmd.SetGlobalDepthBias(0.0f, 0.0f);
        }
    }
}