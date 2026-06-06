#ifndef YUTREL_DDGI_INCLUDED
#define YUTREL_DDGI_INCLUDED

static const float YUTREL_DDGI_GOLDEN_ANGLE                   = 2.39996322972865332f;
static const float YUTREL_DDGI_PROBE_RAY_MISS_SENTINEL_OFFSET = 1.0f;
static const float YUTREL_DDGI_TWO_PI                         = 6.28318530717958648f;
static const float YUTREL_DDGI_MIN_IRRADIANCE_ENCODING_GAMMA  = 0.01f;
static const float YUTREL_DDGI_TEXTURE_STORE_VALUE_MAX        = 65504.0f;
static const float YUTREL_DDGI_LINEAR_ENERGY_VALUE_MAX        = 1.0e30f;
static const float YUTREL_DDGI_GATHER_MIN_VISIBILITY          = 0.05f;
static const float YUTREL_DDGI_GATHER_CRUSH_THRESHOLD         = 0.2f;
static const float YUTREL_DDGI_GATHER_MIN_WEIGHT              = 0.000001f;
static const float YUTREL_DDGI_GATHER_MIN_TRILINEAR_WEIGHT    = 0.001f;
static const uint YUTREL_DDGI_FIXED_RAY_COUNT                 = 32u;

float _DDGIProbeIrradianceEncodingGamma;
float4 _DDGIProbeRayRotationRow0;
float4 _DDGIProbeRayRotationRow1;
float4 _DDGIProbeRayRotationRow2;
float _DDGIProbeRandomRotationEnabled;

struct DDGIAtlasTexel
{
    uint2 base_texel;
    uint2 interior_base_texel;
    uint2 local_texel;
    uint tile_texel_size;
    uint interior_texels;
    uint slice;
    bool is_border;
};

uint DDGIProbePlaneIndex(uint3 probe_coord, uint3 probe_count)
{
    return probe_coord.x + probe_coord.z * max(probe_count.x, 1u);
}

uint3 DDGIProbeCoordFromPlaneIndex(uint plane_probe_index, uint probe_y, uint3 probe_count)
{
    uint probe_count_x = max(probe_count.x, 1u);
    return uint3(plane_probe_index % probe_count_x, probe_y, plane_probe_index / probe_count_x);
}

float3 DDGIProbeWorldPosition(float3 volume_min_ws, float3 probe_spacing_ws, uint3 probe_coord)
{
    return volume_min_ws + float3(probe_coord) * probe_spacing_ws;
}

uint3 DDGIProbeDataTexel(uint3 probe_coord)
{
    return uint3(probe_coord.x, probe_coord.z, probe_coord.y);
}

float3 DDGIProbeDataOffset(float4 probe_data)
{
    return all(probe_data == probe_data) && all(abs(probe_data) < 1.0e20f.xxxx) ? probe_data.xyz : 0.0f.xxx;
}

uint3 DDGIProbeRayDataTexel(uint ray_index, uint3 probe_coord, uint3 probe_count)
{
    return uint3(ray_index, DDGIProbePlaneIndex(probe_coord, probe_count), probe_coord.y);
}

uint DDGIAtlasTileTexelSize(uint interior_texels)
{
    return max(interior_texels, 1u) + 2u;
}

uint2 DDGIProbeAtlasTileBaseTexel(uint3 probe_coord, uint interior_texels)
{
    uint tile_texel_size = DDGIAtlasTileTexelSize(interior_texels);
    return uint2(probe_coord.x, probe_coord.z) * tile_texel_size;
}

uint2 DDGIProbeAtlasInteriorBaseTexel(uint3 probe_coord, uint interior_texels)
{
    return DDGIProbeAtlasTileBaseTexel(probe_coord, interior_texels) + 1u;
}

bool DDGIAtlasLocalTexelIsBorder(uint2 local_texel, uint interior_texels)
{
    uint tile_texel_size = DDGIAtlasTileTexelSize(interior_texels);
    return local_texel.x == 0u || local_texel.y == 0u || local_texel.x >= tile_texel_size - 1u ||
           local_texel.y >= tile_texel_size - 1u;
}

