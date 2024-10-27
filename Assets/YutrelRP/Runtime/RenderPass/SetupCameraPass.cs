using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using YutrelRP.FrameData;

namespace YutrelRP
{
    internal class SetupCameraPass : YutrelRenderPass
    {
        private class PassData
        {
            internal CameraData camera_data;
        }

        internal void Setup(RenderGraph render_graph, CameraData camera_data)
        {
            using var builder = render_graph.AddRasterRenderPass<PassData>(
                "Setup Camera Pass",
                out var pass_data,
                new ProfilingSampler("Setup Camera Pass"));

            pass_data.camera_data = camera_data;

            builder.AllowGlobalStateModification(true);

            builder.SetRenderFunc((PassData data, RasterGraphContext context) =>
            {
                context.cmd.SetupCameraProperties(data.camera_data.camera);
            });
        }
    }
}