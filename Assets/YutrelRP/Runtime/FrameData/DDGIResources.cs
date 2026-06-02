using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace YutrelRP
{
    public class DDGIResources : ContextItem
    {
        public static readonly int
            probe_ray_data_ID = Shader.PropertyToID("_DDGIProbeRayData"),
            probe_ray_data_dimensions_ID = Shader.PropertyToID("_DDGIProbeRayDataDimensions"),
            probe_ray_data_debug_slice_ID = Shader.PropertyToID("_DDGIProbeRayDataDebugSlice"),
            probe_ray_data_max_distance_ID = Shader.PropertyToID("_DDGIProbeRayDataMaxDistance");

        public TextureHandle probe_ray_data;
        public Vector3Int probe_count;
        public int rays_per_probe;
        public float probe_max_ray_distance;

        public override void Reset()
        {
            probe_ray_data = TextureHandle.nullHandle;
            probe_count = Vector3Int.zero;
            rays_per_probe = 0;
            probe_max_ray_distance = 0.0f;
        }
    }
}
