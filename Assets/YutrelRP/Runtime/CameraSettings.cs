using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace YutrelRP
{
    [Serializable]
    public class CameraSettings
    {
        public enum RenderScaleMode
        {
            Inherit,
            Multiply,
            Override
        }

        public bool copy_color = true, copy_depth = true;

        public RenderingLayerMask new_rendering_layer_mask = -1;

        public bool mask_lights;

        public RenderScaleMode render_scale_mode = RenderScaleMode.Inherit;

        // [Range(YutrelRenderer.render_scale_min, YutrelRenderer.render_scale_max)]
        public float render_scale = 1f;

        public bool override_postFX;

        // public PostFXSettings postFXSettings = default;

        public bool allow_FXAA;

        public bool keep_Alpha;

        public FinalBlendMode final_blend_mode = new()
        {
            source = BlendMode.One,
            destination = BlendMode.Zero
        };

        public float GetRenderScale(float scale)
        {
            return render_scale_mode == RenderScaleMode.Inherit ? scale :
                render_scale_mode == RenderScaleMode.Override ? render_scale :
                scale * render_scale;
        }

        [Serializable]
        public struct FinalBlendMode
        {
            public BlendMode source, destination;
        }
    }
}