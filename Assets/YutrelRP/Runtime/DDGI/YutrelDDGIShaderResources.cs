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
        private RayTracingShader probeTrace;

        public RayTracingShader probe_trace => probeTrace;
    }
}
