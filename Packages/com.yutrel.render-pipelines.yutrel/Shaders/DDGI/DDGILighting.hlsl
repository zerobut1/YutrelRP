#ifndef YUTREL_DDGI_LIGHTING_INCLUDED
#define YUTREL_DDGI_LIGHTING_INCLUDED

#include "../Utils/ShadingModelOpenPBR.hlsl"
#include "../Utils/ShadingModelStandard.hlsl"
#include "DDGICommon.hlsl"

Texture2DArray<float4> _DDGIProbeIrradiance;
Texture2DArray<float2> _DDGIProbeDistance;
Texture2DArray<float4> _DDGIProbeData;

float _DDGIProbeNormalBias;
float _DDGIProbeViewBias;
float _DDGILightingIntensityScale;
float _DDGIIrradianceEncodingGamma;

float3 DDGILightingSafeNormalize(float3 value)
{
    float lengthSqr = dot(value, value);
    return lengthSqr > 1.0e-10f ? value * rsqrt(lengthSqr) : float3(0.0f, 1.0f, 0.0f);
}

int3 DDGILightingGetBaseProbeGridCoords(float3 worldPosition)
{
    float3 gridCoords = (worldPosition - _DDGIProbeBoundsMin) / max(_DDGIProbeSpacing, 1.0e-6f);
    int3 maxBase      = max(DDGIProbeCount() - int3(1, 1, 1), int3(0, 0, 0));
    return clamp((int3)floor(gridCoords), int3(0, 0, 0), maxBase);
}

float DDGILightingGetVolumeBlendWeight(float3 worldPosition)
{
    int3 probeCount = DDGIProbeCount();
    float3 extent   = _DDGIProbeSpacing * (float3)(probeCount - int3(1, 1, 1)) * 0.5f;
    float3 origin   = _DDGIProbeBoundsMin + extent;
    float3 delta    = abs(worldPosition - origin) - extent;
    if (all(delta < 0.0f))
    {
        return 1.0f;
    }

    float3 weight = 1.0f - saturate(delta / max(_DDGIProbeSpacing, 1.0e-6f));
    return weight.x * weight.y * weight.z;
}

