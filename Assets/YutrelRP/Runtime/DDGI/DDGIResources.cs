using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace YutrelRP
{
    public class DDGIResources : ContextItem
    {
        public static readonly int
            probe_ray_data_ID = Shader.PropertyToID("_DDGIProbeRayData"),
            trace_albedo_ID = Shader.PropertyToID("_DDGITraceAlbedo"),
            probe_ray_data_dimensions_ID = Shader.PropertyToID("_DDGIProbeRayDataDimensions"),
            probe_ray_data_debug_slice_ID = Shader.PropertyToID("_DDGIProbeRayDataDebugSlice"),
            probe_ray_data_max_distance_ID = Shader.PropertyToID("_DDGIProbeRayDataMaxDistance"),
            probe_irradiance_ID = Shader.PropertyToID("_DDGIProbeIrradiance"),
            probe_irradiance_dimensions_ID = Shader.PropertyToID("_DDGIProbeIrradianceDimensions"),
            probe_irradiance_debug_slice_ID = Shader.PropertyToID("_DDGIProbeIrradianceDebugSlice"),
            probe_distance_ID = Shader.PropertyToID("_DDGIProbeDistance"),
            probe_distance_dimensions_ID = Shader.PropertyToID("_DDGIProbeDistanceDimensions"),
            probe_distance_debug_slice_ID = Shader.PropertyToID("_DDGIProbeDistanceDebugSlice"),
            probe_data_ID = Shader.PropertyToID("_DDGIProbeData"),
            probe_data_dimensions_ID = Shader.PropertyToID("_DDGIProbeDataDimensions"),
            probe_data_debug_slice_ID = Shader.PropertyToID("_DDGIProbeDataDebugSlice"),
            volume_min_ws_ID = Shader.PropertyToID("_DDGIVolumeMinWS"),
            volume_max_ws_ID = Shader.PropertyToID("_DDGIVolumeMaxWS"),
            probe_spacing_ws_ID = Shader.PropertyToID("_DDGIProbeSpacingWS"),
            probe_normal_bias_ID = Shader.PropertyToID("_DDGIProbeNormalBias"),
            probe_view_bias_ID = Shader.PropertyToID("_DDGIProbeViewBias"),
            gather_valid_ID = Shader.PropertyToID("_DDGIGatherValid"),
            diffuse_intensity_ID = Shader.PropertyToID("_DDGIDiffuseIntensity");

        public TextureHandle probe_ray_data;
        public TextureHandle trace_albedo;
        public TextureHandle probe_irradiance;
        public TextureHandle probe_distance;
        public TextureHandle probe_data;
        public Vector3Int probe_count;
        public int rays_per_probe;
        public int probe_irradiance_interior_texels;
        public int probe_distance_interior_texels;
        public float probe_max_ray_distance;
        public Vector3 volume_min_ws;
        public Vector3 volume_max_ws;
        public Vector3 probe_spacing_ws;
        public float probe_normal_bias;
        public float probe_view_bias;
        public float probe_distance_exponent;
        public bool has_gather_data;
        public bool has_persistent_atlas;
        public string diagnostic;

        // DDGI atlas 布局约定：
        // ProbeRayData: width=raysPerProbe, height=probeCountX*probeCountZ, slice=probeY, planeIndex=probeX+probeZ*probeCountX。
        // ProbeIrradiance/ProbeDistance: 每个 probe 为带 1 texel border 的 octahedral tile，
        // width=probeCountX*(interiorTexels+2), height=probeCountZ*(interiorTexels+2), slice=probeY。
        // ProbeData: width=probeCountX, height=probeCountZ, slice=probeY；rgba=offset.xyz/state，初始 offset=0,state=1(active)。
        public override void Reset()
        {
            probe_ray_data = TextureHandle.nullHandle;
            trace_albedo = TextureHandle.nullHandle;
            probe_irradiance = TextureHandle.nullHandle;
            probe_distance = TextureHandle.nullHandle;
            probe_data = TextureHandle.nullHandle;
            probe_count = Vector3Int.zero;
            rays_per_probe = 0;
            probe_irradiance_interior_texels = 0;
            probe_distance_interior_texels = 0;
            probe_max_ray_distance = 0.0f;
            volume_min_ws = Vector3.zero;
            volume_max_ws = Vector3.zero;
            probe_spacing_ws = Vector3.zero;
            probe_normal_bias = 0.0f;
            probe_view_bias = 0.0f;
            probe_distance_exponent = 0.0f;
            has_gather_data = false;
            has_persistent_atlas = false;
            diagnostic = null;
        }

        internal void SetVolumeMetadata(YutrelDDGIVolume volume)
        {
            probe_count = volume.ProbeCount;
            rays_per_probe = volume.RaysPerProbe;
            probe_irradiance_interior_texels = volume.ProbeIrradianceInteriorTexels;
            probe_distance_interior_texels = volume.ProbeDistanceInteriorTexels;
            probe_max_ray_distance = volume.ProbeMaxRayDistance;
            var bounds = volume.WorldBounds;
            volume_min_ws = bounds.min;
            volume_max_ws = bounds.max;
            probe_spacing_ws = volume.GetWorldProbeSpacing();
            probe_normal_bias = volume.ProbeNormalBias;
            probe_view_bias = volume.ProbeViewBias;
            probe_distance_exponent = volume.DistanceExponent;
        }

        internal Vector4 ProbeRayDataDimensions => new(
            rays_per_probe,
            probe_count.x * probe_count.z,
            probe_count.y,
            0.0f);

        internal Vector4 ProbeIrradianceDimensions
        {
            get
            {
                var tile = probe_irradiance_interior_texels + 2;
                return new Vector4(probe_count.x * tile, probe_count.z * tile, probe_count.y, tile);
            }
        }

        internal Vector4 ProbeDistanceDimensions
        {
            get
            {
                var tile = probe_distance_interior_texels + 2;
                return new Vector4(probe_count.x * tile, probe_count.z * tile, probe_count.y, tile);
            }
        }

        internal Vector4 ProbeDataDimensions => new(probe_count.x, probe_count.z, probe_count.y, 1.0f);

        internal readonly struct Identity
        {
            private const int AtlasSemanticVersion = 8;

            public readonly int volumeKey;
            public readonly Vector3Int probeCount;
            public readonly int irradianceInteriorTexels;
            public readonly int distanceInteriorTexels;
            public readonly int semanticVersion;

            public Identity(YutrelDDGIVolume volume)
            {
                volumeKey = volume != null ? volume.GetEntityId().GetHashCode() : 0;
                probeCount = volume != null ? volume.ProbeCount : Vector3Int.zero;
                irradianceInteriorTexels = volume != null ? volume.ProbeIrradianceInteriorTexels : 0;
                distanceInteriorTexels = volume != null ? volume.ProbeDistanceInteriorTexels : 0;
                semanticVersion = AtlasSemanticVersion;
            }

            public override bool Equals(object obj)
            {
                return obj is Identity other &&
                       volumeKey == other.volumeKey &&
                       probeCount == other.probeCount &&
                       irradianceInteriorTexels == other.irradianceInteriorTexels &&
                       distanceInteriorTexels == other.distanceInteriorTexels &&
                       semanticVersion == other.semanticVersion;
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    var hash = volumeKey;
                    hash = (hash * 397) ^ probeCount.GetHashCode();
                    hash = (hash * 397) ^ irradianceInteriorTexels;
                    hash = (hash * 397) ^ distanceInteriorTexels;
                    hash = (hash * 397) ^ semanticVersion;
                    return hash;
                }
            }

            public override string ToString()
            {
                return $"volume={volumeKey}, probes={probeCount}, irradiance={irradianceInteriorTexels}, distance={distanceInteriorTexels}, semantic={semanticVersion}";
            }
        }
    }
}
