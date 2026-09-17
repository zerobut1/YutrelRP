using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RendererUtils;
using UnityEngine.Rendering.RenderGraphModule;

namespace YutrelRP
{
    /// <summary>
    /// Optional opt-in depth prepass. Only materials with an explicit DepthOnly
    /// pass participate, so existing materials retain their current depth policy.
    /// </summary>
    internal sealed class DepthPrepass
    {
        private static readonly ProfilingSampler sampler = new("Depth Prepass");
        private static readonly ShaderTagId shader_tag_id = new("DepthOnly");

        internal static void Record(RenderGraph render_graph, Camera camera,
            CullingResults culling_results, RenderTargets textures)
        {
            using var builder = render_graph.AddRasterRenderPass<DepthPrepass>(
                sampler.name, out var pass, sampler);

            var desc = new RendererListDesc(shader_tag_id, culling_results, camera)
            {
                sortingCriteria = SortingCriteria.CommonOpaque,
                renderQueueRange = RenderQueueRange.opaque
            };
            pass.renderer_list = render_graph.CreateRendererList(desc);
            builder.UseRendererList(pass.renderer_list);
            builder.SetRenderAttachmentDepth(textures.scene_depth, AccessFlags.ReadWrite);
            builder.SetRenderFunc<DepthPrepass>(static (data, context) =>
                context.cmd.DrawRendererList(data.renderer_list));
        }

        private RendererListHandle renderer_list;
    }
}
