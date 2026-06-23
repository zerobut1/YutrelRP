using UnityEngine;
using UnityEngine.Rendering.RenderGraphModule;

namespace YutrelRP
{
    internal static class DDGIProbeTrace
    {
        internal static bool IsEnabled(YutrelRPSettings settings)
        {
            return settings?.ddgiSettings != null && settings.ddgiSettings.enabled;
        }

        internal static void Record(RenderGraph renderGraph, Camera camera, YutrelRPSettings settings,
#if UNITY_EDITOR
            YutrelRPDebugSettings debugSettings,
#endif
            LightResources lightResources, RenderTargets textures, Vector2Int attachmentSize,
            ref DDGIResources resources)
        {
            if (!IsEnabled(settings))
            {
                DDGIProbeTracePass.ReleasePersistentAtlasesForDisabled();
                resources.Reset();
                return;
            }

#if UNITY_EDITOR
            var screenTraceDebug = debugSettings != null &&
                                   debugSettings.debug_view_mode == YutrelRPDebugSettings.DebugViewMode.DDGIScreenTrace;
#else
            const bool screenTraceDebug = false;
#endif
            var sceneDepth = textures != null ? textures.scene_depth : TextureHandle.nullHandle;
            DDGIProbeTracePass.Record(renderGraph, camera, settings.ddgiSettings, lightResources, screenTraceDebug,
                sceneDepth, attachmentSize, ref resources);
        }

        internal static void Cleanup()
        {
            DDGIProbeTracePass.Cleanup();
        }
    }
}
