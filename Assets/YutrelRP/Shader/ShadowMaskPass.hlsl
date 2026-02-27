#ifndef YUTREL_SHADOW_MASK_PASS_INCLUDED
#define YUTREL_SHADOW_MASK_PASS_INCLUDED

#include "Utils/Shadow.hlsl"
#include "Utils/GBuffer.hlsl"
#include "Utils/Light.hlsl"

struct ShadowData
{
    int cascade_index;
    float strength;
};

float FadedShadowStrength(float distance, float scale, float fade)
{
    return saturate((1.0f - distance * scale) * fade);
}

ShadowData GetShadowData(float3 position_WS, float depth)
{
    ShadowData out_data;
    out_data.cascade_index = -1;
    out_data.strength = FadedShadowStrength(depth, _DirectionalShadowDistanceFade.x, _DirectionalShadowDistanceFade.y);

    for (int cascade_index = 0; cascade_index < _DirectionalShadowCascadeCount; cascade_index++)
    {
        float4 sphere = _DirectionalShadowCascadeDatas[cascade_index].culling_sphere;
        float dist = distance(position_WS, sphere.xyz);
        if (dist < sphere.w)
        {
            if (cascade_index == _DirectionalShadowCascadeCount - 1)
            {
                out_data.strength *= FadedShadowStrength(dist, 1.0f / sphere.w, _DirectionalShadowDistanceFade.z);
            }

            out_data.cascade_index = cascade_index;
            break;
        }
    }
    out_data.strength = out_data.cascade_index == -1 ? 0.0f : out_data.strength;

    return out_data;
}

float SampleDirectionalShadowAtlas(float3 shadow_uv)
{
    return 1.0f - SAMPLE_TEXTURE2D_SHADOW(_DirectionalShadowAtlas, SHADOW_SAMPLER, shadow_uv);
}

float GetCascadedShadow(DirectionalLightShadowData light_shadow_data, ShadowData fragment_shadow_data,
                        float3 position_WS)
{
    float shadow = 0.0f;

    int cascade_index = fragment_shadow_data.cascade_index;
    float shadow_strength = fragment_shadow_data.strength;
    if (shadow_strength < 0.001f)
    {
        return shadow;
    }

    float3 shadow_uv = mul(_DirectionalShadowVPMatrices[cascade_index], float4(position_WS, 1.0));
    if (shadow_uv.z > 0 && shadow_uv.z < 1)
    {
        shadow = SampleDirectionalShadowAtlas(shadow_uv);
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
    float scene_depth = SAMPLE_TEXTURE2D(_SceneDepth, sampler_SceneDepth, input.uv).r;
    float3 position_WS = ComputeWorldSpacePosition(input.uv, scene_depth, UNITY_MATRIX_I_VP);
    float linear_depth = LinearEyeDepth(position_WS,UNITY_MATRIX_V);

    float directional_shadow = GetDirectionalShadowAttenuation(GetDirectionalLightShadowData(0),
                                                               GetShadowData(position_WS, linear_depth),
                                                               position_WS);

    return float4(1.0f - directional_shadow, 1.0, 1.0, 1.0);
}

#endif
