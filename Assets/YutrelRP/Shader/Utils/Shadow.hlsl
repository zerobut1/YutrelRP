#ifndef YUTREL_SHADOW_INCLUDED
#define YUTREL_SHADOW_INCLUDED

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Shadow/ShadowSamplingTent.hlsl"

struct DirectionalLightShadowData
{
    int index;
    float3 _padding;
};

TEXTURE2D_SHADOW(_DirectionalShadowAtlas);
#define SHADOW_SAMPLER sampler_linear_clamp_compare
SAMPLER_CMP(SHADOW_SAMPLER);

CBUFFER_START(_YutrelShadows)
    int _DirectionalShadowCascadeCount;
    float _DirectionalShadowDistance;
CBUFFER_END

StructuredBuffer<float4x4> _DirectionalShadowVPMatrices;

struct DirectionalShadowCascadeData
{
    float4 culling_sphere;
};

StructuredBuffer<DirectionalShadowCascadeData> _DirectionalShadowCascadeDatas;

#endif
