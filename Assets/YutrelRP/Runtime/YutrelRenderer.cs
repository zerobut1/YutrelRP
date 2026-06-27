using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace YutrelRP
{
    public class YutrelRenderer
    {
        private readonly YutrelRPSettings settings;
#if UNITY_EDITOR
        private readonly YutrelRPDebugSettings debug_settings;
#endif
        private readonly ContextContainer frame_data = new();
        private readonly YutrelRayTracingSceneManager ray_tracing_scene_manager = new();

#if UNITY_EDITOR
        internal YutrelRenderer(YutrelRPSettings settings, YutrelRPDebugSettings debug_settings)
        {
            this.settings = settings;
            this.debug_settings = debug_settings;
        }
#else
        public YutrelRenderer(YutrelRPSettings settings)
        {
            this.settings = settings;
        }
#endif

        public void Dispose()
        {
            ray_tracing_scene_manager.Dispose();
            DirectionalLightPass.Cleanup();
            EnvironmentLightingPass.Cleanup();
            ScreenSpaceAmbientOcclusionPass.Cleanup();
            ShadowMaskPass.Cleanup();
            ToneMappingPass.Cleanup();
#if UNITY_EDITOR
            DebugViewPass.Cleanup();
            UnsupportedShadersPass.Cleanup();
#endif
            LightResources.Cleanup();
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

            VolumeManager.instance.Update(camera.transform, ~0);
            var shadow_settings = YutrelShadowSettings.Resolve(settings.shadowSettings, VolumeManager.instance.stack);
            var post_process_settings =
                YutrelSceneRenderSettings.Resolve(VolumeManager.instance.stack);

            // culling
            if (!camera.TryGetCullingParameters(out var culling_parameters)) return;
            culling_parameters.shadowDistance = Mathf.Min(shadow_settings.max_distance, camera.farClipPlane);
            culling_parameters.conservativeEnclosingSphere = shadow_settings.conservative_enclosing_sphere;
            culling_parameters.numIterationsEnclosingSphere = shadow_settings.num_iterations_enclosing_sphere;
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
                currentFrameIndex = Time.frameCount
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

                    SetupLightPass.Record(render_graph, context, camera, culling_results, shadow_settings, ref light_resources,
                        ref shadow_resources);

                    ShadowPass.Record(render_graph, shadow_resources, shadow_settings);

                    SetupPass.Record(render_graph, camera, ref textures, attachment_size, post_process_settings);

                    BasePass.Record(render_graph, camera, culling_results, textures);

                    ShadowMaskPass.Record(render_graph, textures, light_resources, shadow_resources, shadow_settings,
                        attachment_size);

                    DirectionalLightPass.Record(render_graph, textures, light_resources);

                    ScreenSpaceAmbientOcclusionPass.Record(render_graph, textures, settings.ambientOcclusionSettings,
                        attachment_size);

                    EnvironmentLightingPass.Record(render_graph, textures, light_resources);

                    SkyboxPass.Record(render_graph, camera, textures, light_resources);

#if UNITY_EDITOR
                    UnsupportedShadersPass.Record(render_graph, camera, culling_results, textures);
                    GizmosPass.Record(render_graph, camera, textures.scene_color, textures.scene_depth,
                        GizmoSubset.PreImageEffects);
#endif

                    ToneMappingPass.Record(render_graph, textures, post_process_settings);

#if UNITY_EDITOR
                    DebugViewPass.Record(render_graph, camera, textures, light_resources, shadow_resources,
                        shadow_settings, debug_settings, attachment_size);

                    GizmosPass.Record(render_graph, camera, textures.final_color, textures.scene_depth,
                        GizmoSubset.PostImageEffects);
#endif

                    FinalPass.Record(render_graph, camera, textures);
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
