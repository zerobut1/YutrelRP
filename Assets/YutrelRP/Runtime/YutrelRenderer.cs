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
            DDGIProbeTrace.Cleanup();
            DirectionalLightPass.Cleanup();
            EnvironmentLightingPass.Cleanup();
            RayTracingSmokeTest.Cleanup();
            ScreenSpaceAmbientOcclusionPass.Cleanup();
            ShadowMaskPass.Cleanup();
            ToneMappingPass.Cleanup();
#if UNITY_EDITOR
            DebugViewPass.Cleanup();
            DDGIScreenTraceDepthCopyPass.Cleanup();
            DDGIProbeDebugPass.Cleanup();
            DDGITextureDump.Cleanup();
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
                    var ddgi_resources = frame_data.GetOrCreate<DDGIResources>();
                    var light_resources = frame_data.GetOrCreate<LightResources>();
                    var shadow_resources = frame_data.GetOrCreate<ShadowResources>();
                    ddgi_resources.Reset();
                    shadow_resources.Reset();
                    VolumeManager.instance.Update(camera.transform, ~0);
                    var post_process_settings =
                        YutrelSceneRenderSettings.Resolve(VolumeManager.instance.stack);

                    SetupLightPass.Record(render_graph, context, camera, culling_results, settings, ref light_resources,
                        ref shadow_resources);

                    ShadowPass.Record(render_graph, shadow_resources, settings.shadowSettings);

                    SetupPass.Record(render_graph, camera, ref textures, attachment_size, post_process_settings);

                    BasePass.Record(render_graph, camera, culling_results, textures);

                    ShadowMaskPass.Record(render_graph, textures, light_resources, shadow_resources, settings.shadowSettings,
                        attachment_size);

                    DirectionalLightPass.Record(render_graph, textures, light_resources);

                    ScreenSpaceAmbientOcclusionPass.Record(render_graph, textures, settings.ambientOcclusionSettings,
                        attachment_size);

                    DDGIProbeTrace.Record(render_graph, camera, settings, light_resources, textures, attachment_size,
                        ref ddgi_resources);

                    EnvironmentLightingPass.Record(render_graph, textures, light_resources, ddgi_resources,
                        settings.ddgiSettings);

                    SkyboxPass.Record(render_graph, camera, textures, light_resources);

#if UNITY_EDITOR
                    UnsupportedShadersPass.Record(render_graph, camera, culling_results, textures);
                    GizmosPass.Record(render_graph, camera, textures.scene_color, textures.scene_depth,
                        GizmoSubset.PreImageEffects);
#endif

                    ToneMappingPass.Record(render_graph, textures, post_process_settings);

                    if (RayTracingSmokeTest.IsEnabled(settings))
                    {
                        RayTracingSmokeTest.Record(render_graph, camera, ref textures, settings, attachment_size);
                    }

#if UNITY_EDITOR
                    DebugViewPass.Record(render_graph, camera, textures, light_resources, shadow_resources,
                        settings.shadowSettings, ddgi_resources, settings.debugViewMode,
                        settings.ddgiSettings, attachment_size);

                    DDGIProbeDebugPass.Record(render_graph, camera, textures, ddgi_resources, settings.debugViewMode,
                        settings.ddgiSettings);

                    GizmosPass.Record(render_graph, camera, textures.final_color, textures.scene_depth,
                        GizmoSubset.PostImageEffects);

                    DDGITextureDump.Record(render_graph, camera, ddgi_resources, settings.ddgiSettings,
                        attachment_size);
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
#if UNITY_EDITOR
                    DDGITextureDump.StartReadbacks();
                    DDGITextureDump.Update();
#endif
                }

                CommandBufferPool.Release(command_buffer);
            }
        }
    }
}
