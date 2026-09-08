#ifndef YUTREL_SHADOW_MASK_PASS_INCLUDED
#define YUTREL_SHADOW_MASK_PASS_INCLUDED

#include "Utils/GBuffer.hlsl"
#include "Utils/Light.hlsl"
#include "Utils/ShadowSampling.hlsl"

float4 ShadowMaskPassFragment(FullScreenVaryings input) : SV_Target
{
    float scene_depth  = SAMPLE_TEXTURE2D(_SceneDepth, sampler_SceneDepth, input.uv).r;
    float3 normal_WS   = normalize(SAMPLE_TEXTURE2D(_GBuffer_B, sampler_GBuffer_B, input.uv).xyz * 2.0f - 1.0f);
    float3 position_WS = ComputeWorldSpacePositionFromFullScreenUV(input.uv, scene_depth);
    float visibility   = EvaluateCsmShadowVisibility(GetDirectionalLightShadowData(0), position_WS, normal_WS);
    return float4(visibility, 1.0, 1.0, 1.0);
}

#endif
