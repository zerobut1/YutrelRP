using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace YutrelRP
{
    public class LightResources : ContextItem
    {
        public BufferHandle directional_light_data_buffer;

        public int brdf_lut_Id;
        public TextureHandle BRDF_LUT;

        public override void Reset()
        {
            directional_light_data_buffer = BufferHandle.nullHandle;
            brdf_lut_Id = Shader.PropertyToID("_BRDF_LUT");
            BRDF_LUT = TextureHandle.nullHandle;
        }
    };
}