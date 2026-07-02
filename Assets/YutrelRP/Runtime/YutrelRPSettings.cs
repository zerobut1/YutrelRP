using UnityEngine;
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
            public bool enabled;

            public EncodingSettings encoding = new();
            public SamplingSettings sampling = new();
            public BlendingSettings blending = new();
            public RelocationSettings relocation = new();
            public ClassificationSettings classification = new();

            [System.Serializable]
            public class EncodingSettings
            {
                [Min(0.001f)] public float lightingIntensityScale = 100000.0f;
                [Min(0.01f)] public float irradianceEncodingGamma = 5.0f;
            }

            [System.Serializable]
            public class SamplingSettings
            {
                [Min(0.0f)] public float probeNormalBias = 0.2f;
                [Min(0.0f)] public float probeViewBias = 0.1f;
            }

            [System.Serializable]
            public class BlendingSettings
            {
                [Range(0.0f, 1.0f)] public float probeHysteresis = 0.97f;
                [Min(0.01f)] public float distanceExponent = 50.0f;
                [Min(0.0f)] public float irradianceThreshold = 0.2f;
                [Min(0.0f)] public float brightnessThreshold = 2.0f;
                [Range(0.0f, 1.0f)] public float probeRandomRayBackfaceThreshold = 0.1f;
            }

            [System.Serializable]
            public class RelocationSettings
            {
                public bool enabled;
                [Min(0.0f)] public float probeMinFrontfaceDistance = 0.1f;
                [Range(0.0f, 1.0f)] public float probeFixedRayBackfaceThreshold = 0.25f;
            }

            [System.Serializable]
            public class ClassificationSettings
            {
                public bool enabled;
            }
        }
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
