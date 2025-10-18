#ifndef YUTREL_LIGHTING_INCLUDED
#define YUTREL_LIGHTING_INCLUDED

#include "Surface.hlsl"
#include "Light.hlsl"

float3 IncomingLight(Surface surface, Light light)
{
    return saturate(dot(surface.normal, light.direction)) * light.color;
}

float3 GetLighting(Surface surface, Light light)
{
    return IncomingLight(surface, light) * surface.base_color;
}

float3 GetLighting(Surface surface)
{
    float3 out_color = float3(0, 0, 0);
    for (int i = 0; i < _DirectionalLightCount; i++)
    {
        Light light = GetDirectionalLight(i);
        out_color += GetLighting(surface, light);
    }
    
    return out_color;
}

#endif
