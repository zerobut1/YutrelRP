#ifndef YUTREL_DEBUG_VIEW_PASS_INCLUDED
#define YUTREL_DEBUG_VIEW_PASS_INCLUDED

#include "DDGI/DDGI.hlsl"
#include "Utils/GBuffer.hlsl"
#include "Utils/Shadow.hlsl"

TEXTURE2D(_ShadowMask);
SAMPLER(sampler_ShadowMask);
TEXTURE2D(_ScreenSpaceAO);
SAMPLER(sampler_ScreenSpaceAO);
TEXTURE2D_ARRAY(_DDGIProbeRayData);
SAMPLER(sampler_DDGIProbeRayData);
TEXTURE2D_ARRAY(_DDGIProbeIrradiance);
SAMPLER(sampler_DDGIProbeIrradiance);
TEXTURE2D_ARRAY(_DDGIProbeDistance);
SAMPLER(sampler_DDGIProbeDistance);
TEXTURE2D_ARRAY(_DDGIProbeData);
SAMPLER(sampler_DDGIProbeData);

int _DebugViewMode;
int _DebugViewIssue;
float4 _DDGIProbeCount;
float4 _DDGIProbeRayDataDimensions;
int _DDGIProbeRayDataDebugSlice;
float _DDGIProbeRayDataMaxDistance;
float4 _DDGIProbeIrradianceDimensions;
int _DDGIProbeIrradianceDebugSlice;
float4 _DDGIProbeDistanceDimensions;
int _DDGIProbeDistanceDebugSlice;
float4 _DDGIProbeDataDimensions;
int _DDGIProbeDataDebugSlice;
float3 _DDGIVolumeMinWS;
float3 _DDGIVolumeMaxWS;
float3 _DDGIProbeSpacingWS;
float _DDGIGatherValid;
float _DDGIDiffuseIntensity;

struct DebugViewShadowData
{
    int cascade_index;
    float strength;
};

float DebugViewFadedShadowStrength(float distance, float scale, float fade)
{
    return saturate((1.0f - distance * scale) * fade);
}

DebugViewShadowData GetDebugViewShadowData(float3 position_WS, float depth)
{
    DebugViewShadowData out_data;
    out_data.cascade_index = -1;
    out_data.strength      = DebugViewFadedShadowStrength(depth, _DirectionalShadowDistanceFade.x, _DirectionalShadowDistanceFade.y);

    for (int cascade_index = 0; cascade_index < _DirectionalShadowCascadeCount; cascade_index++)
    {
        float4 sphere = _DirectionalShadowCascadeDatas[cascade_index].culling_sphere;
        float dist    = distance(position_WS, sphere.xyz);
        if (dist < sphere.w)
        {
            if (cascade_index == _DirectionalShadowCascadeCount - 1)
            {
                out_data.strength *= DebugViewFadedShadowStrength(dist, 1.0f / sphere.w, _DirectionalShadowDistanceFade.z);
            }

            out_data.cascade_index = cascade_index;
            break;
        }
    }
    out_data.strength = out_data.cascade_index == -1 ? 0.0f : out_data.strength;

    return out_data;
}

float4 GetCascadeColor(int cascade_index)
{
    switch (cascade_index)
    {
    case 0:
        return float4(1.0f, 0.0f, 0.0f, 1.0f);
    case 1:
        return float4(0.0f, 1.0f, 0.0f, 1.0f);
    case 2:
        return float4(0.0f, 0.0f, 1.0f, 1.0f);
    case 3:
        return float4(1.0f, 1.0f, 0.0f, 1.0f);
    default:
        return float4(0.0f, 0.0f, 0.0f, 1.0f);
    }
}

float DebugViewDither(float2 position_SS)
{
    // Stable screen-space interleaved gradient noise. Amplitude is one 8-bit
    // output step, enough to hide quantization bands without changing depth
    // ordering at debug-view scale.
    float noise = frac(52.9829189f * frac(dot(position_SS, float2(0.06711056f, 0.00583715f))));
    return (noise - 0.5f) / 255.0f;
}

