using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace YutrelRP
{
    public class YutrelRenderer
    {
        private readonly YutrelRPSettings settings;
        private readonly ContextContainer frame_data = new();

        public YutrelRenderer(YutrelRPSettings settings)
        {
            this.settings = settings;
        }

        public void Dispose()
        {
        }

        public void Render(RenderGraph render_graph, ScriptableRenderContext context, Camera camera)
        {
            var camera_sampler = ProfilingSampler.Get(camera.cameraType);

            // culling
            if (!camera.TryGetCullingParameters(out var culling_parameters)) return;
            culling_parameters.shadowDistance = Mathf.Min(settings.shadowSettings.max_distance, camera.farClipPlane);
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
                var textures = frame_data.GetOrCreate<RenderTargets>();
                var light_resources = frame_data.GetOrCreate<LightResources>();
                var shadow_reources = frame_data.GetOrCreate<ShadowResources>();
                shadow_reources.Reset();

                SetupLightPass.Record(render_graph, culling_results, settings, ref light_resources,
                    ref shadow_reources);

                ShadowPass.Record(render_graph, shadow_reources, settings.shadowSettings);

                SetupPass.Record(render_graph, camera, ref textures,
                    new Vector2Int(camera.pixelWidth, camera.pixelHeight));

                BasePass.Record(render_graph, camera, culling_results, textures);

                ShadowMaskPass.Record(render_graph, textures, light_resources, shadow_reources,
                    new Vector2Int(camera.pixelWidth, camera.pixelHeight));

                DirectionalLightPass.Record(render_graph, textures, light_resources);

                SkyboxPass.Record(render_graph, camera, textures);

                DefaultShaderPass.Record(render_graph, camera, culling_results, textures);

                ToneMappingPass.Record(render_graph, textures, settings.postProcessSettings);

                FinalPass.Record(render_graph, textures);
            }

            render_graph.EndRecordingAndExecute();

            context.ExecuteCommandBuffer(render_graph_parameters.commandBuffer);
            context.Submit();
            CommandBufferPool.Release(render_graph_parameters.commandBuffer);
        }
    }
}