using System;

namespace YutrelRP
{
    [Serializable]
    public class YutrelRPSettings
    {
        public bool useSRPBatcher = true;

        public ShadowSettings shadowSettings;

        public PostProcessSettings postProcessSettings;
    }
}