float DebugViewLinearDepth01(float raw_depth)
{
#if UNITY_REVERSED_Z
    if (raw_depth <= 0.0f)
    {
        return 1.0f;
    }
#else
    if (raw_depth >= 1.0f)
    {
        return 1.0f;
    }
#endif

    float linear_depth;
    if (IsOrthographicCamera())
    {
        linear_depth = OrthographicDepthBufferToLinear(raw_depth);
    }
    else
    {
        linear_depth = LinearEyeDepth(raw_depth, _ZBufferParams);
    }

    float linear_depth_01 = saturate(linear_depth / _ProjectionParams.z);

    // Full camera-far linear depth is technically correct but often collapses
    // editor scenes to near-black when far clip is large. Keep near<far
    // ordering and white far/clear semantics while using a display curve so
    // intermediate scene depth remains visible, matching test-2's practical
    // debug-view intent.
    return sqrt(linear_depth_01);
}

bool DebugViewIsValidSurfaceDepth(float raw_depth)
{
#if UNITY_REVERSED_Z
    return raw_depth > 0.0f;
#else
    return raw_depth < 1.0f;
#endif
}

float4 SampleDebugViewGBuffer(float2 uv)
{
    EncodedGBuffer gbuffer;
    gbuffer.GBuffer_A   = SAMPLE_TEXTURE2D(_GBuffer_A, sampler_GBuffer_A, uv);
    gbuffer.GBuffer_B   = SAMPLE_TEXTURE2D(_GBuffer_B, sampler_GBuffer_B, uv);
    gbuffer.GBuffer_C   = SAMPLE_TEXTURE2D(_GBuffer_C, sampler_GBuffer_C, uv);
    gbuffer.scene_depth = 0.0f;
    gbuffer.uv          = uv;

    GBufferData gbuffer_data = DecodeGBuffer(gbuffer);
    if (gbuffer_data.shading_model_id != 1)
    {
        return float4(0.0f, 0.0f, 0.0f, 1.0f);
    }

    if (_DebugViewMode == 1)
    {
        return float4(gbuffer_data.base_color, 1.0f);
    }
    if (_DebugViewMode == 2)
    {
        return float4(gbuffer_data.roughness.xxx, 1.0f);
    }
    if (_DebugViewMode == 3)
    {
        return float4(gbuffer_data.metallic.xxx, 1.0f);
    }
    if (_DebugViewMode == 4)
    {
        return float4(gbuffer_data.specular.xxx, 1.0f);
    }
    return float4(gbuffer_data.normal_WS * 0.5f + 0.5f, 1.0f);
}

float4 SampleDebugViewAmbientOcclusion(float2 uv)
{
    float scene_depth = SAMPLE_TEXTURE2D(_SceneDepth, sampler_SceneDepth, uv).r;
    if (!DebugViewIsValidSurfaceDepth(scene_depth))
    {
        return float4(1.0f, 1.0f, 1.0f, 1.0f);
    }

    EncodedGBuffer gbuffer;
    gbuffer.GBuffer_A   = SAMPLE_TEXTURE2D(_GBuffer_A, sampler_GBuffer_A, uv);
    gbuffer.GBuffer_B   = SAMPLE_TEXTURE2D(_GBuffer_B, sampler_GBuffer_B, uv);
    gbuffer.GBuffer_C   = SAMPLE_TEXTURE2D(_GBuffer_C, sampler_GBuffer_C, uv);
    gbuffer.scene_depth = scene_depth;
    gbuffer.uv          = uv;

    GBufferData gbuffer_data = DecodeGBuffer(gbuffer);
    if (gbuffer_data.shading_model_id != 1)
    {
        return float4(1.0f, 1.0f, 1.0f, 1.0f);
    }

    float material_AO     = saturate(gbuffer_data.material_AO);
    float screen_space_AO = saturate(SAMPLE_TEXTURE2D(_ScreenSpaceAO, sampler_ScreenSpaceAO, uv).r);
    float combined_AO     = min(material_AO, screen_space_AO);
    return float4(combined_AO.xxx, 1.0f);
}

float3 DebugViewToneMapDDGIRadiance(float3 radiance)
{
    return saturate(1.0f - exp(-max(radiance, 0.0f) * 0.5f));
}

float3 DebugViewDDGIDiffuseScale(GBufferData gbuffer_data, float2 uv)
{
    float metallic        = saturate(gbuffer_data.metallic);
    float material_AO     = saturate(gbuffer_data.material_AO);
    float screen_space_AO = saturate(SAMPLE_TEXTURE2D(_ScreenSpaceAO, sampler_ScreenSpaceAO, uv).r);
    float combined_AO     = min(material_AO, screen_space_AO);
    return max(gbuffer_data.base_color, 0.0f) * (1.0f - metallic) * combined_AO;
}

