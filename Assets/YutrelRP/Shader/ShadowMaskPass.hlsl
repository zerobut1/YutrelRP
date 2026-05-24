#ifndef YUTREL_SHADOW_MASK_PASS_INCLUDED
#define YUTREL_SHADOW_MASK_PASS_INCLUDED

#include "Utils/GBuffer.hlsl"
#include "Utils/Light.hlsl"
#include "Utils/Shadow.hlsl"

struct ShadowData
{
    int cascade_index;
    float cascade_blend;
    float strength;
};

#if defined(_DIRECTIONAL_SHADOW_FILTER_NONE)
#elif defined(_DIRECTIONAL_SHADOW_FILTER_LOW)
#define DIRECTIONAL_FILTER_SAMPLES 4
#define DIRECTIONAL_FILTER_SETUP SampleShadow_ComputeSamples_Tent_3x3
#elif defined(_DIRECTIONAL_SHADOW_FILTER_MEDIUM)
#define DIRECTIONAL_FILTER_SAMPLES 9
#define DIRECTIONAL_FILTER_SETUP SampleShadow_ComputeSamples_Tent_5x5
#elif defined(_DIRECTIONAL_SHADOW_FILTER_HIGH)
#define DIRECTIONAL_FILTER_SAMPLES 16
#define DIRECTIONAL_FILTER_SETUP SampleShadow_ComputeSamples_Tent_7x7
#endif

float FadedShadowStrength(float distance, float scale, float fade)
{
    return saturate((1.0f - distance * scale) * fade);
}

ShadowData GetShadowData(float3 position_WS, float depth)
{
    ShadowData out_data;
    out_data.cascade_index = -1;
    out_data.cascade_blend = 1.0f;
    out_data.strength      = FadedShadowStrength(depth, _DirectionalShadowDistanceFade.x, _DirectionalShadowDistanceFade.y);

    for (int cascade_index = 0; cascade_index < _DirectionalShadowCascadeCount; cascade_index++)
    {
        float4 sphere = _DirectionalShadowCascadeDatas[cascade_index].culling_sphere;
        float dist    = distance(position_WS, sphere.xyz);
        if (dist < sphere.w)
        {
            float cascade_fade = FadedShadowStrength(dist, 1.0f / sphere.w, _DirectionalShadowDistanceFade.z);
            if (cascade_index == _DirectionalShadowCascadeCount - 1)
            {
                out_data.strength *= cascade_fade;
            }
            else
            {
                out_data.cascade_blend = cascade_fade;
            }

            out_data.cascade_index = cascade_index;
            break;
        }
    }
    out_data.strength = out_data.cascade_index == -1 ? 0.0f : out_data.strength;

    return out_data;
}

float2 ClampDirectionalShadowUV(float2 shadow_uv, int cascade_index)
{
    float2 half_texel = 0.5f * _DirectionalShadowAtlasTexelSize.xy;
    float tile_height = rcp((float)_DirectionalShadowCascadeCount);
    float tile_min_y  = tile_height * cascade_index;
    float tile_max_y  = tile_min_y + tile_height;

    float2 min_uv = float2(half_texel.x, tile_min_y + half_texel.y);
    float2 max_uv = float2(1.0f - half_texel.x, tile_max_y - half_texel.y);
    return clamp(shadow_uv, min_uv, max_uv);
}

float SampleDirectionalShadowAtlas(float3 shadow_uv, int cascade_index)
{
    shadow_uv.xy = ClampDirectionalShadowUV(shadow_uv.xy, cascade_index);
    return 1.0f - SAMPLE_TEXTURE2D_SHADOW(_DirectionalShadowAtlas, SHADOW_SAMPLER, shadow_uv);
}

float FilterDirectionalShadowAtlas(float3 shadow_uv, int cascade_index)
{
#if defined(DIRECTIONAL_FILTER_SETUP)
    real weights[DIRECTIONAL_FILTER_SAMPLES];
    real2 positions[DIRECTIONAL_FILTER_SAMPLES];
    DIRECTIONAL_FILTER_SETUP(_DirectionalShadowAtlasTexelSize, shadow_uv.xy, weights, positions);

    float shadow = 0.0f;
    for (int i = 0; i < DIRECTIONAL_FILTER_SAMPLES; i++)
    {
        shadow += weights[i] * SampleDirectionalShadowAtlas(float3(positions[i], shadow_uv.z), cascade_index);
    }
    return shadow;
#else
    return SampleDirectionalShadowAtlas(shadow_uv, cascade_index);
#endif
}

float GetCascadeShadow(int cascade_index, float3 position_WS)
{
    float3 shadow_uv = mul(_DirectionalShadowVPMatrices[cascade_index], float4(position_WS, 1.0));
    if (shadow_uv.z > 0.0f && shadow_uv.z < 1.0f)
    {
        return FilterDirectionalShadowAtlas(shadow_uv, cascade_index);
    }
    return 0.0f;
}

float GetCascadedShadow(DirectionalLightShadowData light_shadow_data, ShadowData fragment_shadow_data,
                        float3 position_WS)
{
    float shadow = 0.0f;

    int cascade_index     = fragment_shadow_data.cascade_index;
    float shadow_strength = fragment_shadow_data.strength;
    if (shadow_strength < 0.001f)
    {
        return shadow;
    }

    shadow = GetCascadeShadow(cascade_index, position_WS);

    if (fragment_shadow_data.cascade_blend < 0.999f && cascade_index + 1 < _DirectionalShadowCascadeCount)
    {
        float next_shadow = GetCascadeShadow(cascade_index + 1, position_WS);
        shadow            = lerp(next_shadow, shadow, fragment_shadow_data.cascade_blend);
    }

    shadow *= shadow_strength;

    return shadow;
}

float GetDirectionalShadowAttenuation(DirectionalLightShadowData light_shadow_data, ShadowData fragment_shadow_data,
                                      float3 position_WS)
{
    float shadow = 0.0;

    shadow = GetCascadedShadow(light_shadow_data, fragment_shadow_data, position_WS);

    return shadow;
}

float4 ShadowMaskPassFragment(FullScreenVaryings input) : SV_Target
{
    float scene_depth  = SAMPLE_TEXTURE2D(_SceneDepth, sampler_SceneDepth, input.uv).r;
    float3 position_WS = ComputeWorldSpacePositionFromFullScreenUV(input.uv, scene_depth);
    float linear_depth = LinearEyeDepth(position_WS, UNITY_MATRIX_V);

    float directional_shadow = GetDirectionalShadowAttenuation(GetDirectionalLightShadowData(0),
                                                               GetShadowData(position_WS, linear_depth),
                                                               position_WS);

    return float4(1.0f - directional_shadow, 1.0, 1.0, 1.0);
}

#endif
