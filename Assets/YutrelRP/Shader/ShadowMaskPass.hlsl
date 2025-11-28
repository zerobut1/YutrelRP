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

ShadowData GetShadowData(float3 position_WS, float depth)
{
    ShadowData out_data;
    out_data.cascade_index = -1;
    out_data.strength = depth < _DirectionalShadowDistance ? 1.0f : 0.0f;

    for (int cascade_index = 0; cascade_index < _DirectionalShadowCascadeCount; cascade_index++)
    {
        float4 sphere = _DirectionalShadowCascadeDatas[cascade_index].culling_sphere;
        float dist = distance(position_WS, sphere.xyz);
        if (dist < sphere.w)
        {
            out_data.cascade_index = cascade_index;
            break;
        }
    }
    out_data.strength = out_data.cascade_index == -1 ? 0.0f : out_data.strength;

    return out_data;
}

float SampleDirectioanalShadowAtlas(float3 shadow_uv)
{
    return SAMPLE_TEXTURE2D_SHADOW(_DirectionalShadowAtlas, SHADOW_SAMPLER, shadow_uv);
}

float GetCascadedShadow(DirectionalLightShadowData light_shadow_data, ShadowData fragment_shadow_data,
                        float3 position_WS)
{
    int cascade_index = fragment_shadow_data.cascade_index;
    float shadow_strength = fragment_shadow_data.strength;
    if (shadow_strength < 0.001f)
    {
        return 1.0f;
    }
    float3 shadow_uv = mul(_DirectionalShadowVPMatrices[cascade_index], float4(position_WS, 1.0));
    float shadow = 1.0f;
    if (shadow_uv.z > 0 && shadow_uv.z < 1)
    {
        shadow = SampleDirectioanalShadowAtlas(shadow_uv);
    }
    
    return shadow;
}

float GetDirectionalShadowAttenuation(DirectionalLightShadowData light_shadow_data, ShadowData fragment_shadow_data,
                                      float3 position_WS)
{
    float shadow = 1.0;

    shadow = GetCascadedShadow(light_shadow_data, fragment_shadow_data, position_WS);

    return shadow;
}

float4 ShadowMaskPassFragment(FullScreenVaryings input) : SV_TARGET
{
    float scene_depth = tex2D(_SceneDepth, input.uv).r;
    float3 position_WS = ComputeWorldSpacePosition(input.uv, scene_depth, UNITY_MATRIX_I_VP);

    float directional_shadow = GetDirectionalShadowAttenuation(GetDirectionalLightShadowData(0),
                                                               GetShadowData(position_WS, scene_depth),
                                                               position_WS);

    return float4(directional_shadow, 1.0, 1.0, 1.0);
}

#endif
