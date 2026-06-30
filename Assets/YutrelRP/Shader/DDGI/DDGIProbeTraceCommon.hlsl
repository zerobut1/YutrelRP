#ifndef YUTREL_DDGI_PROBE_TRACE_COMMON_INCLUDED
#define YUTREL_DDGI_PROBE_TRACE_COMMON_INCLUDED

static const uint DDGI_TRACE_HIT_KIND_MISS         = 0u;
static const uint DDGI_TRACE_HIT_KIND_FRONT_FACE   = 1u;
static const uint DDGI_TRACE_HIT_KIND_BACK_FACE    = 2u;
static const float3 DDGI_TRACE_FALLBACK_BASE_COLOR = float3(0.8f, 0.8f, 0.8f);

struct DDGIProbeTracePayload
{
    uint hitKind;
    float rayT;
    float3 normalWS;
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

float3 DDGITraceRayFacingNormalWS(float3 normalWS)
{
    float3 normal = DDGITraceSafeNormalize(normalWS, DDGITraceFallbackNormalWS());
    return dot(normal, WorldRayDirection()) > 0.0f ? -normal : normal;
}

float3 DDGITraceOffsetRayOrigin(float3 positionWS, float3 normalWS, float bias)
{
    return positionWS + normalWS * max(bias, 0.0f);
}

void DDGITraceCommitClosestHit(inout DDGIProbeTracePayload payload, float3 baseColor, float3 normalWS)
{
    payload.hitKind   = HitKind() == HIT_KIND_TRIANGLE_FRONT_FACE ? DDGI_TRACE_HIT_KIND_FRONT_FACE : DDGI_TRACE_HIT_KIND_BACK_FACE;
    payload.rayT      = RayTCurrent();
    payload.normalWS  = DDGITraceRayFacingNormalWS(normalWS);
    payload.baseColor = max(baseColor, 0.0f);
}

#endif
