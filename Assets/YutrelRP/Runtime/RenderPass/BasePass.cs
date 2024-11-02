using UnityEngine.Rendering;
using UnityEngine.Rendering.RendererUtils;
using UnityEngine.Rendering.RenderGraphModule;
using CameraData = YutrelRP.FrameData.CameraData;

namespace YutrelRP
{
    internal class BasePass : YutrelRenderPass
    {
        private static readonly ShaderTagId s_shader_tag_id = new ShaderTagId("GBuffer");

        private class PassData
        {
            internal TextureHandle m_GBuffer_A;
            internal TextureHandle m_GBuffer_B;
            internal TextureHandle m_GBuffer_C;

            internal RendererListHandle opaque_renderer_list;
        }

        internal void Render(RenderGraph graph, CameraData camera_data, TextureHandle GBuffer_A,
            TextureHandle GBuffer_B, TextureHandle GBuffer_C, TextureHandle scene_depth)
        {
            using var builder =
                graph.AddRasterRenderPass<PassData>("Base Pass", out var pass_data,
                    new ProfilingSampler("Base Pass"));

            // GBuffer
            var camera = camera_data.camera;
            pass_data.m_GBuffer_A = GBuffer_A;
            pass_data.m_GBuffer_B = GBuffer_B;
            pass_data.m_GBuffer_C = GBuffer_C;

            builder.SetRenderAttachment(pass_data.m_GBuffer_A, 0, AccessFlags.Write);
            builder.SetRenderAttachment(pass_data.m_GBuffer_B, 1, AccessFlags.Write);
            builder.SetRenderAttachment(pass_data.m_GBuffer_C, 2, AccessFlags.Write);
            builder.SetRenderAttachmentDepth(scene_depth, AccessFlags.Write);

            // 不透明
            var opaque_renderer_desc =
                new RendererListDesc(s_shader_tag_id, camera_data.culling_results, camera_data.camera)
                {
                    sortingCriteria = SortingCriteria.CommonOpaque,
                    renderQueueRange = RenderQueueRange.opaque
                };
            pass_data.opaque_renderer_list = graph.CreateRendererList(opaque_renderer_desc);
            builder.UseRendererList(pass_data.opaque_renderer_list);

            builder.SetRenderFunc((PassData data, RasterGraphContext context) =>
            {
                context.cmd.DrawRendererList(data.opaque_renderer_list);
            });
        }
    }
}