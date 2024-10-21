using UnityEngine;
using UnityEngine.Rendering;

namespace YutrelRP.FrameData
{
    public class LightData : ContextItem
    {
        public VisibleLight sun_light;

        public override void Reset()
        {
            sun_light = default;
        }
    }
}