float3 DDGILightingGetVolumeIrradiance(float3 worldPosition, float3 surfaceBias, float3 direction)
{
    float3 irradiance             = 0.0f;
    float accumulatedWeights      = 0.0f;
    float3 biasedWorldPosition    = worldPosition + surfaceBias;
    int3 baseProbeCoords          = DDGILightingGetBaseProbeGridCoords(biasedWorldPosition);
    float3 baseProbeWorldPosition = DDGIProbeBaseWorldPosition(baseProbeCoords);
    float3 gridSpaceDistance      = biasedWorldPosition - baseProbeWorldPosition;
    float3 alpha                  = saturate(gridSpaceDistance / max(_DDGIProbeSpacing, 1.0e-6f));

    for (int probeIndex = 0; probeIndex < 8; probeIndex++)
    {
        int3 adjacentProbeOffset = int3(probeIndex, probeIndex >> 1, probeIndex >> 2) & int3(1, 1, 1);
        int3 adjacentProbeCoords = clamp(
            baseProbeCoords + adjacentProbeOffset,
            int3(0, 0, 0),
            DDGIProbeCount() - int3(1, 1, 1));
        int adjacentProbeIndex = DDGIProbeIndex(adjacentProbeCoords);
        if (_DDGIProbeClassificationEnabled != 0 &&
            DDGILoadProbeState(_DDGIProbeData, adjacentProbeCoords) == DDGI_PROBE_STATE_INACTIVE)
        {
            continue;
        }

        float3 adjacentProbeWorldPosition = DDGIProbeWorldPosition(_DDGIProbeData, adjacentProbeCoords);

        float3 worldPosToAdjProbe     = DDGILightingSafeNormalize(adjacentProbeWorldPosition - worldPosition);
        float3 biasedPosToAdjProbe    = DDGILightingSafeNormalize(adjacentProbeWorldPosition - biasedWorldPosition);
        float biasedPosToAdjProbeDist = length(adjacentProbeWorldPosition - biasedWorldPosition);
        float3 trilinear              = max(0.001f, lerp(1.0f - alpha, alpha, (float3)adjacentProbeOffset));
        float trilinearWeight         = trilinear.x * trilinear.y * trilinear.z;
        float weight                  = 1.0f;

        float wrapShading = (dot(worldPosToAdjProbe, direction) + 1.0f) * 0.5f;
        weight *= (wrapShading * wrapShading) + 0.2f;

        float2 octantCoordinates = DDGIOctEncode(-biasedPosToAdjProbe);
        float3 probeTextureUV    = DDGIProbeAtlasUV(adjacentProbeIndex, octantCoordinates, 14);
        float2 filteredDistance  = 2.0f * _DDGIProbeDistance.SampleLevel(sampler_linear_clamp, probeTextureUV, 0).rg;
        float variance           = abs(filteredDistance.x * filteredDistance.x - filteredDistance.y);

        float chebyshevWeight = 1.0f;
        if (biasedPosToAdjProbeDist > filteredDistance.x)
        {
            float v         = biasedPosToAdjProbeDist - filteredDistance.x;
            chebyshevWeight = variance / max(variance + v * v, 1.0e-6f);
            chebyshevWeight = max(chebyshevWeight * chebyshevWeight * chebyshevWeight, 0.0f);
        }

        weight *= max(0.05f, chebyshevWeight);
        weight = max(0.000001f, weight);

        const float crushThreshold = 0.2f;
        if (weight < crushThreshold)
        {
            weight *= (weight * weight) / (crushThreshold * crushThreshold);
        }

        weight *= trilinearWeight;

        octantCoordinates      = DDGIOctEncode(direction);
        probeTextureUV         = DDGIProbeAtlasUV(adjacentProbeIndex, octantCoordinates, 6);
        float3 probeIrradiance = _DDGIProbeIrradiance.SampleLevel(sampler_linear_clamp, probeTextureUV, 0).rgb;
        probeIrradiance        = pow(max(probeIrradiance, 0.0f), _DDGIIrradianceEncodingGamma * 0.5f);

        irradiance += weight * probeIrradiance;
        accumulatedWeights += weight;
    }

    if (accumulatedWeights <= 0.0f)
    {
        return float3(0.0f, 0.0f, 0.0f);
    }

    irradiance *= rcp(accumulatedWeights);
    irradiance *= irradiance;
    irradiance *= TWO_PI;
    irradiance *= 1.0989f;
    irradiance *= _DDGILightingIntensityScale;
    return irradiance;
}

float3 EvaluateDDGIDiffuseLightingForSurface(
    float3 position_WS,
    float3 normal_WS,
    float3 view_direction_WS)
{
    float volumeBlendWeight = DDGILightingGetVolumeBlendWeight(position_WS);
    if (volumeBlendWeight <= 0.0f)
    {
        return 0.0f;
    }

    float3 surfaceBias = normal_WS * _DDGIProbeNormalBias + view_direction_WS * _DDGIProbeViewBias;
    float3 irradiance  = DDGILightingGetVolumeIrradiance(position_WS, surfaceBias, normal_WS);
    return irradiance * INV_PI * volumeBlendWeight;
}

float3 EvaluateDDGIDiffuseLighting(StandardSurface surface)
{
    return EvaluateDDGIDiffuseLightingForSurface(
        surface.position_WS,
        surface.normal_WS,
        surface.view_direction_WS);
}

float3 EvaluateDDGIDiffuseLighting(OpenPBRSurface surface)
{
    return EvaluateDDGIDiffuseLightingForSurface(
        surface.position_WS,
        surface.normal_WS,
        surface.view_direction_WS);
}

#endif
