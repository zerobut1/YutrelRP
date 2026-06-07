#ifndef YUTREL_DDGI_PROBE_BLEND_COMMON_INCLUDED
#define YUTREL_DDGI_PROBE_BLEND_COMMON_INCLUDED

#define YUTREL_DDGI_NO_GATHER_SAMPLING
#include "Assets/YutrelRP/Shader/DDGI/DDGI.hlsl"

Texture2DArray<float4> _DDGIProbeRayData;

float4 _DDGIProbeCount;
float4 _DDGIProbeRayDataDimensions;
float4 _DDGIProbeIrradianceDimensions;
float4 _DDGIProbeDistanceDimensions;
float _DDGIProbeRayDataMaxDistance;
float3 _DDGIProbeSpacingWS;
float _DDGIProbeHysteresis;
float _DDGIProbeDistanceExponent;
float _DDGIProbeRandomRayBackfaceThreshold;
float _DDGIProbeFixedRaysEnabled;
float _DDGIProbeSkipFixedRaysForBlend;
float _DDGIProbeIrradianceThreshold;
float _DDGIProbeBrightnessThreshold;

static const uint DDGI_BLEND_THREAD_GROUP_SIZE_X      = 8u;
static const uint DDGI_BLEND_THREAD_GROUP_SIZE_Y      = 8u;
static const float DDGI_IRRADIANCE_DARKENING_MIN_STEP = 1.0f / 1024.0f;

uint3 DDGIProbeBlendProbeCount()
{
    return uint3((uint)max(_DDGIProbeCount.x, 1.0f), (uint)max(_DDGIProbeCount.y, 1.0f), (uint)max(_DDGIProbeCount.z, 1.0f));
}

bool DDGIProbeBlendAtlasTexel(uint3 dispatch_id, float4 dimensions, out uint3 probe_coord, out uint2 local_texel, out uint interior_texels)
{
    uint width           = (uint)max(dimensions.x, 1.0f);
    uint height          = (uint)max(dimensions.y, 1.0f);
    uint slice_count     = (uint)max(dimensions.z, 1.0f);
    uint tile_texel_size = (uint)max(dimensions.w, 3.0f);

    probe_coord     = uint3(0u, 0u, 0u);
    local_texel     = uint2(0u, 0u);
    interior_texels = max(tile_texel_size - 2u, 1u);

    uint3 probe_count = DDGIProbeBlendProbeCount();
    probe_coord       = uint3(dispatch_id.x / tile_texel_size, dispatch_id.z, dispatch_id.y / tile_texel_size);
    bool valid        = dispatch_id.x < width && dispatch_id.y < height && dispatch_id.z < slice_count &&
                        probe_coord.x < probe_count.x && probe_coord.y < probe_count.y && probe_coord.z < probe_count.z;

    local_texel = valid ? uint2(dispatch_id.x, dispatch_id.y) - DDGIProbeAtlasTileBaseTexel(probe_coord, interior_texels) : uint2(0u, 0u);
    return valid && local_texel.x < tile_texel_size && local_texel.y < tile_texel_size;
}

bool DDGIProbeBlendInteriorAtlasTexel(uint3 dispatch_id, float4 dimensions, out uint3 probe_coord, out uint2 local_texel, out uint interior_texels)
{
    if (!DDGIProbeBlendAtlasTexel(dispatch_id, dimensions, probe_coord, local_texel, interior_texels))
    {
        return false;
    }

    return !DDGIAtlasLocalTexelIsBorder(local_texel, interior_texels);
}

bool DDGIProbeBlendBorderAtlasTexel(uint3 dispatch_id, float4 dimensions, out uint3 probe_coord, out uint2 local_texel, out uint interior_texels)
{
    if (!DDGIProbeBlendAtlasTexel(dispatch_id, dimensions, probe_coord, local_texel, interior_texels))
    {
        return false;
    }

    return DDGIAtlasLocalTexelIsBorder(local_texel, interior_texels);
}

