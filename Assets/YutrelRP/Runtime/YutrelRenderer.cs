using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace YutrelRP
{
    public class YutrelRenderer
    {
        private readonly YutrelRPSettings m_settings;
        private readonly ContextContainer m_frame_data = new();

        public YutrelRenderer(YutrelRPSettings settings)
        {
            m_settings = settings;
        }

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
                var light_resources = m_frame_data.GetOrCreate<LightResources>();

                SetupPass.Record(render_graph, camera, ref textures,
                    new Vector2Int(camera.pixelWidth, camera.pixelHeight));

                SetupLightPass.Record(render_graph, culling_results, m_settings, ref light_resources);

                BasePass.Record(render_graph, camera, culling_results, textures);

                DirectionalLightPass.Record(render_graph, textures, light_resources);

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