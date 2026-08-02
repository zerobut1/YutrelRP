using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace YutrelRP
{
    [Serializable]
    [SupportedOnRenderPipeline(typeof(YutrelRPAsset))]
    [UnityEngine.Categorization.CategoryInfo(Name = "R: DDGI", Order = 1000)]
    public sealed class YutrelDDGIShaderResources : IRenderPipelineResources
    {
        [SerializeField, HideInInspector] private int m_Version = 0;

        public int version => m_Version;

        bool IRenderPipelineGraphicsSettings.isAvailableInPlayerBuild => true;

        [SerializeField, ResourcePath("Shaders/DDGI/DDGIProbeTrace.raytrace")]
        private RayTracingShader probeTraceRayTracing;

        [SerializeField, ResourcePath("Shaders/DDGI/DDGIFullscreenTraceRadiance.raytrace")]
        private RayTracingShader fullscreenTraceRadiance;

        [SerializeField, ResourcePath("Shaders/DDGI/DDGIProbeBlending.compute")]
        private ComputeShader probeBlending;

        [SerializeField, ResourcePath("Shaders/DDGI/DDGIProbeRelocation.compute")]
        private ComputeShader probeRelocation;

        [SerializeField, ResourcePath("Shaders/DDGI/DDGIProbeClassification.compute")]
        private ComputeShader probeClassification;

        [SerializeField, ResourcePath("Shaders/DDGI/DDGIDebug.compute")]
        private ComputeShader debugShader;

        [SerializeField, ResourcePath("Shaders/DDGI/DDGIProbeDebug.shader")]
        private Shader probeDebugShader;

        [SerializeField, ResourcePath("Shaders/DDGI/DDGILightingPass.shader")]
        private Shader lightingShader;

        public ComputeShader debug => debugShader;
        public Shader probe_debug => probeDebugShader;
        public Shader lighting => lightingShader;

        public RayTracingShader probe_trace_ray_tracing => probeTraceRayTracing;
        public RayTracingShader fullscreen_trace_radiance => fullscreenTraceRadiance;
        public ComputeShader probe_blending => probeBlending;
        public ComputeShader probe_relocation => probeRelocation;
        public ComputeShader probe_classification => probeClassification;
    }
}
