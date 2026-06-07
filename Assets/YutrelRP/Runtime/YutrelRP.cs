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
    public class YutrelRP : RenderPipeline
    {
        private readonly RenderGraph render_graph = new("Yutrel Render Graph");
        private readonly VolumeProfile default_volume_profile;
        private readonly YutrelRenderer renderer;
        private readonly YutrelRPSettings settings;

        public YutrelRP(YutrelRPSettings settings)
        {
            this.settings = settings;
            GraphicsSettings.useScriptableRenderPipelineBatching = settings.useSRPBatcher;
            default_volume_profile = CreateDefaultVolumeProfile();
            VolumeManager.instance.Initialize(default_volume_profile);
            renderer = new YutrelRenderer(this.settings);
        }

        protected override void Dispose(bool is_disposing)
        {
            base.Dispose(is_disposing);
            renderer.Dispose();
            VolumeManager.instance.Deinitialize();
            DestroyDefaultVolumeProfile();
            CleanupRenderGraph();
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
            if (default_volume_profile == null)
            {
                return;
            }

            foreach (var component in default_volume_profile.components)
            {
                CoreUtils.Destroy(component);
            }

            CoreUtils.Destroy(default_volume_profile);
        }

        private void CleanupRenderGraph()
        {
#if UNITY_EDITOR
            try
            {
                render_graph.Cleanup();
            }
            catch (InvalidOperationException exception) when (exception.Message.Contains("Render Graph is active"))
            {
                EditorApplication.delayCall += CleanupRenderGraph;
            }
#else
            render_graph.Cleanup();
#endif
        }

        protected override void Render(ScriptableRenderContext context, List<Camera> cameras)
        {
            foreach (var camera in cameras) renderer.Render(render_graph, context, camera);

            render_graph.EndFrame();
        }
    }
}
