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

#endif
