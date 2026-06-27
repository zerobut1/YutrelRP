using UnityEngine;
namespace YutrelRP
{
    [System.Serializable]
    public class YutrelRPSettings
    {
        public bool useSRPBatcher = true;

        public ShadowSettings shadowSettings;

        public AmbientOcclusionSettings ambientOcclusionSettings = new();
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
