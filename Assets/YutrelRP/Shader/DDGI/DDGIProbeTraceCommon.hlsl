#ifndef YUTREL_DDGI_PROBE_TRACE_COMMON_INCLUDED
#define YUTREL_DDGI_PROBE_TRACE_COMMON_INCLUDED

static const uint DDGI_TRACE_HIT_KIND_MISS       = 0u;
static const uint DDGI_TRACE_HIT_KIND_FRONT_FACE = 1u;
static const uint DDGI_TRACE_HIT_KIND_BACK_FACE  = 2u;

struct DDGIProbeTracePayload
{
    uint hitKind;
    float rayT;
    float3 positionWS;
    float3 normalWS;
    float3 shadingNormalWS;
    float3 baseColor;
};

float3 DDGITraceSafeNormalize(float3 value, float3 fallback)
{
    float lengthSq = dot(value, value);
    return lengthSq > 1.0e-10f ? value * rsqrt(lengthSq) : fallback;
}

float3 DDGITraceFallbackNormalWS()
{
    return DDGITraceSafeNormalize(-WorldRayDirection(), float3(0.0f, 1.0f, 0.0f));
}

float3 DDGITraceKeepSameHemisphere(float3 normalWS, float3 referenceNormalWS)
{
    float3 referenceNormal = DDGITraceSafeNormalize(referenceNormalWS, DDGITraceFallbackNormalWS());
    float3 normal          = DDGITraceSafeNormalize(normalWS, referenceNormal);
    return dot(normal, referenceNormal) >= 0.0f ? normal : referenceNormal;
}

float3 DDGITraceOrientNormal(float3 normalWS, float3 referenceNormalWS)
{
    float3 referenceNormal = DDGITraceSafeNormalize(referenceNormalWS, DDGITraceFallbackNormalWS());
    float3 normal          = DDGITraceSafeNormalize(normalWS, referenceNormal);
    return dot(normal, referenceNormal) >= 0.0f ? normal : -normal;
}

float3 DDGITraceOffsetRayOrigin(float3 positionWS, float3 normalWS, float bias)
{
    return positionWS + normalWS * max(bias, 0.0f);
}

void DDGITraceCommitClosestHit(
    inout DDGIProbeTracePayload payload,
    float3 baseColor,
    float3 positionWS,
    float3 normalWS,
    float3 shadingNormalWS)
{
    payload.hitKind         = HitKind() == HIT_KIND_TRIANGLE_FRONT_FACE ? DDGI_TRACE_HIT_KIND_FRONT_FACE : DDGI_TRACE_HIT_KIND_BACK_FACE;
    payload.rayT            = RayTCurrent();
    payload.positionWS      = positionWS;
    payload.normalWS        = DDGITraceSafeNormalize(normalWS, DDGITraceFallbackNormalWS());
    payload.shadingNormalWS = DDGITraceKeepSameHemisphere(shadingNormalWS, payload.normalWS);
    payload.baseColor       = max(baseColor, 0.0f);
}

#if defined(YUTREL_DDGI_PROBE_TRACE_RADIANCE_COMMON)

#include "DDGICommon.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/EntityLighting.hlsl"

#define DDGI_RAY_MASK 0xFFu

static const float DDGI_DIRECTIONAL_SHADOW_RAY_TMAX        = 1.0e27f;
static const float DDGI_DIRECTIONAL_SHADOW_RAY_NORMAL_BIAS = 1.0e-4f;

struct DirectionalLightData
{
    float3 color;
    float illuminance;
    float4 direction;
    float4 shadow_data;
};

StructuredBuffer<DirectionalLightData> _DirectionalLightData;
Texture2DArray<float4> _DDGIProbeIrradiance;
Texture2DArray<float2> _DDGIProbeDistance;
Texture2DArray<float4> _DDGIProbeData;
TextureCube<float4> _DDGIEnvironmentCube;
RaytracingAccelerationStructure _DDGIAccelerationStructure;
SamplerState sampler_linear_clamp;

int _DirectionalLightCount;
int _DDGIEnvironmentEnabled;
float4 _DDGIEnvironmentCube_HDR;
float _EnvironmentIntensity;
float _EnvironmentDiffuseMultiplier;
float _DDGIProbeMaxRayDistance;
float _DDGILightingIntensityScale;
float _DDGIProbeNormalBias;
float _DDGIProbeViewBias;
float _DDGIIrradianceEncodingGamma;

float DDGITraceLightingIntensityInvScale()
{
    return rcp(max(_DDGILightingIntensityScale, 1.0e-6f));
}

float3 DDGITraceEnvironmentRadiance(float3 directionWS)
{
    if (_DDGIEnvironmentEnabled == 0)
    {
        return float3(0.0f, 0.0f, 0.0f);
    }

    float4 encodedEnvironment = _DDGIEnvironmentCube.SampleLevel(
        sampler_linear_clamp,
        normalize(directionWS),
        0.0f);
    float3 radiance = DecodeHDREnvironment(encodedEnvironment, _DDGIEnvironmentCube_HDR);
    return max(radiance, 0.0f) * (_EnvironmentIntensity * DDGITraceLightingIntensityInvScale()) *
           _EnvironmentDiffuseMultiplier;
}