float4 SampleDebugViewDDGIProbeRayData(float2 uv)
{
    uint width        = (uint)max(_DDGIProbeRayDataDimensions.x, 1.0f);
    uint height       = (uint)max(_DDGIProbeRayDataDimensions.y, 1.0f);
    uint slice        = (uint)clamp(_DDGIProbeRayDataDebugSlice, 0, max((int)_DDGIProbeRayDataDimensions.z - 1, 0));
    uint3 probe_count = uint3((uint)max(_DDGIProbeCount.x, 1.0f), (uint)max(_DDGIProbeCount.y, 1.0f), (uint)max(_DDGIProbeCount.z, 1.0f));

    uint2 texel          = uint2(min((uint)(uv.x * width), width - 1), min((uint)((1.0f - uv.y) * height), height - 1));
    uint3 probe_coord    = DDGIProbeCoordFromPlaneIndex(texel.y, slice, probe_count);
    uint3 ray_data_texel = DDGIProbeRayDataTexel(texel.x, probe_coord, probe_count);
    float4 data          = LOAD_TEXTURE2D_ARRAY(_DDGIProbeRayData, ray_data_texel.xy, ray_data_texel.z);
    float probe_parity   = frac((float)(probe_coord.x + probe_coord.y + probe_coord.z) * 0.5f);
    float row_line       = frac((1.0f - uv.y) * height) < 0.08f ? 1.0f : 0.0f;

    float signed_distance = data.a;
    if (DDGIProbeRayDataIsMiss(signed_distance, _DDGIProbeRayDataMaxDistance))
    {
        float3 miss_color = float3(0.0f, 0.0f, 0.0f) + probe_parity * 0.02f + row_line * 0.2f;
        return float4(saturate(miss_color), 1.0f);
    }

    if (DDGIProbeRayDataIsBackface(signed_distance, _DDGIProbeRayDataMaxDistance))
    {
        return float4(saturate(float3(0.95f, 0.18f, 0.85f) + row_line * 0.25f), 1.0f);
    }

    float3 radiance_color = DebugViewToneMapDDGIRadiance(data.rgb);
    return float4(saturate(radiance_color + row_line * 0.18f), 1.0f);
}

float3 DebugViewDDGIIndexColor(uint3 probe_coord, uint3 probe_count)
{
    float3 denom = max(float3(probe_count) - 1.0f, 1.0f.xxx);
    return float3(probe_coord) / denom;
}

float4 SampleDebugViewDDGIAtlas(Texture2DArray atlas, float4 dimensions, int debug_slice, float2 uv, bool radiance_atlas)
{
    uint width           = (uint)max(dimensions.x, 1.0f);
    uint height          = (uint)max(dimensions.y, 1.0f);
    uint slice           = (uint)clamp(debug_slice, 0, max((int)dimensions.z - 1, 0));
    uint tile_texel_size = (uint)max(dimensions.w, 3.0f);
    uint interior_texels = max(tile_texel_size - 2u, 1u);
    uint3 probe_count    = uint3(max(width / tile_texel_size, 1u), max((uint)dimensions.z, 1u), max(height / tile_texel_size, 1u));

    uint2 texel                = uint2(min((uint)(uv.x * width), width - 1), min((uint)((1.0f - uv.y) * height), height - 1));
    uint3 probe_coord          = uint3(min(texel.x / tile_texel_size, probe_count.x - 1u), slice, min(texel.y / tile_texel_size, probe_count.z - 1u));
    uint2 local_texel          = texel - DDGIProbeAtlasTileBaseTexel(probe_coord, interior_texels);
    DDGIAtlasTexel atlas_texel = DDGIProbeAtlasTexel(probe_coord, local_texel, interior_texels);
    float4 data                = atlas.Load(uint4(texel, slice, 0));

    if (atlas_texel.is_border)
    {
        float3 index_color = DebugViewDDGIIndexColor(probe_coord, probe_count);
        return float4(saturate(float3(1.0f, 0.78f, 0.18f) * (0.65f + index_color * 0.35f)), 1.0f);
    }

    if (radiance_atlas)
    {
        return float4(DebugViewToneMapDDGIRadiance(data.rgb), 1.0f);
    }

    float2 interior_uv     = (float2(local_texel - 1u) + 0.5f) / max(float(interior_texels), 1.0f);
    float3 direction       = DDGIOctahedralDecode(interior_uv);
    float3 direction_color = direction * 0.5f + 0.5f;
    float3 index_tint      = DebugViewDDGIIndexColor(probe_coord, probe_count) * 0.25f;
    float3 stored_color    = saturate(data.rgb);
    float stored_weight    = saturate(max(max(stored_color.r, stored_color.g), stored_color.b));
    float3 debug_color     = saturate(direction_color * 0.75f + index_tint);
    return float4(lerp(debug_color, stored_color, stored_weight * 0.55f), 1.0f);
}

