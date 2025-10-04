using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace YutrelRP
{
    public class YutrelRenderer
    {
        private readonly ContextContainer m_frame_data = new();

        public void Dispose()
        {
        }

        public void Render(RenderGraph render_graph, ScriptableRenderContext context, Camera camera)
        {
            CameraSettings camera_settings = new();

            var camera_sampler = ProfilingSampler.Get(camera.cameraType);

            // culling
            if (!camera.TryGetCullingParameters(out var culling_parameters)) return;
            var culling_results = context.Cull(ref culling_parameters);

            // render graph
            var render_graph_parameters = new RenderGraphParameters
            {
                scriptableRenderContext = context,
                commandBuffer = CommandBufferPool.Get(),
                currentFrameIndex = Time.frameCount,
                rendererListCulling = true
            };
            render_graph.BeginRecording(render_graph_parameters);
            using (new RenderGraphProfilingScope(render_graph, camera_sampler))
            {
                var textures = m_frame_data.GetOrCreate<RenderTargets>();

                SetupPass.Record(render_graph, camera, ref textures,
                    new Vector2Int(camera.pixelWidth, camera.pixelHeight));

                BasePass.Record(render_graph, camera, culling_results, textures);

                TempShadingPass.Record(render_graph, textures);

                SkyboxPass.Record(render_graph, camera, textures);

                DefaultShaderPass.Record(render_graph, camera, culling_results, textures);

                FinalPass.Record(render_graph, textures);
            }

            render_graph.EndRecordingAndExecute();

            context.ExecuteCommandBuffer(render_graph_parameters.commandBuffer);
            context.Submit();
            CommandBufferPool.Release(render_graph_parameters.commandBuffer);
        }
    }
}