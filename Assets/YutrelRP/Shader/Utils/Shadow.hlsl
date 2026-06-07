#ifndef YUTREL_SHADOW_INCLUDED
#define YUTREL_SHADOW_INCLUDED

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Shadow/ShadowSamplingTent.hlsl"

struct DirectionalLightShadowData
{
    float index;
    float soft_shadow;
    float strength;
    float normal_bias;
};

TEXTURE2D_SHADOW(_DirectionalShadowAtlas);
#define SHADOW_SAMPLER sampler_linear_clamp_compare
SAMPLER_CMP(SHADOW_SAMPLER);

CBUFFER_START(_YutrelShadows)
int _DirectionalShadowCascadeCount;
float4 _DirectionalShadowDistanceFade;
float4 _DirectionalShadowAtlasTexelSize;
CBUFFER_END

StructuredBuffer<float4x4> _DirectionalShadowVPMatrices;

struct DirectionalShadowCascadeData
{
    float4 culling_sphere;
    float4 data;
};

StructuredBuffer<DirectionalShadowCascadeData> _DirectionalShadowCascadeDatas;

#endif
