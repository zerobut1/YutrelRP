using UnityEngine;
using UnityEngine.Rendering.RenderGraphModule;

namespace YutrelRP
{
    internal static class RayTracingSmokeTest
    {
#if UNITY_EDITOR
        internal static bool IsEnabled(YutrelRPSettings settings, YutrelRPDebugSettings debugSettings)
#else
        internal static bool IsEnabled(YutrelRPSettings settings)
#endif
        {
            if (settings == null)
            {
                return false;
            }

#if UNITY_EDITOR
            switch (debugSettings != null ? debugSettings.debug_view_mode : YutrelRPDebugSettings.DebugViewMode.Disabled)
            {
                case YutrelRPDebugSettings.DebugViewMode.RayTracingSmokeTestRayGen:
                case YutrelRPDebugSettings.DebugViewMode.RayTracingSmokeTestRTASHitMiss:
                    return true;
            }
#endif
            return settings.rayTracingSmokeTestSettings != null &&
                   settings.rayTracingSmokeTestSettings.enabled;
        }

#if UNITY_EDITOR
        internal static void Record(RenderGraph renderGraph, Camera camera, ref RenderTargets textures,
            YutrelRPSettings settings, YutrelRPDebugSettings debugSettings, Vector2Int attachmentSize)
#else
        internal static void Record(RenderGraph renderGraph, Camera camera, ref RenderTargets textures,
            YutrelRPSettings settings, Vector2Int attachmentSize)
#endif
        {
            var smokeTestSettings = GetSettings(settings
#if UNITY_EDITOR
                , debugSettings
#endif
            );
            if (smokeTestSettings == null || !smokeTestSettings.enabled)
            {
                return;
            }

            RayTracingSmokeTestPass.Record(renderGraph, camera, ref textures, smokeTestSettings, attachmentSize);
        }

        internal static void Cleanup()
        {
            RayTracingSmokeTestPass.Cleanup();
        }

#if UNITY_EDITOR
        internal static YutrelRPSettings.RayTracingSmokeTestSettings GetSettings(YutrelRPSettings settings,
            YutrelRPDebugSettings debugSettings)
#else
        internal static YutrelRPSettings.RayTracingSmokeTestSettings GetSettings(YutrelRPSettings settings)
#endif
        {
            if (settings == null)
            {
                return null;
            }

#if UNITY_EDITOR
            switch (debugSettings != null ? debugSettings.debug_view_mode : YutrelRPDebugSettings.DebugViewMode.Disabled)
            {
                case YutrelRPDebugSettings.DebugViewMode.RayTracingSmokeTestRayGen:
                    return new YutrelRPSettings.RayTracingSmokeTestSettings
                    {
                        enabled = true,
                        mode = YutrelRPSettings.RayTracingSmokeTestMode.RayGenOnly
                    };
                case YutrelRPDebugSettings.DebugViewMode.RayTracingSmokeTestRTASHitMiss:
                    return new YutrelRPSettings.RayTracingSmokeTestSettings
                    {
                        enabled = true,
                        mode = YutrelRPSettings.RayTracingSmokeTestMode.RTASHitMiss
                    };
            }
#endif
            return settings.rayTracingSmokeTestSettings;
        }
    }
}
