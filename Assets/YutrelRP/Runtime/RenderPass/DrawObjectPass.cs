using UnityEngine.Rendering;
using UnityEngine.Rendering.RendererUtils;
using UnityEngine.Rendering.RenderGraphModule;
using YutrelRP.FrameData;

namespace YutrelRP
{
    public partial class YutrelRenderGraphRecorder
    {
        // private static readonly ShaderTagId s_shader_tag_id = new ShaderTagId("ExampleLightModeTag");

        private static readonly ProfilingSampler s_draw_object_profiling_sampler =
            new ProfilingSampler("Draw Object Pass");

        internal class DrawObjectPassData
        {
            internal RendererListHandle opaque_renderer_list_handle;
        }

        private void AddDrawObjectPass(RenderGraph render_graph, CameraData camera_data)
        {
            using var builder = render_graph.AddRasterRenderPass<DrawObjectPassData>("Draw Object Pass",
                out var pass_data, s_draw_object_profiling_sampler);

            // 不透明
            var opaque_renderer_desc =
                new RendererListDesc(s_shader_tag_id, camera_data.culling_results, camera_data.camera)
                {
                    sortingCriteria = SortingCriteria.CommonOpaque,
                    renderQueueRange = RenderQueueRange.opaque
                };
            pass_data.opaque_renderer_list_handle = render_graph.CreateRendererList(opaque_renderer_desc);
            builder.UseRendererList(pass_data.opaque_renderer_list_handle);

            // BackBuffer
            if (m_backbuffer_color_handle.IsValid())
            {
                builder.SetRenderAttachment(m_backbuffer_color_handle, 0, AccessFlags.Write);
            }

            if (m_backbuffer_depth_handle.IsValid())
            {
                builder.SetRenderAttachmentDepth(m_backbuffer_depth_handle, AccessFlags.Write);
            }

            // builder.AllowPassCulling(false);

            builder.SetRenderFunc((DrawObjectPassData data, RasterGraphContext context) =>
            {
                context.cmd.DrawRendererList(data.opaque_renderer_list_handle);
            });
        }
    }
}