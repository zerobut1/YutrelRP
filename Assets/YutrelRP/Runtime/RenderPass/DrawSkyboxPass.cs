using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using YutrelRP.FrameData;

namespace YutrelRP
{
    public partial class YutrelRenderGraphRecorder
    {
        private static readonly ProfilingSampler s_draw_skybox_profiling_sampler =
            new ProfilingSampler("Draw Skybox Pass");

        internal class DrawSkyboxPassData
        {
            internal RendererListHandle skybox_list_handle;
        }

        private void AddDrawSkyboxPass(RenderGraph render_graph, CameraData camera_data)
        {
            using var builder = render_graph.AddRasterRenderPass<DrawSkyboxPassData>("Draw Skybox Pass",
                out var pass_data,
                s_draw_skybox_profiling_sampler);

            pass_data.skybox_list_handle = render_graph.CreateSkyboxRendererList(camera_data.camera);
            builder.UseRendererList(pass_data.skybox_list_handle);

            if (m_backbuffer_color.IsValid())
            {
                builder.SetRenderAttachment(m_backbuffer_color, 0, AccessFlags.Write);
            }

            if (m_backbuffer_depth.IsValid())
            {
                builder.SetRenderAttachmentDepth(m_backbuffer_depth, AccessFlags.Write);
            }

            // builder.AllowPassCulling(false);

            builder.SetRenderFunc((DrawSkyboxPassData data, RasterGraphContext context) =>
            {
                context.cmd.DrawRendererList(data.skybox_list_handle);
            });
        }
    }
}