float4 SampleDebugViewDDGIProbeData(float2 uv)
{
    uint width  = (uint)max(_DDGIProbeDataDimensions.x, 1.0f);
    uint height = (uint)max(_DDGIProbeDataDimensions.y, 1.0f);
    uint slice  = (uint)clamp(_DDGIProbeDataDebugSlice, 0, max((int)_DDGIProbeDataDimensions.z - 1, 0));

    uint2 texel         = uint2(min((uint)(uv.x * width), width - 1), min((uint)((1.0f - uv.y) * height), height - 1));
    uint3 probe_count   = uint3(width, (uint)max(_DDGIProbeDataDimensions.z, 1.0f), height);
    uint3 probe_coord   = uint3(texel.x, slice, texel.y);
    float4 data         = LOAD_TEXTURE2D_ARRAY(_DDGIProbeData, texel, slice);
    float active        = saturate(data.w);
    float3 index_color  = DebugViewDDGIIndexColor(probe_coord, probe_count);
    float cell_line     = (frac(uv.x * width) < 0.08f || frac((1.0f - uv.y) * height) < 0.08f) ? 1.0f : 0.0f;
    float3 offset_color = saturate(abs(data.xyz));
    float3 active_color = active.xxx * float3(0.05f, 0.35f, 0.05f);
    return float4(saturate(index_color * 0.35f + active_color + offset_color + cell_line * 0.25f), 1.0f);
}

float DebugViewEvaluateDDGICoverage(float3 position_WS)
{
    return _DDGIGatherValid > 0.5f ? DDGIVolumeCoverage(position_WS, _DDGIVolumeMinWS, _DDGIVolumeMaxWS, _DDGIProbeSpacingWS) : 0.0f;
}

DDGIGatherSample DebugViewEvaluateDDGIGather(float3 position_WS, float3 normal_WS, float3 view_direction_WS)
{
    uint3 probe_count       = uint3((uint)max(_DDGIProbeCount.x, 1.0f), (uint)max(_DDGIProbeCount.y, 1.0f), (uint)max(_DDGIProbeCount.z, 1.0f));
    uint irradiance_texels  = (uint)max(_DDGIProbeIrradianceDimensions.w - 2.0f, 1.0f);
    uint distance_texels    = (uint)max(_DDGIProbeDistanceDimensions.w - 2.0f, 1.0f);
    DDGIGatherSample sample = DDGISampleTrilinearGather(_DDGIProbeIrradiance, sampler_DDGIProbeIrradiance, _DDGIProbeDistance, sampler_DDGIProbeDistance, position_WS, normal_WS, view_direction_WS, _DDGIVolumeMinWS, _DDGIVolumeMaxWS, _DDGIProbeSpacingWS, probe_count, irradiance_texels, distance_texels, _DDGIProbeIrradianceDimensions, _DDGIProbeDistanceDimensions, _DDGIProbeRayDataMaxDistance);
    sample.irradiance *= _DDGIDiffuseIntensity;
    sample.coverage *= _DDGIGatherValid > 0.5f ? 1.0f : 0.0f;
    return sample;
}

