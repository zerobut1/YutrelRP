#ifndef YUTREL_LIGHT_INCLUDED
#define YUTREL_LIGHT_INCLUDED

CBUFFER_START(_YutrelLight)
    int _DirectionalLightCount;
CBUFFER_END

struct DirectionalLightData
{
    float4 color;
    float4 direction;
};

StructuredBuffer<DirectionalLightData> _DirectionalLightData;

struct Light
{
    float3 color;
    float3 direction;
};

Light GetDirectionalLight(int index)
{
    DirectionalLightData data = _DirectionalLightData[index];
    Light light;
    light.color = data.color.rgb;
    light.direction = normalize(data.direction.xyz);
    return light;
}

#endif