uint2 DDGIAtlasWrappedInteriorTexel(uint2 local_texel, uint interior_texels)
{
    uint interior_texel_count = max(interior_texels, 1u);
    uint tile_texel_size      = DDGIAtlasTileTexelSize(interior_texel_count);
    if (!DDGIAtlasLocalTexelIsBorder(local_texel, interior_texel_count))
    {
        return min(local_texel - 1u, uint2(interior_texel_count - 1u, interior_texel_count - 1u));
    }

    bool corner_texel = (local_texel.x == 0u || local_texel.x >= tile_texel_size - 1u) &&
                        (local_texel.y == 0u || local_texel.y >= tile_texel_size - 1u);
    bool row_texel    = local_texel.x > 0u && local_texel.x < tile_texel_size - 1u;
    uint2 copy_local_texel;

    if (corner_texel)
    {
        copy_local_texel = uint2(local_texel.x > 0u ? 1u : interior_texel_count,
                                 local_texel.y > 0u ? 1u : interior_texel_count);
    }
    else if (row_texel)
    {
        copy_local_texel = uint2((tile_texel_size - 1u) - local_texel.x,
                                 local_texel.y + (local_texel.y > 0u ? 0xffffffffu : 1u));
    }
    else
    {
        copy_local_texel = uint2(local_texel.x + (local_texel.x > 0u ? 0xffffffffu : 1u),
                                 (tile_texel_size - 1u) - local_texel.y);
    }

    return min(copy_local_texel - 1u, uint2(interior_texel_count - 1u, interior_texel_count - 1u));
}

float2 DDGIAtlasInteriorUV(uint2 local_texel, uint interior_texels)
{
    uint interior_texel_count = max(interior_texels, 1u);
    uint2 interior_texel      = DDGIAtlasWrappedInteriorTexel(local_texel, interior_texel_count);
    return (float2(interior_texel) + 0.5f) / float(interior_texel_count);
}

DDGIAtlasTexel DDGIProbeAtlasTexel(uint3 probe_coord, uint2 local_texel, uint interior_texels)
{
    DDGIAtlasTexel texel;
    texel.base_texel          = DDGIProbeAtlasTileBaseTexel(probe_coord, interior_texels);
    texel.interior_base_texel = texel.base_texel + 1u;
    texel.local_texel         = local_texel;
    texel.tile_texel_size     = DDGIAtlasTileTexelSize(interior_texels);
    texel.interior_texels     = max(interior_texels, 1u);
    texel.slice               = probe_coord.y;
    texel.is_border           = DDGIAtlasLocalTexelIsBorder(local_texel, interior_texels);
    return texel;
}

float3 DDGISafeNormalize(float3 value, float3 fallback)
{
    float length_sq = dot(value, value);
    return length_sq > 1.0e-10f ? value * rsqrt(length_sq) : fallback;
}

bool DDGIIsFinite(float value)
{
    return value == value && abs(value) < 1.0e20f;
}

bool DDGIIsFinite2(float2 value)
{
    return all(value == value) && all(abs(value) < 1.0e20f.xx);
}

bool DDGIIsFinite3(float3 value)
{
    return all(value == value) && all(abs(value) < 1.0e20f.xxx);
}

bool DDGIIsFinite4(float4 value)
{
    return all(value == value) && all(abs(value) < 1.0e20f.xxxx);
}

float DDGIProbeIrradianceEncodingGamma()
{
    return max(_DDGIProbeIrradianceEncodingGamma, YUTREL_DDGI_MIN_IRRADIANCE_ENCODING_GAMMA);
}

float3 DDGISanitizeLinearEnergy(float3 value)
{
    return all(value == value) ? min(max(value, 0.0f.xxx), YUTREL_DDGI_LINEAR_ENERGY_VALUE_MAX.xxx) : 0.0f.xxx;
}

float3 DDGIClampTextureStoreValue(float3 value)
{
    return min(DDGISanitizeLinearEnergy(value), YUTREL_DDGI_TEXTURE_STORE_VALUE_MAX.xxx);
}

float3 DDGIEncodeProbeIrradiance(float3 linear_irradiance)
{
    float inverse_gamma = rcp(DDGIProbeIrradianceEncodingGamma());
    float3 encoded      = pow(DDGISanitizeLinearEnergy(linear_irradiance), inverse_gamma.xxx);
    return DDGIClampTextureStoreValue(encoded);
}

