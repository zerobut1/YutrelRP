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
            ref DDGIResources resources)
        {
            if (!IsEnabled(settings))
            {
                DDGIProbeTracePass.ReleasePersistentAtlasesForDisabled();
                resources.Reset();
                return;
            }

            DDGIProbeTracePass.Record(renderGraph, camera, settings.ddgiSettings, ref resources);
        }

        internal static void Cleanup()
        {
            DDGIProbeTracePass.Cleanup();
        }
    }
}
