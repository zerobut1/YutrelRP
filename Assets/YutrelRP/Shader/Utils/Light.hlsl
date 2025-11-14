#ifndef YUTREL_LIGHT_INCLUDED
#define YUTREL_LIGHT_INCLUDED

#include "Shadow.hlsl"

CBUFFER_START(_YutrelLight)
    int _DirectionalLightCount;
CBUFFER_END

struct DirectionalLightData
{
    float3 color;
    float intensity;
    float4 direction;
    DirectionalLightShadowData shadow_data;
};

StructuredBuffer<DirectionalLightData> _DirectionalLightData;

TEXTURE2D(_BRDF_LUT);
SAMPLER(sampler_BRDF_LUT);

TEXTURE2D(_ShadowMask);
SAMPLER(sampler_ShadowMask);

struct Light
{
    float3 color;
    float intensity;
    float3 direction;
    float attenuation;
};

Light GetDirectionalLight(int index, float2 uv)
{
    DirectionalLightData data = _DirectionalLightData[index];
    Light light;
    light.color = data.color;
    light.intensity = data.intensity;
    light.direction = normalize(data.direction.xyz);
    light.attenuation = 1.0f;

    if (data.shadow_data.index >= 0)
    {
        light.attenuation = saturate(SAMPLE_TEXTURE2D(_ShadowMask, sampler_ShadowMask, uv).r);
    }
    return light;
}

DirectionalLightShadowData GetDirectionalLightShadowData(int index)
{
    DirectionalLightData data = _DirectionalLightData[index];
    return data.shadow_data;
}

#endif
