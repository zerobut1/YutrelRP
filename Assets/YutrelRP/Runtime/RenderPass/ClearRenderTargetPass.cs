using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using YutrelRP.FrameData;

namespace YutrelRP
{
    public partial class YutrelRenderGraphRecorder
    {
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
                new ProfilingSampler("Clear Render Target Pass"));

            passData.clear_flags = camera_data.GetClearFlags();
            passData.clear_color = camera_data.GetClearColor();

            if (m_backbuffer_color.IsValid())
            {
                builder.SetRenderAttachment(m_backbuffer_color, 0, AccessFlags.Write);
            }

            if (m_backbuffer_depth.IsValid())
            {
                builder.SetRenderAttachmentDepth(m_backbuffer_depth, AccessFlags.Write);
            }

            builder.SetRenderFunc((ClearRenderTargetPassData data, RasterGraphContext context) =>
            {
                context.cmd.ClearRenderTarget(data.clear_flags, data.clear_color, 1, 0);
            });
        }
    }
}