float3 DDGIDecodeProbeIrradiance(float3 encoded_irradiance)
{
    float gamma        = DDGIProbeIrradianceEncodingGamma();
    float3 irradiance  = DDGIClampTextureStoreValue(encoded_irradiance);
    float3 sqrt_energy = pow(irradiance, (gamma * 0.5f).xxx);
    return DDGISanitizeLinearEnergy(sqrt_energy * sqrt_energy * YUTREL_DDGI_TWO_PI);
}

float DDGIProbeRayDataEncodeMiss(float max_distance)
{
    return -(max(max_distance, 0.001f) + YUTREL_DDGI_PROBE_RAY_MISS_SENTINEL_OFFSET);
}

float DDGIProbeRayDataEncodeTracePending(float max_distance)
{
    return -(max(max_distance, 0.001f) + YUTREL_DDGI_PROBE_RAY_MISS_SENTINEL_OFFSET + 2.0f);
}

float DDGIProbeRayDataEncodeFrontface(float distance)
{
    return max(distance, 0.0f);
}

float DDGIProbeRayDataEncodeBackface(float distance)
{
    return -max(distance, 0.0f);
}

float DDGIProbeRayDataMissThreshold(float max_distance)
{
    return -(max(max_distance, 0.001f) + YUTREL_DDGI_PROBE_RAY_MISS_SENTINEL_OFFSET * 0.5f);
}

bool DDGIProbeRayDataIsMiss(float signed_distance, float max_distance)
{
    return signed_distance <= DDGIProbeRayDataMissThreshold(max_distance);
}

bool DDGIProbeRayDataIsBackface(float signed_distance, float max_distance)
{
    return signed_distance < 0.0f && !DDGIProbeRayDataIsMiss(signed_distance, max_distance);
}

bool DDGIProbeRayDataIsFrontface(float signed_distance)
{
    return signed_distance > 0.0f;
}

float DDGIProbeRayDataDistance(float signed_distance, float max_distance)
{
    return DDGIProbeRayDataIsMiss(signed_distance, max_distance) ? max(max_distance, 0.001f) : abs(signed_distance);
}

float2 DDGIOctahedralWrap(float2 value)
{
    float2 sign_value = float2(value.x >= 0.0f ? 1.0f : -1.0f, value.y >= 0.0f ? 1.0f : -1.0f);
    return (1.0f.xx - abs(value.yx)) * sign_value;
}

float2 DDGIOctahedralEncode(float3 direction)
{
    float3 normal = DDGISafeNormalize(direction, float3(0.0f, 1.0f, 0.0f));
    normal /= max(abs(normal.x) + abs(normal.y) + abs(normal.z), 1.0e-6f);
    float2 encoded = normal.z >= 0.0f ? normal.xy : DDGIOctahedralWrap(normal.xy);
    return encoded * 0.5f + 0.5f;
}

float3 DDGIOctahedralDecode(float2 encoded)
{
    float2 value  = encoded * 2.0f - 1.0f;
    float3 normal = float3(value.x, value.y, 1.0f - abs(value.x) - abs(value.y));
    float fold    = saturate(-normal.z);
    normal.x += normal.x >= 0.0f ? -fold : fold;
    normal.y += normal.y >= 0.0f ? -fold : fold;
    return DDGISafeNormalize(normal, float3(0.0f, 1.0f, 0.0f));
}

float3 DDGIBuildProbeRayDirection(uint ray_index, uint ray_count)
{
    float normalized_index = (float(ray_index) + 0.5f) / max(float(ray_count), 1.0f);
    float y                = 1.0f - 2.0f * normalized_index;
    float radius           = sqrt(saturate(1.0f - y * y));
    float phi              = float(ray_index) * YUTREL_DDGI_GOLDEN_ANGLE;
    return DDGISafeNormalize(float3(cos(phi) * radius, y, sin(phi) * radius), float3(0.0f, 1.0f, 0.0f));
}

uint DDGIFixedRayCount()
{
    return YUTREL_DDGI_FIXED_RAY_COUNT;
}

bool DDGIUsesFixedProbeRays(bool fixed_rays_enabled, uint ray_count)
{
    return fixed_rays_enabled && ray_count > 0u;
}

uint DDGIProbeBlendRayStart(bool skip_fixed_rays, uint ray_count)
{
    return skip_fixed_rays && ray_count > DDGIFixedRayCount() ? DDGIFixedRayCount() : 0u;
}

