#ifndef YUTREL_LIGHT_INCLUDED
#define YUTREL_LIGHT_INCLUDED

CBUFFER_START(_YutrelLight)
    int _DirectionalLightCount;
CBUFFER_END

struct DirectionalLightData
{
    float3 color;
    float intensity;
    float4 direction;
};

StructuredBuffer<DirectionalLightData> _DirectionalLightData;

TEXTURE2D(_BRDF_LUT);
SAMPLER(sampler_BRDF_LUT);

struct Light
{
    float3 color;
    float intensity;
    float3 direction;
};

Light GetDirectionalLight(int index)
{
    DirectionalLightData data = _DirectionalLightData[index];
    Light light;
    light.color = data.color;
    light.intensity = data.intensity;
    light.direction = normalize(data.direction.xyz);
    return light;
}

#endif
