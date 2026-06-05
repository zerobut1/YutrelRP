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
            screen_trace_debug_ID = Shader.PropertyToID("_DDGIScreenTraceDebug"),
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
            probe_relocation_enabled_ID = Shader.PropertyToID("_DDGIProbeRelocationEnabled"),
            probe_fixed_ray_backface_threshold_ID = Shader.PropertyToID("_DDGIProbeFixedRayBackfaceThreshold"),
            probe_random_ray_backface_threshold_ID = Shader.PropertyToID("_DDGIProbeRandomRayBackfaceThreshold"),
            probe_min_frontface_distance_ID = Shader.PropertyToID("_DDGIProbeMinFrontfaceDistance"),
            probe_max_relocation_offset_ID = Shader.PropertyToID("_DDGIProbeMaxRelocationOffset"),
            volume_min_ws_ID = Shader.PropertyToID("_DDGIVolumeMinWS"),
            volume_max_ws_ID = Shader.PropertyToID("_DDGIVolumeMaxWS"),
            probe_spacing_ws_ID = Shader.PropertyToID("_DDGIProbeSpacingWS"),
            probe_normal_bias_ID = Shader.PropertyToID("_DDGIProbeNormalBias"),
            probe_view_bias_ID = Shader.PropertyToID("_DDGIProbeViewBias"),
            probe_irradiance_encoding_gamma_ID = Shader.PropertyToID("_DDGIProbeIrradianceEncodingGamma"),
            gather_valid_ID = Shader.PropertyToID("_DDGIGatherValid"),
            diffuse_intensity_ID = Shader.PropertyToID("_DDGIDiffuseIntensity");

        public TextureHandle probe_ray_data;
        public TextureHandle trace_albedo;
        public TextureHandle screen_trace_debug;
        public TextureHandle probe_irradiance;
        public TextureHandle probe_distance;
        public TextureHandle probe_data;
        public Texture probe_irradiance_texture;
        public Texture probe_distance_texture;
        public Texture probe_data_texture;
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
        public float probe_irradiance_encoding_gamma;
        public float probe_distance_exponent;
        public bool has_gather_data;
        public bool has_persistent_atlas;
        public bool probe_relocation_enabled;
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
            screen_trace_debug = TextureHandle.nullHandle;
            probe_irradiance = TextureHandle.nullHandle;
            probe_distance = TextureHandle.nullHandle;
            probe_data = TextureHandle.nullHandle;
            probe_irradiance_texture = null;
            probe_distance_texture = null;
            probe_data_texture = null;
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
            probe_irradiance_encoding_gamma = 0.0f;
            probe_distance_exponent = 0.0f;
            has_gather_data = false;
            has_persistent_atlas = false;
            probe_relocation_enabled = false;
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
            probe_irradiance_encoding_gamma = volume.IrradianceEncodingGamma;
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
            private const int AtlasSemanticVersion = 11;

            public readonly int volumeKey;
            public readonly Vector3Int probeCount;
            public readonly Vector3 probeSpacingWS;
            public readonly int irradianceInteriorTexels;
            public readonly int distanceInteriorTexels;
            public readonly float irradianceEncodingGamma;
            public readonly int semanticVersion;

            public Identity(YutrelDDGIVolume volume)
            {
                volumeKey = volume != null ? volume.GetEntityId().GetHashCode() : 0;
                probeCount = volume != null ? volume.ProbeCount : Vector3Int.zero;
                probeSpacingWS = volume != null ? volume.GetWorldProbeSpacing() : Vector3.zero;
                irradianceInteriorTexels = volume != null ? volume.ProbeIrradianceInteriorTexels : 0;
                distanceInteriorTexels = volume != null ? volume.ProbeDistanceInteriorTexels : 0;
                irradianceEncodingGamma = volume != null ? volume.IrradianceEncodingGamma : 0.0f;
                semanticVersion = AtlasSemanticVersion;
            }

            public override bool Equals(object obj)
            {
                return obj is Identity other &&
                       volumeKey == other.volumeKey &&
                       probeCount == other.probeCount &&
                       probeSpacingWS == other.probeSpacingWS &&
                       irradianceInteriorTexels == other.irradianceInteriorTexels &&
                       distanceInteriorTexels == other.distanceInteriorTexels &&
                       irradianceEncodingGamma.Equals(other.irradianceEncodingGamma) &&
                       semanticVersion == other.semanticVersion;
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    var hash = volumeKey;
                    hash = (hash * 397) ^ probeCount.GetHashCode();
                    hash = (hash * 397) ^ probeSpacingWS.GetHashCode();
                    hash = (hash * 397) ^ irradianceInteriorTexels;
                    hash = (hash * 397) ^ distanceInteriorTexels;
                    hash = (hash * 397) ^ irradianceEncodingGamma.GetHashCode();
                    hash = (hash * 397) ^ semanticVersion;
                    return hash;
                }
            }

            public override string ToString()
            {
                return $"volume={volumeKey}, probes={probeCount}, spacing={probeSpacingWS}, irradiance={irradianceInteriorTexels}, distance={distanceInteriorTexels}, irradianceGamma={irradianceEncodingGamma:0.###}, semantic={semanticVersion}";
            }
        }
    }
}
