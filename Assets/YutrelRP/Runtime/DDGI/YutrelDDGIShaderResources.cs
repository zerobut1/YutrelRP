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

        [SerializeField, ResourcePath("YutrelRP/Shader/DDGI/DDGIProbeTrace.raytrace")]
        private RayTracingShader probeTraceRayTracing;

        [SerializeField, ResourcePath("YutrelRP/Shader/DDGI/DDGIProbeTraceFallback.shader")]
        private Shader probeTraceFallbackShader;

        [SerializeField, ResourcePath("YutrelRP/Shader/DDGI/DDGIProbeBlending.compute")]
        private ComputeShader probeBlending;

        [SerializeField, ResourcePath("YutrelRP/Shader/DDGI/DDGIDebug.compute")]
        private ComputeShader debugShader;

        [SerializeField, ResourcePath("YutrelRP/Shader/DDGI/DDGIProbeDebug.shader")]
        private Shader probeDebugShader;

        public ComputeShader debug => debugShader;
        public Shader probe_debug => probeDebugShader;

        public RayTracingShader probe_trace_ray_tracing => probeTraceRayTracing;
        public Shader probe_trace_fallback_shader => probeTraceFallbackShader;
        public ComputeShader probe_blending => probeBlending;
    }
}
