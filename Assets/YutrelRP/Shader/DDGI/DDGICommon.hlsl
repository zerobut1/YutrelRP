#ifndef YUTREL_DDGI_COMMON_INCLUDED
#define YUTREL_DDGI_COMMON_INCLUDED

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Macros.hlsl"

float4 _DDGIProbeRayRotationRow0;
float4 _DDGIProbeRayRotationRow1;
float4 _DDGIProbeRayRotationRow2;

float3 DDGISphericalFibonacci(uint sampleIndex, uint sampleCount)
{
    const float b  = 0.61803398874989484820f;
    float phi      = TWO_PI * frac((float)sampleIndex * b);
    float cosTheta = 1.0f - (2.0f * (float)sampleIndex + 1.0f) / (float)max(sampleCount, 1u);
    float sinTheta = sqrt(saturate(1.0f - cosTheta * cosTheta));
    return float3(cos(phi) * sinTheta, cosTheta, sin(phi) * sinTheta);
}

float3 DDGIRotateProbeRayDirection(float3 direction)
{
    return normalize(float3(
        dot(direction, float3(_DDGIProbeRayRotationRow0.x, _DDGIProbeRayRotationRow1.x, _DDGIProbeRayRotationRow2.x)),
        dot(direction, float3(_DDGIProbeRayRotationRow0.y, _DDGIProbeRayRotationRow1.y, _DDGIProbeRayRotationRow2.y)),
        dot(direction, float3(_DDGIProbeRayRotationRow0.z, _DDGIProbeRayRotationRow1.z, _DDGIProbeRayRotationRow2.z))));
}

float3 DDGIGetProbeRayDirection(uint rayIndex, uint rayCount)
{
    return DDGIRotateProbeRayDirection(DDGISphericalFibonacci(rayIndex, rayCount));
}

uint DDGIPackRadiance01(float3 value)
{
    float3 radiance = saturate(value);
    return (uint)round(radiance.r * 1023.0f) |
           ((uint)round(radiance.g * 1023.0f) << 10) |
           ((uint)round(radiance.b * 1023.0f) << 20);
}

float3 DDGIUnpackRadiance01(uint packedRadiance)
{
    return float3(
        (float)(packedRadiance & 0x000003FFu) / 1023.0f,
        (float)((packedRadiance >> 10) & 0x000003FFu) / 1023.0f,
        (float)((packedRadiance >> 20) & 0x000003FFu) / 1023.0f);
}

float2 DDGISignNotZero(float2 value)
{
    return float2(value.x >= 0.0f ? 1.0f : -1.0f, value.y >= 0.0f ? 1.0f : -1.0f);
}

float3 DDGIOctDecode(float2 coords)
{
    float3 direction = float3(coords.x, coords.y, 1.0f - abs(coords.x) - abs(coords.y));
    if (direction.z < 0.0f)
    {
        direction.xy = (1.0f - abs(direction.yx)) * DDGISignNotZero(direction.xy);
    }
    return normalize(direction);
}

float2 DDGIOctEncode(float3 direction)
{
    float l1norm = abs(direction.x) + abs(direction.y) + abs(direction.z);
    float2 uv    = direction.xy * rcp(max(l1norm, 1.0e-6f));
    if (direction.z < 0.0f)
    {
        uv = (1.0f - abs(uv.yx)) * DDGISignNotZero(uv.xy);
    }
    return uv;
}

float2 DDGIGetNormalizedOctahedralCoordinates(int2 texCoords, int numTexels)
{
    float2 octahedralTexelCoord = float2(texCoords);
    octahedralTexelCoord += 0.5f;
    octahedralTexelCoord /= (float)max(numTexels, 1);
    return octahedralTexelCoord * 2.0f - 1.0f;
}

#endif
