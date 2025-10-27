using System;
using UnityEngine;

namespace YutrelRP
{
    [Serializable]
    public class YutrelRPSettings
    {
        public bool useSRPBatcher = true;

        public Texture2D BRDF_LUT;

        public ShadowSettings shadowSettings;

        public PostProcessSettings postProcessSettings;
    }
}