uint DDGIProbeBlendRayCount(bool skip_fixed_rays, uint ray_count)
{
    uint ray_start = DDGIProbeBlendRayStart(skip_fixed_rays, ray_count);
    return ray_count > ray_start ? ray_count - ray_start : ray_count;
}

float3 DDGIRotateProbeRayDirection(float3 direction)
{
    float3 rotated = float3(dot(_DDGIProbeRayRotationRow0.xyz, direction),
                            dot(_DDGIProbeRayRotationRow1.xyz, direction),
                            dot(_DDGIProbeRayRotationRow2.xyz, direction));
    return DDGISafeNormalize(rotated, direction);
}

float3 DDGIProbeRayDirection(uint ray_index, uint ray_count, bool fixed_rays_enabled)
{
    bool uses_fixed_rays = DDGIUsesFixedProbeRays(fixed_rays_enabled, ray_count);
    uint fixed_ray_count = min(DDGIFixedRayCount(), max(ray_count, 1u));
    bool is_fixed_ray    = uses_fixed_rays && ray_index < fixed_ray_count;
    uint sample_index    = ray_index;
    uint sample_count    = max(ray_count, 1u);

    if (uses_fixed_rays)
    {
        sample_index = is_fixed_ray ? ray_index : ray_index - fixed_ray_count;
        sample_count = is_fixed_ray ? fixed_ray_count : max(ray_count - fixed_ray_count, 1u);
    }

    float3 direction = DDGIBuildProbeRayDirection(sample_index, sample_count);
    if (is_fixed_ray || _DDGIProbeRandomRotationEnabled <= 0.5f)
    {
        return direction;
    }

    return DDGIRotateProbeRayDirection(direction);
}

float DDGIVolumeCoverage(float3 position_WS, float3 volume_min_WS, float3 volume_max_WS, float3 probe_spacing_WS)
{
    float3 outside_min      = max(volume_min_WS - position_WS, 0.0f.xxx);
    float3 outside_max      = max(position_WS - volume_max_WS, 0.0f.xxx);
    float3 outside_distance = outside_min + outside_max;
    float3 fade_distance    = max(probe_spacing_WS, 0.001f.xxx);
    float3 axis_coverage    = 1.0f.xxx - smoothstep(0.0f.xxx, fade_distance, outside_distance);
    return saturate(axis_coverage.x * axis_coverage.y * axis_coverage.z);
}

#ifndef YUTREL_DDGI_NO_GATHER_SAMPLING

float2 DDGIProbeAtlasUV(uint3 probe_coord, float3 sample_direction, uint interior_texels, float2 atlas_dimensions)
{
    float interior_texel_count = max(float(interior_texels), 1.0f);
    float tile_texel_size      = interior_texel_count + 2.0f;
    float2 octant_coordinates  = DDGIOctahedralEncode(sample_direction) * 2.0f - 1.0f;
    float2 tile_base           = float2(DDGIProbeAtlasTileBaseTexel(probe_coord, interior_texels));
    float2 atlas_texel         = tile_base + tile_texel_size * 0.5f + octant_coordinates * (interior_texel_count * 0.5f);
    return atlas_texel / max(atlas_dimensions, float2(1.0f, 1.0f));
}

float3 DDGISampleProbeIrradiance(Texture2DArray atlas, SamplerState atlas_sampler, uint3 probe_coord,
                                 float3 sample_direction, uint interior_texels, float4 atlas_dimensions)
{
    float2 uv                 = DDGIProbeAtlasUV(probe_coord, sample_direction, interior_texels, atlas_dimensions.xy);
    float3 encoded_irradiance = atlas.SampleLevel(atlas_sampler, float3(uv, float(probe_coord.y)), 0.0f).rgb;
    return DDGIDecodeProbeIrradiance(encoded_irradiance);
}

float3 DDGISampleProbeIrradianceBlendValue(Texture2DArray atlas, SamplerState atlas_sampler, uint3 probe_coord,
                                           float3 sample_direction, uint interior_texels, float4 atlas_dimensions)
{
    float2 uv                 = DDGIProbeAtlasUV(probe_coord, sample_direction, interior_texels, atlas_dimensions.xy);
    float3 encoded_irradiance = atlas.SampleLevel(atlas_sampler, float3(uv, float(probe_coord.y)), 0.0f).rgb;
    return pow(DDGIClampTextureStoreValue(encoded_irradiance), (DDGIProbeIrradianceEncodingGamma() * 0.5f).xxx);
}

