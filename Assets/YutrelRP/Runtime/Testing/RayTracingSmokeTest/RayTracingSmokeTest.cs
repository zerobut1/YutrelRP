using UnityEngine;
using UnityEngine.Rendering.RenderGraphModule;

namespace YutrelRP
{
    internal static class RayTracingSmokeTest
    {
        internal static bool IsEnabled(YutrelRPSettings settings)
        {
            if (settings == null)
            {
                return false;
            }

#if UNITY_EDITOR
            switch (settings.debugViewMode)
            {
                case YutrelRPSettings.DebugViewMode.RayTracingSmokeTestRayGen:
                case YutrelRPSettings.DebugViewMode.RayTracingSmokeTestRTASHitMiss:
                    return true;
            }
#endif
            return settings.rayTracingSmokeTestSettings != null &&
                   settings.rayTracingSmokeTestSettings.enabled;
        }

        internal static void Record(RenderGraph renderGraph, Camera camera, ref RenderTargets textures,
            YutrelRPSettings settings, Vector2Int attachmentSize)
        {
            var smokeTestSettings = GetSettings(settings);
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

        internal static YutrelRPSettings.RayTracingSmokeTestSettings GetSettings(YutrelRPSettings settings)
        {
            if (settings == null)
            {
                return null;
            }

#if UNITY_EDITOR
            switch (settings.debugViewMode)
            {
                case YutrelRPSettings.DebugViewMode.RayTracingSmokeTestRayGen:
                    return new YutrelRPSettings.RayTracingSmokeTestSettings
                    {
                        enabled = true,
                        mode = YutrelRPSettings.RayTracingSmokeTestMode.RayGenOnly
                    };
                case YutrelRPSettings.DebugViewMode.RayTracingSmokeTestRTASHitMiss:
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
