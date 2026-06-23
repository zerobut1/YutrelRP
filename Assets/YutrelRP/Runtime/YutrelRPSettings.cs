using UnityEngine;
using UnityEngine.Rendering;

namespace YutrelRP
{
    [System.Serializable]
    public class YutrelRPSettings
    {
        public bool useSRPBatcher = true;

        public ShadowSettings shadowSettings;

        public AmbientOcclusionSettings ambientOcclusionSettings = new();

        public DDGISettings ddgiSettings = new();

        [System.Serializable]
        public class DDGISettings
        {
            public bool enabled = false;
            public bool logDiagnostics = true;
            public RayTracingShader probeTraceShader;
            public ComputeShader probeRelocationShader;
            public ComputeShader probeBlendShader;
            public ComputeShader textureDumpCopyShader;
            public bool probeRelocationEnabled = false;
            internal static bool ProbeRelocationInScope => false;
            internal bool ProbeRelocationEffectiveEnabled => ProbeRelocationInScope && probeRelocationEnabled;
            public bool probeRandomRotationEnabled = true;
            [Range(0.0f, 1.0f)] public float probeFixedRayBackfaceThreshold = 0.25f;
            [Range(0.0f, 1.0f)] public float probeRandomRayBackfaceThreshold = 0.1f;
            [Min(0.0f)] public float probeMinFrontfaceDistance = 1.0f;
            [Range(0.0f, 0.49f)] public float probeMaxRelocationOffset = 0.45f;
            public bool traceDirectionalVisibility = false;
            public bool traceDirectionalLambert = true;
            [Min(0)] public int debugProbeRayDataSlice = 0;
            [Min(0)] public int debugProbeIrradianceAtlasSlice = 0;
            [Min(0)] public int debugProbeDistanceAtlasSlice = 0;
            [Min(0)] public int debugProbeDataSlice = 0;
            [Min(0.0f)] public float diffuseIntensity = 1.0f;
        }

        public RayTracingSmokeTestSettings rayTracingSmokeTestSettings = new();

        [System.Serializable]
        public class RayTracingSmokeTestSettings
        {
            public bool enabled = false;
            public RayTracingSmokeTestMode mode = RayTracingSmokeTestMode.RayGenOnly;
            public RayTracingShader rayGenShader;
            public RayTracingShader rtasShader;
        }

        public enum RayTracingSmokeTestMode
        {
            RayGenOnly = 0,
            RTASHitMiss = 1
        }

#if UNITY_EDITOR
        public DebugViewMode debugViewMode = DebugViewMode.Disabled;

        public enum DebugViewMode
        {
            [InspectorName("None/Disabled")]
            Disabled = 0,
            [InspectorName("GBuffer/Base Color")]
            GBufferBaseColor = 1,
            [InspectorName("GBuffer/Roughness")]
            GBufferRoughness = 2,
            [InspectorName("GBuffer/Metallic")]
            GBufferMetallic = 3,
            [InspectorName("GBuffer/Specular")]
            GBufferSpecular = 4,
            [InspectorName("GBuffer/World Space Normal")]
            GBufferWorldSpaceNormal = 5,
            [InspectorName("Scene & Lighting/Scene Depth")]
            SceneDepth = 6,
            [InspectorName("Scene & Lighting/Shadow Only")]
            ShadowOnly = 7,
            [InspectorName("Scene & Lighting/CSM Cascade Levels")]
            CSMCascadeLevels = 8,
            [InspectorName("Scene & Lighting/Ambient Occlusion")]
            AmbientOcclusion = 9,
            [InspectorName("Ray Tracing/Ray Gen Smoke Test")]
            RayTracingSmokeTestRayGen = 10,
            [InspectorName("Ray Tracing/RTAS Hit Miss Smoke Test")]
            RayTracingSmokeTestRTASHitMiss = 11,
            [InspectorName("DDGI Texture/Probe Ray Data")]
            DDGIProbeRayData = 12,
            [InspectorName("DDGI Texture/Probe Irradiance Atlas")]
            DDGIProbeIrradianceAtlas = 13,
            [InspectorName("DDGI Texture/Probe Distance Atlas")]
            DDGIProbeDistanceAtlas = 14,
            [InspectorName("DDGI Texture/Probe Data")]
            DDGIProbeData = 15,
            [InspectorName("DDGI Surface/Diffuse Only")]
            DDGIDiffuseOnly = 16,
            [InspectorName("DDGI Surface/Coverage")]
            DDGICoverage = 17,
            [InspectorName("DDGI Surface/Visibility Coverage")]
            DDGIVisibilityCoverage = 18,
            [InspectorName("DDGI Probe Scene/Probe Irradiance")]
            DDGIProbeIrradianceScene = 19,
            [InspectorName("DDGI Probe Scene/Probe Ray Data Quality")]
            DDGIProbeRayDataQualityScene = 20,
            [InspectorName("DDGI Probe Scene/Probe Distance")]
            DDGIProbeDistanceScene = 21,
            [InspectorName("DDGI Texture/Trace Albedo")]
            DDGITraceAlbedo = 22,
            [InspectorName("DDGI Texture/Screen Trace")]
            DDGIScreenTrace = 23,
        }
#endif
    }

    [System.Serializable]
    public class AmbientOcclusionSettings
    {
        public Mode mode = Mode.Disabled;

        public SSAOSettings ssao = new();

        public HBAOSettings hbao = new();

        public GTAOSettings gtao = new();

        public enum Mode
        {
            Disabled = 0,
            SSAO = 1,
            HBAO = 2,
            GTAO = 3,
        }

        [System.Serializable]
        public class SSAOSettings
        {
            [Min(0.001f)] public float radius = 0.5f;
            [Min(0.0f)] public float intensity = 1.0f;
            [Range(0.0f, 0.1f)] public float bias = 0.025f;
            [Range(1, 64)] public int sampleCount = 16;
            [Range(0, 4)] public int denoiseRadius = 1;
        }

        [System.Serializable]
        public class HBAOSettings
        {
            [Min(0.001f)] public float radius = 1.0f;
            [Min(0.0f)] public float intensity = 1.0f;
            [Range(0.0f, 0.3f)] public float bias = 0.1f;
            [Range(1, 16)] public int directionCount = 8;
            [Range(1, 16)] public int stepCount = 4;
            [Min(0.001f)] public float thickness = 1.0f;
            [Range(0, 4)] public int denoiseRadius = 1;
        }

        [System.Serializable]
        public class GTAOSettings
        {
            [Min(0.001f)] public float radius = 1.0f;
            [Min(0.0f)] public float intensity = 1.0f;
            [Range(1, 16)] public int sliceCount = 6;
            [Range(1, 16)] public int samplesPerSlice = 3;
            [Min(0.001f)] public float thickness = 1.0f;
            [Range(0, 4)] public int denoiseRadius = 1;
        }
    }
}
