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

        [SerializeField, ResourcePath("YutrelRP/Shader/DDGI/DDGIProbeBlending.compute")]
        private ComputeShader probeBlending;

        [SerializeField, ResourcePath("YutrelRP/Shader/DDGI/DDGIProbeRelocation.compute")]
        private ComputeShader probeRelocation;

        [SerializeField, ResourcePath("YutrelRP/Shader/DDGI/DDGIDebug.compute")]
        private ComputeShader debugShader;

        [SerializeField, ResourcePath("YutrelRP/Shader/DDGI/DDGIProbeDebug.shader")]
        private Shader probeDebugShader;

        [SerializeField, ResourcePath("YutrelRP/Shader/DDGI/DDGILightingPass.shader")]
        private Shader lightingShader;

        public ComputeShader debug => debugShader;
        public Shader probe_debug => probeDebugShader;
        public Shader lighting => lightingShader;

        public RayTracingShader probe_trace_ray_tracing => probeTraceRayTracing;
        public ComputeShader probe_blending => probeBlending;
        public ComputeShader probe_relocation => probeRelocation;
    }
}
