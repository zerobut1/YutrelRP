using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using YutrelRP.FrameData;

namespace YutrelRP
{
    public partial class YutrelRenderGraphRecorder
    {
        private static readonly ProfilingSampler
            s_setup_camera_profiling_sampler = new ProfilingSampler("Setup Camera Pass");

        internal class SetupCameraPassData
        {
            internal CameraData camera_data;
        }

        private void AddSetupCameraPass(RenderGraph render_graph, CameraData camera_data)
        {
            using var builder = render_graph.AddRasterRenderPass<SetupCameraPassData>(
                "Setup Camera Pass",
                out var passData,
                s_setup_camera_profiling_sampler);

            passData.camera_data = camera_data;

            // builder.AllowPassCulling(false);
            builder.AllowGlobalStateModification(true);

            builder.SetRenderFunc((SetupCameraPassData data, RasterGraphContext context) =>
            {
                context.cmd.SetupCameraProperties(data.camera_data.camera);
            });
        }
    }
}