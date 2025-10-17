#ifndef YUTREL_LIGHTING_INCLUDED
#define YUTREL_LIGHTING_INCLUDED

#include "Surface.hlsl"
#include "Light.hlsl"

float3 IncomingLight(Surface surface, DirectionalLight light)
{
    return saturate(dot(surface.normal, light.direction)) * light.color;
}

float3 GetLighting(Surface surface, DirectionalLight light)
{
    return IncomingLight(surface, light) * surface.base_color;
}

#endif
