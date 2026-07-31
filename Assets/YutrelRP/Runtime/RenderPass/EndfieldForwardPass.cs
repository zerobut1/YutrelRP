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
            RenderTargets textures)
        {
            using var builder =
                render_graph.AddRasterRenderPass<EndfieldForwardPass>(sampler.name, out var pass, sampler);

            var renderer_desc = new RendererListDesc(shader_tag_id, culling_results, camera)
            {
                sortingCriteria = SortingCriteria.CommonOpaque,
                renderQueueRange = RenderQueueRange.opaque
            };
            pass.renderer_list = render_graph.CreateRendererList(renderer_desc);

            builder.UseRendererList(pass.renderer_list);
            builder.SetRenderAttachment(textures.scene_color, 0, AccessFlags.ReadWrite);
            builder.SetRenderAttachmentDepth(textures.scene_depth, AccessFlags.ReadWrite);
            builder.SetRenderFunc<EndfieldForwardPass>(static (pass, context) => pass.Render(context));
        }

        private RendererListHandle renderer_list;

        private void Render(RasterGraphContext context)
        {
            context.cmd.DrawRendererList(renderer_list);
        }
    }
}