float3 DDGIResolveProbeIrradianceBlendValue(float3 blend_value)
{
    return DDGISanitizeLinearEnergy(blend_value * blend_value * YUTREL_DDGI_TWO_PI);
}

float3 DDGISampleProbeDistance(Texture2DArray atlas, SamplerState atlas_sampler, uint3 probe_coord,
                               float3 sample_direction, uint interior_texels, float4 atlas_dimensions)
{
    float2 uv = DDGIProbeAtlasUV(probe_coord, sample_direction, interior_texels, atlas_dimensions.xy);
    return atlas.SampleLevel(atlas_sampler, float3(uv, float(probe_coord.y)), 0.0f).rgb;
}

float3 DDGILoadProbeDataOffset(Texture2DArray probe_data, uint3 probe_coord, bool relocation_enabled)
{
    if (!relocation_enabled)
    {
        return 0.0f.xxx;
    }

    float4 data = probe_data.Load(uint4(DDGIProbeDataTexel(probe_coord), 0u));
    return DDGIProbeDataOffset(data);
}

float3 DDGIProbeRelocatedWorldPosition(float3 volume_min_ws, float3 probe_spacing_ws, uint3 probe_coord,
                                       Texture2DArray probe_data, bool relocation_enabled)
{
    return DDGIProbeWorldPosition(volume_min_ws, probe_spacing_ws, probe_coord) +
           DDGILoadProbeDataOffset(probe_data, probe_coord, relocation_enabled);
}

float3 DDGISampleTrilinearIrradiance(Texture2DArray atlas, SamplerState atlas_sampler, float3 grid_coord,
                                     float3 sample_direction, uint3 probe_count, uint interior_texels,
                                     float4 atlas_dimensions)
{
    float3 max_probe_coord = max(float3(probe_count) - 1.0f, 0.0f.xxx);
    float3 clamped_coord   = clamp(grid_coord, float3(0.0f, 0.0f, 0.0f), max_probe_coord);
    uint3 base_coord       = uint3(floor(clamped_coord));
    uint3 next_coord       = min(base_coord + 1u, probe_count - 1u);
    float3 weight          = saturate(clamped_coord - float3(base_coord));

    float3 c000 = DDGISampleProbeIrradiance(atlas, atlas_sampler, uint3(base_coord.x, base_coord.y, base_coord.z), sample_direction, interior_texels, atlas_dimensions);
    float3 c100 = DDGISampleProbeIrradiance(atlas, atlas_sampler, uint3(next_coord.x, base_coord.y, base_coord.z), sample_direction, interior_texels, atlas_dimensions);
    float3 c010 = DDGISampleProbeIrradiance(atlas, atlas_sampler, uint3(base_coord.x, next_coord.y, base_coord.z), sample_direction, interior_texels, atlas_dimensions);
    float3 c110 = DDGISampleProbeIrradiance(atlas, atlas_sampler, uint3(next_coord.x, next_coord.y, base_coord.z), sample_direction, interior_texels, atlas_dimensions);
    float3 c001 = DDGISampleProbeIrradiance(atlas, atlas_sampler, uint3(base_coord.x, base_coord.y, next_coord.z), sample_direction, interior_texels, atlas_dimensions);
    float3 c101 = DDGISampleProbeIrradiance(atlas, atlas_sampler, uint3(next_coord.x, base_coord.y, next_coord.z), sample_direction, interior_texels, atlas_dimensions);
    float3 c011 = DDGISampleProbeIrradiance(atlas, atlas_sampler, uint3(base_coord.x, next_coord.y, next_coord.z), sample_direction, interior_texels, atlas_dimensions);
    float3 c111 = DDGISampleProbeIrradiance(atlas, atlas_sampler, uint3(next_coord.x, next_coord.y, next_coord.z), sample_direction, interior_texels, atlas_dimensions);

    float3 c00 = lerp(c000, c100, weight.x);
    float3 c10 = lerp(c010, c110, weight.x);
    float3 c01 = lerp(c001, c101, weight.x);
    float3 c11 = lerp(c011, c111, weight.x);
    float3 c0  = lerp(c00, c10, weight.y);
    float3 c1  = lerp(c01, c11, weight.y);
    return lerp(c0, c1, weight.z);
}

