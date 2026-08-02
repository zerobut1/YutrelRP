using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace YutrelRP
{
    public sealed class YutrelRP : RenderPipeline
    {
        public const string ShaderTagName = "YutrelPipeline";

        private readonly YutrelRPAsset asset;
        private readonly RenderGraph renderGraph = new("Yutrel Render Graph");
        private readonly VolumeProfile defaultVolumeProfile;
#if UNITY_EDITOR
        private readonly DebugDisplaySettingsUI debugDisplaySettingsUI = new();
        private readonly YutrelRPDebugSettings debugSettings = new();
        private readonly YutrelRPDebugDisplaySettings debugDisplaySettings;
#endif

        internal YutrelRP(YutrelRPAsset asset)
        {
            this.asset = asset;
            GraphicsSettings.useScriptableRenderPipelineBatching = asset.UseSRPBatcher;
            defaultVolumeProfile = CreateDefaultVolumeProfile();
            VolumeManager.instance.Initialize(defaultVolumeProfile);
#if UNITY_EDITOR
            debugDisplaySettings = new YutrelRPDebugDisplaySettings(debugSettings);
            debugDisplaySettingsUI.RegisterDebug(debugDisplaySettings);
#endif
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
#if UNITY_EDITOR
            debugDisplaySettingsUI.UnregisterDebug();
#endif
            asset.DestroyRenderers();
            YutrelDeferredRenderer.CleanupSharedResources();
            ToneMappingPass.Cleanup();
            FinalPass.Cleanup();
            YutrelRPRuntimeShaderUtility.ClearWarnings();
            VolumeManager.instance.Deinitialize();
            DestroyDefaultVolumeProfile();
            CleanupRenderGraph();
        }

        protected override void Render(ScriptableRenderContext context, List<Camera> cameras)
        {
            BeginContextRendering(context, cameras);
            try
            {
                foreach (var camera in cameras)
                {
                    BeginCameraRendering(context, camera);
                    try
                    {
                        var renderer = asset.GetRenderer(camera);
                        renderer?.Render(
                            renderGraph,
                            context,
                            camera
#if UNITY_EDITOR
                            , debugSettings
#endif
                        );
                    }
                    finally
                    {
                        EndCameraRendering(context, camera);
                    }
                }
            }
            finally
            {
                EndContextRendering(context, cameras);
                renderGraph.EndFrame();
            }
        }

        private static VolumeProfile CreateDefaultVolumeProfile()
        {
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.name = "YutrelRP Default Volume Profile";
            profile.hideFlags = HideFlags.HideAndDontSave;
            profile.Add<YutrelSceneRenderSettings>();
            profile.Add<YutrelShadowSettings>();
            return profile;
        }

        private void DestroyDefaultVolumeProfile()
        {
            if (defaultVolumeProfile == null)
            {
                return;
            }

            foreach (var component in defaultVolumeProfile.components)
            {
                CoreUtils.Destroy(component);
            }

            CoreUtils.Destroy(defaultVolumeProfile);
        }

        private void CleanupRenderGraph()
        {
#if UNITY_EDITOR
            try
            {
                renderGraph.Cleanup();
            }
            catch (InvalidOperationException exception) when (exception.Message.Contains("Render Graph is active"))
            {
                EditorApplication.delayCall += CleanupRenderGraph;
            }
#else
            renderGraph.Cleanup();
#endif
        }
    }
}
