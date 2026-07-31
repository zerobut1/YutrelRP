using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RendererUtils;
using UnityEngine.Rendering.RenderGraphModule;

namespace YutrelRP
{
    internal class EndfieldForwardPass
    {
        private static readonly ProfilingSampler sampler = new("Endfield Forward Pass");
        private static readonly ShaderTagId shader_tag_id = new("EndfieldForward");

        internal static void Record(RenderGraph render_graph, Camera camera, CullingResults culling_results,
            RenderTargets textures, LightResources light_resources)
        {
            if (light_resources.directional_light_count == 0) return;

            using var builder =
                render_graph.AddRasterRenderPass<EndfieldForwardPass>(sampler.name, out var pass, sampler);

            var renderer_desc = new RendererListDesc(shader_tag_id, culling_results, camera)
            {
                sortingCriteria = SortingCriteria.CommonOpaque,
                renderQueueRange = RenderQueueRange.opaque
            };
            pass.renderer_list = render_graph.CreateRendererList(renderer_desc);
            pass.directional_light_data_buffer = light_resources.directional_light_data_buffer;
            pass.shadow_mask = textures.shadow_mask;

            builder.UseRendererList(pass.renderer_list);
            builder.UseBuffer(pass.directional_light_data_buffer);
            builder.UseTexture(pass.shadow_mask);
            builder.SetRenderAttachment(textures.scene_color, 0, AccessFlags.ReadWrite);
            builder.SetRenderAttachmentDepth(textures.scene_depth, AccessFlags.ReadWrite);
            builder.AllowGlobalStateModification(true);
            builder.SetRenderFunc<EndfieldForwardPass>(static (pass, context) => pass.Render(context));
        }

        private RendererListHandle renderer_list;
        private BufferHandle directional_light_data_buffer;
        private TextureHandle shadow_mask;

        private void Render(RasterGraphContext context)
        {
            context.cmd.SetGlobalBuffer(LightResources.directional_light_data_ID, directional_light_data_buffer);
            context.cmd.SetGlobalTexture(RenderTargets.shadow_mask_ID, shadow_mask);
            context.cmd.DrawRendererList(renderer_list);
        }
    }
}
