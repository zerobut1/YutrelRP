using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace YutrelRP
{
    public class RenderTargets : ContextItem
    {
        public TextureHandle camera_output;
        public TextureHandle GBuffer_A;
        public TextureHandle GBuffer_B;
        public TextureHandle GBuffer_C;
        public TextureHandle scene_color;
        public TextureHandle scene_depth;
        public TextureHandle final_color;
        public TextureHandle shadow_mask;

        public override void Reset()
        {
            camera_output = TextureHandle.nullHandle;
            scene_color = TextureHandle.nullHandle;
            scene_depth = TextureHandle.nullHandle;
            GBuffer_A = TextureHandle.nullHandle;
            GBuffer_B = TextureHandle.nullHandle;
            GBuffer_C = TextureHandle.nullHandle;
            final_color = TextureHandle.nullHandle;
            shadow_mask = TextureHandle.nullHandle;
        }
    }
}