#ifndef YUTREL_DDGI_INCLUDED
#define YUTREL_DDGI_INCLUDED

static const float YUTREL_DDGI_GOLDEN_ANGLE = 2.39996322972865332f;

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
    uint last_interior_texel  = interior_texel_count - 1u;
    bool left                 = local_texel.x == 0u;
    bool right                = local_texel.x >= tile_texel_size - 1u;
    bool bottom               = local_texel.y == 0u;
    bool top                  = local_texel.y >= tile_texel_size - 1u;

    if (left && bottom)
    {
        return uint2(last_interior_texel, last_interior_texel);
    }
    if (right && bottom)
    {
        return uint2(0u, last_interior_texel);
    }
    if (left && top)
    {
        return uint2(last_interior_texel, 0u);
    }
    if (right && top)
    {
        return uint2(0u, 0u);
    }
    if (left)
    {
        return uint2(last_interior_texel, min(interior_texel_count - local_texel.y, last_interior_texel));
    }
    if (right)
    {
        return uint2(0u, min(interior_texel_count - local_texel.y, last_interior_texel));
    }
    if (bottom)
    {
        return uint2(min(interior_texel_count - local_texel.x, last_interior_texel), last_interior_texel);
    }
    if (top)
    {
        return uint2(min(interior_texel_count - local_texel.x, last_interior_texel), 0u);
    }

    return min(local_texel - 1u, uint2(last_interior_texel, last_interior_texel));
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

float DDGIVolumeCoverage(float3 position_WS, float3 volume_min_WS, float3 volume_max_WS, float fade_distance)
{
    float3 distance_to_min = position_WS - volume_min_WS;
    float3 distance_to_max = volume_max_WS - position_WS;
    float min_distance     = min(min(distance_to_min.x, distance_to_min.y), min(distance_to_min.z, min(min(distance_to_max.x, distance_to_max.y), distance_to_max.z)));

    if (min_distance <= 0.0f)
    {
        return 0.0f;
    }

    return smoothstep(0.0f, max(fade_distance, 0.001f), min_distance);
}

float2 DDGIProbeAtlasUV(uint3 probe_coord, float3 sample_direction, uint interior_texels, float2 atlas_dimensions)
{
    float2 encoded_direction = DDGIOctahedralEncode(sample_direction);
    float2 tile_base         = float2(DDGIProbeAtlasInteriorBaseTexel(probe_coord, interior_texels));
    float2 atlas_texel       = tile_base + encoded_direction * max(float(interior_texels), 1.0f);
    return (atlas_texel + 0.5f) / max(atlas_dimensions, 1.0f.xx);
}

float3 DDGISampleProbeIrradiance(Texture2DArray atlas, SamplerState atlas_sampler, uint3 probe_coord,
                                 float3 sample_direction, uint interior_texels, float4 atlas_dimensions)
{
    float2 uv = DDGIProbeAtlasUV(probe_coord, sample_direction, interior_texels, atlas_dimensions.xy);
    return SAMPLE_TEXTURE2D_ARRAY_LOD(atlas, atlas_sampler, uv, probe_coord.y, 0.0f).rgb;
}

float3 DDGISampleTrilinearIrradiance(Texture2DArray atlas, SamplerState atlas_sampler, float3 grid_coord,
                                     float3 sample_direction, uint3 probe_count, uint interior_texels,
                                     float4 atlas_dimensions)
{
    float3 max_probe_coord = max(float3(probe_count) - 1.0f, 0.0f.xxx);
    float3 clamped_coord   = clamp(grid_coord, 0.0f.xxx, max_probe_coord);
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

#endif
