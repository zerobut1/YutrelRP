using System;
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
            DirectionalLightPass.Cleanup();
            EnvironmentLightingPass.Cleanup();
            ShadowMaskPass.Cleanup();
            ToneMappingPass.Cleanup();
#if UNITY_EDITOR
            DebugViewPass.Cleanup();
#endif
            FinalPass.Cleanup();
        }

        public void Render(RenderGraph render_graph, ScriptableRenderContext context, Camera camera)
        {
            var camera_sampler = ProfilingSampler.Get(camera.cameraType);

#if UNITY_EDITOR
            if (camera.cameraType == CameraType.SceneView)
            {
                ScriptableRenderContext.EmitWorldGeometryForSceneView(camera);
            }
#endif

            // culling
            if (!camera.TryGetCullingParameters(out var culling_parameters)) return;
            culling_parameters.shadowDistance = Mathf.Min(settings.shadowSettings.max_distance, camera.farClipPlane);
            var culling_results = context.Cull(ref culling_parameters);

            // render graph
            var command_buffer = CommandBufferPool.Get();
            var execute_command_buffer = false;

            var render_graph_parameters = new RenderGraphParameters
            {
                scriptableRenderContext = context,
                commandBuffer = command_buffer,
                executionId = camera.GetEntityId(),
                generateDebugData = RenderGraph.isRenderGraphViewerActive,
                currentFrameIndex = Time.frameCount,
                rendererListCulling = true
            };

            try
            {
                render_graph.BeginRecording(render_graph_parameters);
                using (new RenderGraphProfilingScope(render_graph, camera_sampler))
                {
                    var camera_target_texture = camera.targetTexture;
                    var attachment_size = camera_target_texture == null
                        ? new Vector2Int(camera.pixelWidth, camera.pixelHeight)
                        : new Vector2Int(camera_target_texture.width, camera_target_texture.height);

                    var textures = frame_data.GetOrCreate<RenderTargets>();
                    var light_resources = frame_data.GetOrCreate<LightResources>();
                    var shadow_resources = frame_data.GetOrCreate<ShadowResources>();
                    shadow_resources.Reset();

                    SetupLightPass.Record(render_graph, context, culling_results, settings, ref light_resources,
                        ref shadow_resources);

                    ShadowPass.Record(render_graph, shadow_resources, settings.shadowSettings);

                    SetupPass.Record(render_graph, camera, ref textures, attachment_size);

                    BasePass.Record(render_graph, camera, culling_results, textures);

                    ShadowMaskPass.Record(render_graph, textures, light_resources, shadow_resources, settings.shadowSettings,
                        attachment_size);

                    DirectionalLightPass.Record(render_graph, textures, light_resources);

                    EnvironmentLightingPass.Record(render_graph, textures, light_resources);

                    SkyboxPass.Record(render_graph, camera, textures);

                    DefaultShaderPass.Record(render_graph, camera, culling_results, textures);

                    ToneMappingPass.Record(render_graph, textures, settings.postProcessSettings);

#if UNITY_EDITOR
                    DebugViewPass.Record(render_graph, camera, textures, light_resources, shadow_resources,
                        settings.shadowSettings, settings.debugViewMode, attachment_size);
#endif

                    FinalPass.Record(render_graph, textures);
                }

                render_graph.EndRecordingAndExecute();
                execute_command_buffer = true;
            }
            catch (Exception exception)
            {
                render_graph.ResetGraphAndLogException(exception);
            }
            finally
            {
                if (execute_command_buffer)
                {
                    context.ExecuteCommandBuffer(command_buffer);
                    context.Submit();
                }

                CommandBufferPool.Release(command_buffer);
            }
        }
    }
}
