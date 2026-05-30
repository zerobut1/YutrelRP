using UnityEngine;
using UnityEngine.Rendering.RenderGraphModule;

namespace YutrelRP
{
    internal static class RayTracingSmokeTest
    {
        internal static void Record(RenderGraph renderGraph, Camera camera, ref RenderTargets textures,
            YutrelRPSettings settings, Vector2Int attachmentSize)
        {
            RayTracingSmokeTestPass.Record(renderGraph, camera, ref textures, GetSettings(settings), attachmentSize);
        }

        internal static void Cleanup()
        {
            RayTracingSmokeTestPass.Cleanup();
        }

        internal static YutrelRPSettings.RayTracingSmokeTestSettings GetSettings(YutrelRPSettings settings)
        {
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