struct DDGIGatherSample
{
    float3 irradiance;
    float coverage;
    float visibility;
};

float DDGIMinProbeSpacing(float3 probe_spacing_WS)
{
    return min(probe_spacing_WS.x, min(probe_spacing_WS.y, probe_spacing_WS.z));
}

float3 DDGINormalBiasedSurfacePosition(float3 position_WS, float3 normal_WS, float probe_normal_bias)
{
    return position_WS + DDGISafeNormalize(normal_WS, float3(0.0f, 1.0f, 0.0f)) * max(probe_normal_bias, 0.0f);
}

float3 DDGIBiasedSurfacePosition(float3 position_WS, float3 normal_WS, float3 view_direction_WS,
                                 float probe_normal_bias, float probe_view_bias)
{
    return DDGINormalBiasedSurfacePosition(position_WS, normal_WS, probe_normal_bias) +
           DDGISafeNormalize(view_direction_WS, float3(0.0f, 0.0f, 1.0f)) * max(probe_view_bias, 0.0f);
}

float DDGIProbeVisibility(float3 distance_moments, float surface_distance, float min_variance)
{
    distance_moments = DDGIIsFinite3(distance_moments) ? max(distance_moments, 0.0f.xxx) : float3(1.0f, 1.0f, 0.0f);
    float mean       = distance_moments.r;
    float mean_sq    = max(distance_moments.g, mean * mean);
    float hit_ratio  = saturate(distance_moments.b);
    float variance   = max(mean_sq - mean * mean, max(min_variance, 0.000001f));
    float delta      = surface_distance - mean;

    if (hit_ratio <= 0.001f || !DDGIIsFinite(surface_distance))
    {
        return 1.0f;
    }

    if (delta <= 0.0f)
    {
        return 1.0f;
    }

    float chebyshev = saturate(variance / max(variance + delta * delta, 1.0e-6f));
    chebyshev *= chebyshev * chebyshev;
    return lerp(1.0f, max(YUTREL_DDGI_GATHER_MIN_VISIBILITY, chebyshev), hit_ratio);
}

float DDGIProbeSurfaceWeight(float3 position_WS, float3 normal_WS, float3 probe_position_WS)
{
    float3 surface_to_probe = DDGISafeNormalize(probe_position_WS - position_WS, normal_WS);
    float wrap_shading      = (dot(DDGISafeNormalize(normal_WS, float3(0.0f, 1.0f, 0.0f)), surface_to_probe) + 1.0f) * 0.5f;
    return wrap_shading * wrap_shading + 0.2f;
}

float DDGICrushProbeWeight(float weight)
{
    weight = max(weight, YUTREL_DDGI_GATHER_MIN_WEIGHT);
    if (weight < YUTREL_DDGI_GATHER_CRUSH_THRESHOLD)
    {
        weight *= weight * weight / (YUTREL_DDGI_GATHER_CRUSH_THRESHOLD * YUTREL_DDGI_GATHER_CRUSH_THRESHOLD);
    }
    return weight;
}

float DDGISampleProbeDistanceVisibility(Texture2DArray distance_atlas, SamplerState distance_sampler,
                                        Texture2DArray probe_data, bool relocation_enabled, uint3 probe_coord,
                                        float3 biased_position_WS, float3 normal_WS,
                                        float3 volume_min_WS, float3 probe_spacing_WS, uint distance_interior_texels,
                                        float4 distance_dimensions)
{
    float3 probe_position_WS = DDGIProbeRelocatedWorldPosition(volume_min_WS, probe_spacing_WS, probe_coord, probe_data, relocation_enabled);
    float3 probe_to_surface  = biased_position_WS - probe_position_WS;
    float surface_distance   = length(probe_to_surface);
    float3 sample_direction  = DDGISafeNormalize(probe_to_surface, normal_WS);
    float3 distance_moments  = DDGISampleProbeDistance(distance_atlas, distance_sampler, probe_coord, sample_direction, distance_interior_texels, distance_dimensions);
    float min_spacing        = DDGIMinProbeSpacing(probe_spacing_WS);
    float min_variance       = min_spacing * min_spacing * 0.0001f;
    return DDGIProbeVisibility(distance_moments, surface_distance, min_variance);
}

