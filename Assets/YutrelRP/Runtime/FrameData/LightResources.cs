using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace YutrelRP
{
    public class LightResources : ContextItem
    {
        public BufferHandle directional_light_data_buffer;

        public override void Reset()
        {
            directional_light_data_buffer = BufferHandle.nullHandle;
        }
    };
}