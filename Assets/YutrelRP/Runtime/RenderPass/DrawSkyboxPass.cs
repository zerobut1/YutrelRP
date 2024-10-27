using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using YutrelRP.FrameData;

namespace YutrelRP
{
    internal class DrawSkyboxPass : YutrelRenderPass
    {
        private static readonly ProfilingSampler s_draw_skybox_profiling_sampler =
            new ProfilingSampler("Draw Skybox Pass");

        private class PassData
        {
            internal RendererListHandle skybox_list_handle;
        }

        internal void Render(RenderGraph render_graph, CameraData camera_data, TextureHandle scene_color,
            TextureHandle scene_depth)
        {
            using var builder = render_graph.AddRasterRenderPass<PassData>("Draw Skybox Pass",
                out var pass_data,
                s_draw_skybox_profiling_sampler);

            pass_data.skybox_list_handle = render_graph.CreateSkyboxRendererList(camera_data.camera);
            builder.UseRendererList(pass_data.skybox_list_handle);

            builder.SetRenderAttachment(scene_color, 0, AccessFlags.Write);
            builder.SetRenderAttachmentDepth(scene_depth, AccessFlags.Write);

            // builder.AllowPassCulling(false);

            builder.SetRenderFunc((PassData data, RasterGraphContext context) =>
            {
                context.cmd.DrawRendererList(data.skybox_list_handle);
            });
        }
    }
}