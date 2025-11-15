#ifndef YUTREL_SHADOW_INCLUDED
#define YUTREL_SHADOW_INCLUDED

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Shadow/ShadowSamplingTent.hlsl"

struct DirectionalLightShadowData
{
    int index;
    float3 _padding;
};

TEXTURE2D_SHADOW(_DirectionalShadowAtlas);
#define SHADOW_SAMPLER sampler_DirectionalShadowAtlas
SAMPLER_CMP(SHADOW_SAMPLER);

StructuredBuffer<float4x4> _DirectionalShadowVPMatrices;

#endif
