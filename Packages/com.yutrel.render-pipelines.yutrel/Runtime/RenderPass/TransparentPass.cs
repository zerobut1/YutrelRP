using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RendererUtils;
using UnityEngine.Rendering.RenderGraphModule;

namespace YutrelRP
{
    internal class TransparentPass
    {
        private static readonly ProfilingSampler sampler = new("Transparent Pass");
        private static readonly ShaderTagId shader_tag_id = new("YutrelForwardOnly");

        internal static void Record(RenderGraph render_graph, Camera camera, CullingResults culling_results,
            RenderTargets textures, ForwardPassBindings bindings)
        {
            bindings.RecordPreparation(render_graph, false, transparent: true);
            using var builder = render_graph.AddRasterRenderPass<TransparentPass>(sampler.name, out var pass, sampler);
            var desc = new RendererListDesc(shader_tag_id, culling_results, camera)
            {
                sortingCriteria = SortingCriteria.CommonTransparent,
                renderQueueRange = RenderQueueRange.transparent
            };
            pass.renderer_list = render_graph.CreateRendererList(desc);
            builder.UseRendererList(pass.renderer_list);
            bindings.DeclareResources(builder, false, transparent: true);
            builder.SetRenderAttachment(textures.scene_color, 0, AccessFlags.ReadWrite);
            // Materials own depth/stencil state, including transparent depth writers.
            builder.SetRenderAttachmentDepth(textures.scene_depth, AccessFlags.ReadWrite);
            builder.SetRenderFunc<TransparentPass>(static (data, context) =>
            {
                context.cmd.DrawRendererList(data.renderer_list);
            });
        }

        private RendererListHandle renderer_list;
    }
}