float3 DDGITraceNormalize(float3 value)
{
    float lengthSqr = dot(value, value);
    return lengthSqr > 1.0e-10f ? value * rsqrt(lengthSqr) : float3(0.0f, 1.0f, 0.0f);
}

int3 DDGITraceGetBaseProbeGridCoords(float3 worldPosition)
{
    float3 gridCoords = (worldPosition - _DDGIProbeBoundsMin) / max(_DDGIProbeSpacing, 1.0e-6f);
    int3 maxBase      = max(DDGIProbeCount() - int3(1, 1, 1), int3(0, 0, 0));
    return clamp((int3)floor(gridCoords), int3(0, 0, 0), maxBase);
}

float DDGITraceGetVolumeBlendWeight(float3 worldPosition)
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

float3 DDGITraceGetVolumeIrradiance(float3 worldPosition, float3 surfaceBias, float3 direction)
{
    float3 irradiance             = 0.0f;
    float accumulatedWeights      = 0.0f;
    float3 biasedWorldPosition    = worldPosition + surfaceBias;
    int3 baseProbeCoords          = DDGITraceGetBaseProbeGridCoords(biasedWorldPosition);
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

        float3 worldPosToAdjProbe     = DDGITraceNormalize(adjacentProbeWorldPosition - worldPosition);
        float3 biasedPosToAdjProbe    = DDGITraceNormalize(adjacentProbeWorldPosition - biasedWorldPosition);
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
    return irradiance;
}

void DDGITraceInitPayload(out DDGIProbeTracePayload payload)
{
    payload.hitKind         = DDGI_TRACE_HIT_KIND_MISS;
    payload.rayT            = -1.0f;
    payload.positionWS      = 0.0f;
    payload.normalWS        = float3(0.0f, 1.0f, 0.0f);
    payload.shadingNormalWS = payload.normalWS;
    payload.baseColor       = 0.0f;
}

float DDGITraceDirectionalVisibility(float3 hitPositionWS, float3 normalWS, float3 lightDirectionWS)
{
    RayDesc shadowRay;
    shadowRay.Origin    = DDGITraceOffsetRayOrigin(hitPositionWS, normalWS, DDGI_DIRECTIONAL_SHADOW_RAY_NORMAL_BIAS);
    shadowRay.TMin      = 0.001f;
    shadowRay.Direction = lightDirectionWS;
    shadowRay.TMax      = DDGI_DIRECTIONAL_SHADOW_RAY_TMAX;

    DDGIProbeTracePayload shadowPayload;
    DDGITraceInitPayload(shadowPayload);
    shadowPayload.hitKind = DDGI_TRACE_HIT_KIND_FRONT_FACE;

    TraceRay(
        _DDGIAccelerationStructure,
        RAY_FLAG_ACCEPT_FIRST_HIT_AND_END_SEARCH | RAY_FLAG_SKIP_CLOSEST_HIT_SHADER,
        DDGI_RAY_MASK,
        0,
        1,
        0,
        shadowRay,
        shadowPayload);

    return shadowPayload.hitKind == DDGI_TRACE_HIT_KIND_MISS ? 1.0f : 0.0f;
}

float3 DDGITraceEvaluateDirectRadiance(DDGIProbeTracePayload payload)
{
    if (_DirectionalLightCount <= 0)
    {
        return float3(0.0f, 0.0f, 0.0f);
    }

    DirectionalLightData light = _DirectionalLightData[0];
    float3 lightDirectionWS    = normalize(light.direction.xyz);
    float noL                  = saturate(dot(payload.shadingNormalWS, lightDirectionWS));
    if (noL <= 0.0f)
    {
        return float3(0.0f, 0.0f, 0.0f);
    }

    float visibility  = DDGITraceDirectionalVisibility(payload.positionWS, payload.normalWS, lightDirectionWS);
    float illuminance = light.illuminance * DDGITraceLightingIntensityInvScale();
    return payload.baseColor * light.color * illuminance * noL * visibility * INV_PI;
}

float3 DDGITraceEvaluateDirectRadiance01(DDGIProbeTracePayload payload)
{
    return saturate(DDGITraceEvaluateDirectRadiance(payload));
}

float3 DDGITraceEvaluateRadiance(DDGIProbeTracePayload payload, float3 rayDirectionWS)
{
    float3 directRadiance     = DDGITraceEvaluateDirectRadiance(payload);
    float3 surfaceBias        = payload.normalWS * _DDGIProbeNormalBias - rayDirectionWS * _DDGIProbeViewBias;
    float blendWeight         = DDGITraceGetVolumeBlendWeight(payload.positionWS);
    float3 indirectIrradiance = 0.0f;
    if (blendWeight > 0.0f)
    {
        indirectIrradiance =
            DDGITraceGetVolumeIrradiance(payload.positionWS, surfaceBias, payload.normalWS) * blendWeight;
    }

    float3 bounceAlbedo = min(payload.baseColor, float3(0.9f, 0.9f, 0.9f));
    return saturate(directRadiance + bounceAlbedo * INV_PI * indirectIrradiance);
}

void DDGITraceMiss(inout DDGIProbeTracePayload payload)
{
    payload.hitKind = DDGI_TRACE_HIT_KIND_MISS;
    payload.rayT    = -1.0f;
}

#endif

#endif
