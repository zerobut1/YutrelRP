using System;
using UnityEngine;
using UnityEngine.Rendering.UnifiedRayTracing;

namespace YutrelRP
{
    internal sealed class YutrelRayTracingContext : IDisposable
    {
        private bool initialized;
        private bool warned_initialization_failure;
        private RayTracingResources ray_tracing_resources;

        public RayTracingBackend Backend { get; private set; }
        public RayTracingContext Context { get; private set; }

        public bool EnsureInitialized()
        {
            if (initialized)
            {
                return true;
            }

            Backend = RayTracingContext.IsBackendSupported(RayTracingBackend.Hardware)
                ? RayTracingBackend.Hardware
                : RayTracingBackend.Compute;
            if (!RayTracingContext.IsBackendSupported(Backend))
            {
                WarnInitializationFailure("No UnifiedRayTracing backend is supported.");
                return false;
            }

            ray_tracing_resources = new RayTracingResources();
            if (!ray_tracing_resources.LoadFromRenderPipelineResources())
            {
                WarnInitializationFailure("Failed to load UnifiedRayTracing render pipeline resources.");
                return false;
            }

            Context = new RayTracingContext(Backend, ray_tracing_resources);
            initialized = true;
            return true;
        }

        public IRayTracingShader CreateUnifiedShader(UnityEngine.Object compute_shader,
            UnityEngine.Object ray_tracing_shader)
        {
            if (!EnsureInitialized())
            {
                return null;
            }

            var shader = Backend == RayTracingBackend.Compute ? compute_shader : ray_tracing_shader;
            return shader != null ? Context.CreateRayTracingShader(shader) : null;
        }

        public void Dispose()
        {
            Context?.Dispose();
            Context = null;
            initialized = false;
        }

        private void WarnInitializationFailure(string message)
        {
            if (warned_initialization_failure)
            {
                return;
            }

            warned_initialization_failure = true;
            Debug.LogWarning($"YutrelRP UnifiedRayTracing initialization failed: {message}");
        }
    }
}
