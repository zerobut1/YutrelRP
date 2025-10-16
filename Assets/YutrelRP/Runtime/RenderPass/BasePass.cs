using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RendererUtils;
using UnityEngine.Rendering.RenderGraphModule;

namespace YutrelRP
{
    internal class BasePass
    {
        private static readonly ProfilingSampler sampler = new("Base Pass");

        private static readonly ShaderTagId shader_tag_id = new("GBuffer");

        // data
        private RendererListHandle opaque_renderer_list;

        private void Render(RasterGraphContext context)
        {
            context.cmd.DrawRendererList(opaque_renderer_list);
        }

        public static void Record(RenderGraph render_graph, Camera camera, CullingResults culling_results,
            RenderTargets textures)
        {
            using var builder = render_graph.AddRasterRenderPass<BasePass>("Base Pass", out var pass, sampler);

            builder.SetRenderAttachment(textures.scene_color, 0);
            builder.SetRenderAttachment(textures.GBuffer_A, 1);
            builder.SetRenderAttachment(textures.GBuffer_B, 2);
            builder.SetRenderAttachment(textures.GBuffer_C, 3);
            builder.SetRenderAttachmentDepth(textures.scene_depth, AccessFlags.ReadWrite);

            // 不透明
            var opaque_renderer_desc =
                new RendererListDesc(shader_tag_id, culling_results, camera)
                {
                    sortingCriteria = SortingCriteria.CommonOpaque,
                    renderQueueRange = RenderQueueRange.opaque,
                };
            pass.opaque_renderer_list = render_graph.CreateRendererList(opaque_renderer_desc);
            builder.UseRendererList(pass.opaque_renderer_list);

            builder.SetRenderFunc<BasePass>(static (pass, context) => pass.Render(context));
        }
    }
}