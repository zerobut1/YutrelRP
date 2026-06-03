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

#endif