uint3 DDGIProbeBlendWrappedTexel(uint3 probe_coord, uint2 local_texel, uint interior_texels)
{
    uint2 tile_base      = DDGIProbeAtlasTileBaseTexel(probe_coord, interior_texels);
    uint2 interior_texel = DDGIAtlasWrappedInteriorTexel(local_texel, interior_texels);
    return uint3(tile_base + 1u + interior_texel, probe_coord.y);
}

float4 DDGIProbeBlendHistory(float4 history, float4 current_value)
{
    float history_weight = history.a > 0.0f ? saturate(_DDGIProbeHysteresis) : 0.0f;
    return lerp(current_value, history, history_weight);
}

float DDGIProbeBlendMaxComponent(float3 value)
{
    return max(value.x, max(value.y, value.z));
}

float DDGIProbeBlendLinearRGBToLuminance(float3 rgb)
{
    return dot(rgb, float3(0.2126f, 0.7152f, 0.0722f));
}

float4 DDGIProbeBlendIrradianceHistory(float4 history, float4 current_value)
{
    float3 history_irradiance = DDGIClampProbeIrradianceSampleValue(history.rgb);
    float3 current_irradiance = DDGIClampProbeIrradianceSampleValue(current_value.rgb);
    float hysteresis          = saturate(_DDGIProbeHysteresis);
    if (dot(history_irradiance, history_irradiance) == 0.0f)
    {
        hysteresis = 0.0f;
    }

    float3 delta = current_irradiance - history_irradiance;
    if (DDGIProbeBlendMaxComponent(history_irradiance - current_irradiance) > _DDGIProbeIrradianceThreshold)
    {
        hysteresis = max(0.0f, hysteresis - 0.75f);
    }

    if (DDGIProbeBlendLinearRGBToLuminance(delta) > _DDGIProbeBrightnessThreshold)
    {
        delta *= 0.25f;
    }

    float3 lerp_delta = (1.0f - hysteresis) * delta;
    if (DDGIProbeBlendMaxComponent(current_irradiance) < DDGIProbeBlendMaxComponent(history_irradiance))
    {
        lerp_delta = min(max(DDGI_IRRADIANCE_DARKENING_MIN_STEP.xxx, abs(lerp_delta)), abs(delta)) * sign(lerp_delta);
    }

    return DDGIProbeIrradianceStoreValue(history_irradiance + lerp_delta);
}

DDGIProbeRayData DDGIProbeBlendLoadRayData(uint ray_index, uint3 probe_coord, uint3 probe_count)
{
    float4 raw_ray_data = _DDGIProbeRayData.Load(uint4(DDGIProbeRayDataTexel(ray_index, probe_coord, probe_count), 0u));
    if (!DDGIProbeRayDataRawIsValid(raw_ray_data))
    {
        raw_ray_data = DDGIProbeRayDataEncodeMissValue(0.0f.xxx);
    }

    return DDGIProbeRayDataDecode(raw_ray_data);
}

float DDGIProbeBlendIrradianceDirectionWeight(float3 atlas_direction, float3 ray_direction)
{
    return saturate(dot(atlas_direction, ray_direction));
}

float DDGIProbeBlendDistanceDirectionWeight(float3 atlas_direction, float3 ray_direction)
{
    float cosine = saturate(dot(atlas_direction, ray_direction));
    return pow(cosine, max(_DDGIProbeDistanceExponent, 0.01f));
}

float DDGIProbeBlendMaxDistance()
{
    return max(length(max(abs(_DDGIProbeSpacingWS), 0.001f.xxx)) * 1.5f, 0.001f);
}

float2 DDGIProbeBlendDistanceMoments(DDGIProbeRayData ray_data)
{
    float max_distance    = DDGIProbeBlendMaxDistance();
    float signed_distance = ray_data.distance;
    float distance        = min(abs(signed_distance), max_distance);
    return float2(distance, distance * distance);
}