float4 SampleDebugViewDDGIGather(float2 uv, int gather_debug_mode)
{
    float scene_depth = SAMPLE_TEXTURE2D(_SceneDepth, sampler_SceneDepth, uv).r;
    if (!DebugViewIsValidSurfaceDepth(scene_depth))
    {
        return float4(0.0f, 0.0f, 0.0f, 1.0f);
    }

    EncodedGBuffer gbuffer;
    gbuffer.GBuffer_A   = SAMPLE_TEXTURE2D(_GBuffer_A, sampler_GBuffer_A, uv);
    gbuffer.GBuffer_B   = SAMPLE_TEXTURE2D(_GBuffer_B, sampler_GBuffer_B, uv);
    gbuffer.GBuffer_C   = SAMPLE_TEXTURE2D(_GBuffer_C, sampler_GBuffer_C, uv);
    gbuffer.scene_depth = scene_depth;
    gbuffer.uv          = uv;

    GBufferData gbuffer_data = DecodeGBuffer(gbuffer);
    if (gbuffer_data.shading_model_id != 1)
    {
        return float4(0.0f, 0.0f, 0.0f, 1.0f);
    }

    float3 position_WS    = ComputeWorldSpacePositionFromFullScreenUV(uv, scene_depth);
    float3 view_WS        = GetWorldSpaceViewDirectionForSurface(position_WS);
    DDGIGatherSample ddgi = DebugViewEvaluateDDGIGather(position_WS, gbuffer_data.normal_WS, view_WS);
    if (gather_debug_mode == 1)
    {
        return float4(ddgi.coverage.xxx, 1.0f);
    }
    if (gather_debug_mode == 2)
    {
        return float4(ddgi.coverage, ddgi.visibility, ddgi.coverage * ddgi.visibility, 1.0f);
    }

    float3 diffuse_scale = DebugViewDDGIDiffuseScale(gbuffer_data, uv);
    float3 diffuse_only  = ddgi.irradiance * diffuse_scale * ddgi.coverage;
    return float4(DebugViewToneMapDDGIRadiance(diffuse_only), 1.0f);
}

float4 DebugViewPassFragment(FullScreenVaryings input) : SV_Target
{
    if (_DebugViewIssue != 0)
    {
        return float4(1.0f, 0.0f, 1.0f, 1.0f);
    }

    switch (_DebugViewMode)
    {
    case 1:
    case 2:
    case 3:
    case 4:
    case 5:
        return SampleDebugViewGBuffer(input.uv);

    case 6:
    {
        float debug_scene_depth = DebugViewLinearDepth01(SAMPLE_TEXTURE2D(_SceneDepth, sampler_SceneDepth, input.uv).r);
        if (debug_scene_depth < 1.0f)
        {
            debug_scene_depth = saturate(debug_scene_depth + DebugViewDither(input.position_CS.xy));
        }
        return float4(debug_scene_depth.xxx, 1.0f);
    }

    case 7:
        return SAMPLE_TEXTURE2D(_ShadowMask, sampler_ShadowMask, input.uv).rrrr;

    case 8:
    {
        float scene_depth = SAMPLE_TEXTURE2D(_SceneDepth, sampler_SceneDepth, input.uv).r;
        if (scene_depth <= 0.0f || scene_depth >= 1.0f)
        {
            return float4(0.0f, 0.0f, 0.0f, 1.0f);
        }

        float3 position_WS              = ComputeWorldSpacePositionFromFullScreenUV(input.uv, scene_depth);
        float linear_depth              = LinearEyeDepth(position_WS, UNITY_MATRIX_V);
        DebugViewShadowData shadow_data = GetDebugViewShadowData(position_WS, linear_depth);
        return GetCascadeColor(shadow_data.cascade_index);
    }

    case 9:
        return SampleDebugViewAmbientOcclusion(input.uv);

    case 12:
        return SampleDebugViewDDGIProbeRayData(input.uv);

    case 13:
        return SampleDebugViewDDGIAtlas(_DDGIProbeIrradiance, _DDGIProbeIrradianceDimensions, _DDGIProbeIrradianceDebugSlice, input.uv, true);

    case 14:
        return SampleDebugViewDDGIAtlas(_DDGIProbeDistance, _DDGIProbeDistanceDimensions, _DDGIProbeDistanceDebugSlice, input.uv, false);

    case 15:
        return SampleDebugViewDDGIProbeData(input.uv);

    case 16:
        return SampleDebugViewDDGIGather(input.uv, 0);

    case 17:
        return SampleDebugViewDDGIGather(input.uv, 1);

    case 18:
        return SampleDebugViewDDGIGather(input.uv, 2);
    }

    return float4(0.0f, 0.0f, 0.0f, 1.0f);
}

#endif
