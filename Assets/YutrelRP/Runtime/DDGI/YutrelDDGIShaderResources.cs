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

        [SerializeField, ResourcePath("YutrelRP/Shader/DDGI/DDGIProbeTrace.urtshader")]
        private ComputeShader probeTraceCompute;

        [SerializeField, ResourcePath("YutrelRP/Shader/DDGI/DDGIProbeTrace.urtshader")]
        private RayTracingShader probeTraceRayTracing;

        [SerializeField, ResourcePath("YutrelRP/Shader/DDGI/DDGIProbeBlending.compute")]
        private ComputeShader probeBlending;

        [SerializeField, ResourcePath("YutrelRP/Shader/DDGI/DDGIDebug.compute")]
        private ComputeShader debugShader;

        public ComputeShader debug => debugShader;

        public ComputeShader probe_trace_compute => probeTraceCompute;
        public RayTracingShader probe_trace_ray_tracing => probeTraceRayTracing;
        public ComputeShader probe_blending => probeBlending;
    }
}
