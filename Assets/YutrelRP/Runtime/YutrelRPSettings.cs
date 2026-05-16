using System;

namespace YutrelRP
{
    [Serializable]
    public class YutrelRPSettings
    {
        public bool useSRPBatcher = true;

        public ShadowSettings shadowSettings;

        public PostProcessSettings postProcessSettings;

#if UNITY_EDITOR
        public DebugViewMode debugViewMode = DebugViewMode.Disabled;

        public enum DebugViewMode
        {
            Disabled = 0,
            GBufferBaseColor = 1,
            GBufferRoughness = 2,
            GBufferMetallic = 3,
            GBufferSpecular = 4,
            GBufferWorldSpaceNormal = 5,
            SceneDepth = 6,
            ShadowOnly = 7,
            CSMCascadeLevels = 8,
        }
#endif
    }
}