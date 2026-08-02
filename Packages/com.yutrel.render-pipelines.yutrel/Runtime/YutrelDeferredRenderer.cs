using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace YutrelRP
{
    internal sealed class YutrelDeferredRenderer : YutrelRenderer
    {
        private readonly YutrelDeferredRendererSettings settings;
        private readonly ContextContainer frameData = new();
        private readonly YutrelRayTracingWorld rayTracingWorld = new();
        private readonly YutrelDDGIResourceManager ddgiResourceManager = new();

        private ResolvedShadowSettings currentShadowSettings;
        private ResolvedDDGISettings currentDdgiSettings;
        private DDGIResources currentDdgiResources;

        internal YutrelDeferredRenderer(YutrelDeferredRendererSettings settings)
        {
            this.settings = settings ?? new YutrelDeferredRendererSettings();
        }

        protected override YutrelRendererOutput RecordScene(
            RenderGraph renderGraph,
            in YutrelCameraRenderContext context)
        {
            var camera = context.camera;

#if UNITY_EDITOR
            if (camera.cameraType == CameraType.SceneView)
            {
                ScriptableRenderContext.EmitWorldGeometryForSceneView(camera);
            }
#endif

            currentShadowSettings = YutrelShadowSettings.Resolve(
                settings.shadowSettings,
                VolumeManager.instance.stack);
            currentDdgiSettings = YutrelDDGISettings.Resolve(
                settings.ddgiSettings,
                VolumeManager.instance.stack);

            if (!camera.TryGetCullingParameters(out var cullingParameters))
            {
                return default;
            }

            cullingParameters.shadowDistance = Mathf.Min(
                currentShadowSettings.max_distance,
                camera.farClipPlane);
            cullingParameters.conservativeEnclosingSphere =
                currentShadowSettings.conservative_enclosing_sphere;
            cullingParameters.numIterationsEnclosingSphere =
                currentShadowSettings.num_iterations_enclosing_sphere;
            var cullingResults = context.renderContext.Cull(ref cullingParameters);

            var textures = frameData.GetOrCreate<RenderTargets>();
            var lightResources = frameData.GetOrCreate<LightResources>();
            var shadowResources = frameData.GetOrCreate<ShadowResources>();
            shadowResources.Reset();

            SetupLightPass.Record(
                renderGraph,
                context.renderContext,
                camera,
                cullingResults,
                currentShadowSettings,
                ref lightResources,
                ref shadowResources);

            ShadowPass.Record(renderGraph, shadowResources, currentShadowSettings);

            SetupPass.CreateDeferredTargets(
                renderGraph,
                camera,
                ref textures,
                context.targetSize,
                context.sceneColorFormat,
                context.preExposure);

            BasePass.Record(renderGraph, camera, cullingResults, textures);

            ShadowMaskPass.Record(
                renderGraph,
                textures,
                lightResources,
                shadowResources,
                currentShadowSettings,
                context.targetSize);

            DirectionalLightPass.Record(renderGraph, textures, lightResources);

            ScreenSpaceAmbientOcclusionPass.Record(
                renderGraph,
                textures,
                settings.ambientOcclusionSettings,
                context.targetSize);

            currentDdgiResources = null;
            if (currentDdgiSettings.enabled)
            {
                currentDdgiResources = frameData.GetOrCreate<DDGIResources>();
                ddgiResourceManager.Prepare(
                    renderGraph,
                    camera,
                    currentDdgiResources,
                    currentDdgiSettings);
                DDGIProbeTracePass.Record(
                    renderGraph,
                    currentDdgiResources,
                    lightResources,
                    rayTracingWorld,
                    currentDdgiSettings);
                DDGIProbeBlendingPass.Record(renderGraph, currentDdgiResources, currentDdgiSettings);
                DDGIProbeRelocationPass.Record(renderGraph, currentDdgiResources, currentDdgiSettings);
                DDGIProbeClassificationPass.Record(renderGraph, currentDdgiResources, currentDdgiSettings);
                DDGILightingPass.Record(renderGraph, textures, currentDdgiResources, currentDdgiSettings);

#if UNITY_EDITOR
                if (context.debugSettings.ddgi_ray_data_debug_texture)
                {
                    DDGIDebugPass.Record(renderGraph, currentDdgiResources);
                }
#endif
            }
            else
            {
                frameData.GetOrCreate<DDGIResources>().Reset();
                ddgiResourceManager.Release();
                EnvironmentLightingPass.Record(renderGraph, textures, lightResources);
            }

            EndfieldForwardPass.Record(
                renderGraph,
                camera,
                cullingResults,
                textures,
                lightResources,
                currentDdgiResources,
                currentDdgiSettings);

            SkyboxPass.Record(renderGraph, camera, textures, lightResources);

#if UNITY_EDITOR
            UnsupportedShadersPass.Record(renderGraph, camera, cullingResults, textures);
            DDGIProbeDebugPass.Record(
                renderGraph,
                camera,
                textures,
                currentDdgiResources,
                currentDdgiSettings,
                context.debugSettings,
                context.targetSize);
            GizmosPass.Record(
                renderGraph,
                camera,
                textures.scene_color,
                textures.scene_depth,
                GizmoSubset.PreImageEffects);
#endif

            return new YutrelRendererOutput(textures.scene_color, textures.scene_depth);
        }

        protected override TextureHandle RecordAfterPostProcessing(
            RenderGraph renderGraph,
            in YutrelCameraRenderContext context,
            in YutrelRendererOutput output,
            TextureHandle postProcessedColor)
        {
#if UNITY_EDITOR
            var textures = frameData.GetOrCreate<RenderTargets>();
            var lightResources = frameData.GetOrCreate<LightResources>();
            var shadowResources = frameData.GetOrCreate<ShadowResources>();
            textures.scene_color = output.sceneColor;
            textures.scene_depth = output.sceneDepth;
            textures.final_color = postProcessedColor;

            if (currentDdgiSettings.enabled &&
                DDGIFullscreenTraceRadianceDebugPass.IsFullscreenTraceMode(
                    context.debugSettings.ddgi_probe_debug_mode))
            {
                DDGIFullscreenTraceRadianceDebugPass.Record(
                    renderGraph,
                    context.camera,
                    textures,
                    currentDdgiResources,
                    lightResources,
                    rayTracingWorld,
                    currentDdgiSettings,
                    context.debugSettings,
                    context.targetSize);
            }

            DebugViewPass.Record(
                renderGraph,
                context.camera,
                textures,
                lightResources,
                shadowResources,
                currentShadowSettings,
                context.debugSettings,
                context.targetSize);

            GizmosPass.Record(
                renderGraph,
                context.camera,
                textures.final_color,
                textures.scene_depth,
                GizmoSubset.PostImageEffects);

            return textures.final_color;
#else
            return postProcessedColor;
#endif
        }

        protected override void Dispose(bool disposing)
        {
            ddgiResourceManager.Dispose();
            rayTracingWorld.Dispose();
        }

        internal static void CleanupSharedResources()
        {
            DDGILightingPass.Cleanup();
            DDGIProbeTracePass.Cleanup();
            DirectionalLightPass.Cleanup();
            EnvironmentLightingPass.Cleanup();
            SkyboxPass.Cleanup();
            ScreenSpaceAmbientOcclusionPass.Cleanup();
            ShadowMaskPass.Cleanup();
#if UNITY_EDITOR
            DDGIProbeDebugPass.Cleanup();
            DDGIFullscreenTraceRadianceDebugPass.Cleanup();
            DebugViewPass.Cleanup();
            UnsupportedShadersPass.Cleanup();
#endif
            LightResources.Cleanup();
        }
    }
}
