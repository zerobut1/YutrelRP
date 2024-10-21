using UnityEngine.Rendering;
using UnityEngine.Rendering.RendererUtils;
using UnityEngine.Rendering.RenderGraphModule;
using YutrelRP.FrameData;

namespace YutrelRP
{
    public partial class YutrelRenderGraphRecorder
    {
        private static readonly ShaderTagId s_shader_tag_id = new ShaderTagId("GBuffer");

        internal class BasePassData
        {
            internal TextureHandle m_GBuffer_A;
            internal TextureHandle m_GBuffer_B;
            internal TextureHandle m_GBuffer_C;

            internal RendererListHandle opaque_renderer_list;
        }

        private BasePassData AddBasePass(RenderGraph graph, CameraData camera_data)
        {
            using var builder =
                graph.AddRasterRenderPass<BasePassData>("Base Pass", out var pass_data,
                    new ProfilingSampler("Base Pass"));

            // GBuffer
            var camera = camera_data.camera;
            pass_data.m_GBuffer_A =
                YutrelRPUtils.CreateColorTexture(graph, camera.pixelWidth, camera.pixelHeight, "GBuffer A");
            pass_data.m_GBuffer_B =
                YutrelRPUtils.CreateColorTexture(graph, camera.pixelWidth, camera.pixelHeight, "GBuffer B");
            pass_data.m_GBuffer_C =
                YutrelRPUtils.CreateColorTexture(graph, camera.pixelWidth, camera.pixelHeight, "GBuffer C");

            builder.SetRenderAttachment(pass_data.m_GBuffer_A, 0, AccessFlags.Write);
            builder.SetRenderAttachment(pass_data.m_GBuffer_B, 1, AccessFlags.Write);
            builder.SetRenderAttachment(pass_data.m_GBuffer_C, 2, AccessFlags.Write);
            builder.SetRenderAttachmentDepth(m_backbuffer_depth, AccessFlags.Write);

            // 不透明
            var opaque_renderer_desc =
                new RendererListDesc(s_shader_tag_id, camera_data.culling_results, camera_data.camera)
                {
                    sortingCriteria = SortingCriteria.CommonOpaque,
                    renderQueueRange = RenderQueueRange.opaque
                };
            pass_data.opaque_renderer_list = graph.CreateRendererList(opaque_renderer_desc);
            builder.UseRendererList(pass_data.opaque_renderer_list);

            builder.SetRenderFunc((BasePassData data, RasterGraphContext context) =>
            {
                context.cmd.DrawRendererList(data.opaque_renderer_list);
            });

            return pass_data;
        }
    }
}