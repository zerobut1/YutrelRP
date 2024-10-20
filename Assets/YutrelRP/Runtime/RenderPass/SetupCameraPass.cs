using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using YutrelRP.FrameData;

namespace YutrelRP
{
    public partial class YutrelRenderGraphRecorder
    {
        internal class SetupCameraPassData
        {
            internal CameraData camera_data;
        }

        private void AddSetupCameraPass(RenderGraph render_graph, CameraData camera_data)
        {
            using var builder = render_graph.AddRasterRenderPass<SetupCameraPassData>(
                "Setup Camera Pass",
                out var passData,
                new ProfilingSampler("Setup Camera Pass"));

            passData.camera_data = camera_data;

            builder.AllowGlobalStateModification(true);

            builder.SetRenderFunc((SetupCameraPassData data, RasterGraphContext context) =>
            {
                context.cmd.SetupCameraProperties(data.camera_data.camera);
            });
        }
    }
}