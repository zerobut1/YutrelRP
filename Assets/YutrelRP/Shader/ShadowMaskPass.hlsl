#ifndef YUTREL_SHADOW_MASK_PASS_INCLUDED
#define YUTREL_SHADOW_MASK_PASS_INCLUDED

#include "Utils/Shadow.hlsl"
#include "Utils/GBuffer.hlsl"
#include "Utils/Light.hlsl"

struct Varyings
{
    float4 positionCS : SV_POSITION;
    float2 uv : VAR_SCREEN_UV;
};

Varyings ShadowMaskPassVertex(uint vertexID : SV_VertexID)
{
    Varyings output;
    output.positionCS = float4(
        vertexID <= 1 ? -1.0 : 3.0,
        vertexID == 1 ? 3.0 : -1.0,
        0.0,
        1.0
    );
    output.uv = float2(
        vertexID <= 1 ? 0.0 : 2.0,
        vertexID == 1 ? 2.0 : 0.0
    );
    if (_ProjectionParams.x < 0.0)
    {
        output.uv.y = 1.0 - output.uv.y;
    }
    return output;
}

float SampleDirectioanalShadowAtlas(float3 shadow_uv)
{
    return SAMPLE_TEXTURE2D_ARRAY_SHADOW(_DirectionalShadowAtlas, SHADOW_SAMPLER, shadow_uv, 0);
}

float GetCascadedShadow(DirectionalLightShadowData light_shadow_data, float3 position_WS)
{
    float3 shadow_uv = mul(_DirectionalShadowVPMatrices[light_shadow_data.index], float4(position_WS, 1.0));
    float shadow = 1.0f;
    if (shadow_uv.z > 0 && shadow_uv.z < 1)
    {
        shadow = SampleDirectioanalShadowAtlas(shadow_uv);
    }

    return shadow;
}

float GetDirectionalShadowAttenuation(DirectionalLightShadowData light_shadow_data, float3 position_WS)
{
    float shadow = 1.0;

    shadow = GetCascadedShadow(light_shadow_data, position_WS);

    return shadow;
}

float4 ShadowMaskPassFragment(Varyings input) : SV_TARGET
{
    float scene_depth = tex2D(_SceneDepth, input.uv).r;
    float3 position_WS = ComputeWorldSpacePosition(input.uv, scene_depth, UNITY_MATRIX_I_VP);

    float directional_shadow = GetDirectionalShadowAttenuation(GetDirectionalLightShadowData(0), position_WS);

    return float4(directional_shadow, 1.0, 1.0, 1.0);
}

#endif