DDGIGatherSample DDGISampleTrilinearGather(Texture2DArray irradiance_atlas, SamplerState irradiance_sampler,
                                           Texture2DArray distance_atlas, SamplerState distance_sampler,
                                           Texture2DArray probe_data, bool relocation_enabled,
                                           float3 position_WS, float3 normal_WS, float3 view_direction_WS,
                                           float3 volume_min_WS, float3 volume_max_WS, float3 probe_spacing_WS,
                                           uint3 probe_count, uint irradiance_interior_texels,
                                           uint distance_interior_texels, float4 irradiance_dimensions,
                                           float4 distance_dimensions, float max_ray_distance,
                                           float probe_normal_bias, float probe_view_bias)
{
    DDGIGatherSample result;
    result.irradiance = 0.0f.xxx;
    result.coverage   = DDGIVolumeCoverage(position_WS, volume_min_WS, volume_max_WS, probe_spacing_WS);
    result.visibility = 0.0f;

    float3 biased_position = DDGIBiasedSurfacePosition(position_WS, normal_WS, view_direction_WS, probe_normal_bias, probe_view_bias);
    float3 grid_position   = DDGINormalBiasedSurfacePosition(position_WS, normal_WS, probe_normal_bias);
    float3 max_probe_coord = max(float3(probe_count) - 1.0f, 0.0f.xxx);
    float3 grid_coord      = (grid_position - volume_min_WS) / max(probe_spacing_WS, 0.001f.xxx);
    float3 clamped_coord   = clamp(grid_coord, 0.0f.xxx, max_probe_coord);
    uint3 base_coord       = uint3(floor(clamped_coord));
    uint3 next_coord       = min(base_coord + 1u, probe_count - 1u);
    float3 weight          = saturate(clamped_coord - float3(base_coord));
    float weight_sum       = 0.0f;
    float contribution_sum = 0.0f;

    [unroll] for (uint z = 0u; z < 2u; z++)
    {
        [unroll] for (uint y = 0u; y < 2u; y++)
        {
            [unroll] for (uint x = 0u; x < 2u; x++)
            {
                uint3 probe_coord      = uint3(x == 0u ? base_coord.x : next_coord.x,
                                               y == 0u ? base_coord.y : next_coord.y,
                                               z == 0u ? base_coord.z : next_coord.z);
                float3 axis_weight     = max(YUTREL_DDGI_GATHER_MIN_TRILINEAR_WEIGHT.xxx,
                                             float3(x == 0u ? 1.0f - weight.x : weight.x,
                                                    y == 0u ? 1.0f - weight.y : weight.y,
                                                    z == 0u ? 1.0f - weight.z : weight.z));
                float trilinear_weight = axis_weight.x * axis_weight.y * axis_weight.z;
                float3 probe_position  = DDGIProbeRelocatedWorldPosition(volume_min_WS, probe_spacing_WS, probe_coord, probe_data, relocation_enabled);
                float visibility =
                    DDGISampleProbeDistanceVisibility(distance_atlas, distance_sampler, probe_data, relocation_enabled, probe_coord, biased_position, normal_WS, volume_min_WS, probe_spacing_WS, distance_interior_texels, distance_dimensions);
                float surface_weight = DDGIProbeSurfaceWeight(position_WS, normal_WS, probe_position);
                float contribution   = DDGICrushProbeWeight(visibility * surface_weight) * trilinear_weight;
                float3 irradiance =
                    DDGISampleProbeIrradianceBlendValue(irradiance_atlas, irradiance_sampler, probe_coord, normal_WS, irradiance_interior_texels, irradiance_dimensions);

                result.irradiance += max(irradiance, 0.0f) * contribution;
                result.visibility += trilinear_weight * visibility;
                contribution_sum += contribution;
                weight_sum += trilinear_weight;
            }
        }
    }

    result.irradiance = contribution_sum > 0.0001f ? DDGIResolveProbeIrradianceBlendValue(result.irradiance / contribution_sum) : 0.0f.xxx;
    result.visibility = weight_sum > 0.0f ? saturate(result.visibility / weight_sum) : 0.0f;
    return result;
}

#endif

#endif
