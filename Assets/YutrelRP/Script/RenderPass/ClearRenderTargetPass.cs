using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using YutrelRP.FrameData;

namespace YutrelRP
{
    public partial class YutrelRenderGraphRecorder
    {
        private static readonly ProfilingSampler
            s_clear_rt_profiling_sampler = new ProfilingSampler("Clear Render Target Pass");

        internal class ClearRenderTargetPassData
        {
            internal RTClearFlags clear_flags;
            internal Color clear_color;
        }

        private void AddClearRenderTargetPass(RenderGraph render_graph, CameraData camera_data)
        {
            using var builder = render_graph.AddRasterRenderPass<ClearRenderTargetPassData>(
                "Clear Render Target Pass",
                out var passData,
                s_clear_rt_profiling_sampler);

            passData.clear_flags = camera_data.GetClearFlags();
            passData.clear_color = camera_data.GetClearColor();

            if (m_backbuffer_color_handle.IsValid())
            {
                builder.SetRenderAttachment(m_backbuffer_color_handle, 0, AccessFlags.Write);
            }

            if (m_backbuffer_depth_handle.IsValid())
            {
                builder.SetRenderAttachmentDepth(m_backbuffer_depth_handle, AccessFlags.Write);
            }

            // builder.AllowPassCulling(false);

            builder.SetRenderFunc((ClearRenderTargetPassData data, RasterGraphContext context) =>
            {
                context.cmd.ClearRenderTarget(data.clear_flags, data.clear_color, 1, 0);
            });
        }
    }
}