bool DDGIProbeBlendIrradiance(uint3 probe_coord, uint2 local_texel, uint interior_texels, out float3 irradiance)
{
    uint rays_per_probe = (uint)max(_DDGIProbeRayDataDimensions.x, 1.0f);
    uint3 probe_count   = DDGIProbeBlendProbeCount();
    bool fixed_rays     = _DDGIProbeFixedRaysEnabled > 0.5f;
    bool skip_fixed     = _DDGIProbeSkipFixedRaysForBlend > 0.5f;
    uint ray_start      = DDGIProbeBlendRayStart(skip_fixed, rays_per_probe);
    uint blend_rays     = DDGIProbeBlendRayCount(skip_fixed, rays_per_probe);
    float2 interior_uv  = DDGIAtlasInteriorUV(local_texel, interior_texels);
    float3 direction    = DDGIOctahedralDecode(interior_uv);
    float3 sum          = float3(0.0f, 0.0f, 0.0f);
    float weight_sum    = 0.0f;
    uint backface_count = 0u;
    uint max_backfaces  = (uint)(float(blend_rays) * saturate(_DDGIProbeRandomRayBackfaceThreshold));

    irradiance = 0.0f.xxx;

    [loop] for (uint ray_index = ray_start; ray_index < rays_per_probe; ray_index++)
    {
        float3 ray_direction      = DDGIProbeRayDirection(ray_index, rays_per_probe, fixed_rays);
        float weight              = DDGIProbeBlendIrradianceDirectionWeight(direction, ray_direction);
        DDGIProbeRayData ray_data = DDGIProbeBlendLoadRayData(ray_index, probe_coord, probe_count);
        if (DDGIProbeRayDataIsBackface(ray_data.distance))
        {
            backface_count++;
            if (backface_count >= max_backfaces)
            {
                return false;
            }
            continue;
        }
        sum += ray_data.radiance * weight;
        weight_sum += weight;
    }

    float epsilon = (float)blend_rays * 1.0e-9f;
    irradiance    = sum / (2.0f * max(weight_sum, epsilon));
    return true;
}

float2 DDGIProbeBlendDistance(uint3 probe_coord, uint2 local_texel, uint interior_texels)
{
    uint rays_per_probe = (uint)max(_DDGIProbeRayDataDimensions.x, 1.0f);
    uint3 probe_count   = DDGIProbeBlendProbeCount();
    bool fixed_rays     = _DDGIProbeFixedRaysEnabled > 0.5f;
    bool skip_fixed     = _DDGIProbeSkipFixedRaysForBlend > 0.5f;
    uint ray_start      = DDGIProbeBlendRayStart(skip_fixed, rays_per_probe);
    uint blend_rays     = DDGIProbeBlendRayCount(skip_fixed, rays_per_probe);
    float2 interior_uv  = DDGIAtlasInteriorUV(local_texel, interior_texels);
    float3 direction    = DDGIOctahedralDecode(interior_uv);
    float2 sum          = float2(0.0f, 0.0f);
    float weight_sum    = 0.0f;

    [loop] for (uint ray_index = ray_start; ray_index < rays_per_probe; ray_index++)
    {
        float3 ray_direction      = DDGIProbeRayDirection(ray_index, rays_per_probe, fixed_rays);
        float weight              = DDGIProbeBlendDistanceDirectionWeight(direction, ray_direction);
        DDGIProbeRayData ray_data = DDGIProbeBlendLoadRayData(ray_index, probe_coord, probe_count);
        sum += DDGIProbeBlendDistanceMoments(ray_data) * weight;
        weight_sum += weight;
    }

    float epsilon = max((float)blend_rays * 1.0e-9f, 1.0e-9f);
    return sum / (2.0f * max(weight_sum, epsilon));
}

#endif
