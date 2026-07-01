using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace YutrelRP
{
    public sealed class DDGIResources : ContextItem
    {
        public const GraphicsFormat ProbeIrradianceGraphicsFormat = GraphicsFormat.A2B10G10R10_UNormPack32;
        public const GraphicsFormat ProbeDistanceGraphicsFormat = GraphicsFormat.R16G16_SFloat;
        public const GraphicsFormat ProbeDataGraphicsFormat = GraphicsFormat.R16G16B16A16_SFloat;
        public const int FixedRayCount = 32;

        public YutrelDDGIVolume active_volume;
        public bool is_valid;
        public TextureHandle probe_ray_data;
        public TextureHandle probe_ray_data_debug;
        public TextureHandle probe_irradiance;
        public TextureHandle probe_distance;
        public TextureHandle probe_data;
        public Vector3Int probe_count;
        public Vector4 probe_ray_rotation_row0;
        public Vector4 probe_ray_rotation_row1;
        public Vector4 probe_ray_rotation_row2;
        public int probe_irradiance_interior_texels;
        public int probe_distance_interior_texels;

        public override void Reset()
        {
            active_volume = null;
            is_valid = false;
            probe_ray_data = TextureHandle.nullHandle;
            probe_ray_data_debug = TextureHandle.nullHandle;
            probe_irradiance = TextureHandle.nullHandle;
            probe_distance = TextureHandle.nullHandle;
            probe_data = TextureHandle.nullHandle;
            probe_count = Vector3Int.zero;
            probe_ray_rotation_row0 = new Vector4(1.0f, 0.0f, 0.0f, 0.0f);
            probe_ray_rotation_row1 = new Vector4(0.0f, 1.0f, 0.0f, 0.0f);
            probe_ray_rotation_row2 = new Vector4(0.0f, 0.0f, 1.0f, 0.0f);
            probe_irradiance_interior_texels = 0;
            probe_distance_interior_texels = 0;
        }

        public Vector4 ProbeIrradianceDimensions
        {
            get
            {
                var tile = probe_irradiance_interior_texels + 2;
                return new Vector4(probe_count.x * tile, probe_count.z * tile, probe_count.y, tile);
            }
        }

        public Vector4 ProbeDistanceDimensions
        {
            get
            {
                var tile = probe_distance_interior_texels + 2;
                return new Vector4(probe_count.x * tile, probe_count.z * tile, probe_count.y, tile);
            }
        }
    }
}
