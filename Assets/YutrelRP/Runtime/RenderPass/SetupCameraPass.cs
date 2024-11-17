using UnityEngine;
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
                // var camera_data = data.camera_data;
                var camera = data.camera_data.camera;

                Matrix4x4 view_matrix = camera.worldToCameraMatrix;
                Matrix4x4 projection_matrix = camera.projectionMatrix;
                projection_matrix = GL.GetGPUProjectionMatrix(projection_matrix, true);
                Matrix4x4 camera_to_world = (projection_matrix * view_matrix).inverse;

                context.cmd.SetGlobalMatrix(Shader.PropertyToID("_inverse_VP_matrix"), camera_to_world);

                context.cmd.SetupCameraProperties(data.camera_data.camera);
            });
        }
    }
}