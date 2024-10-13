using UnityEngine;
using UnityEngine.Rendering;

namespace YutrelRP.FrameData
{
    public class CameraData : ContextItem
    {
        public Camera camera;
        public CullingResults culling_results;

        public override void Reset()
        {
            camera = null;
            culling_results = default;
        }

        public RTClearFlags GetClearFlags()
        {
            var clear_flags = camera.clearFlags;
            if (clear_flags == CameraClearFlags.Depth)
            {
                return RTClearFlags.DepthStencil;
            }
            else if (clear_flags == CameraClearFlags.Nothing)
            {
                return RTClearFlags.None;
            }

            return RTClearFlags.All;
        }

        public Color GetClearColor()
        {
            return CoreUtils.ConvertSRGBToActiveColorSpace(camera.backgroundColor);
        }
    }
}