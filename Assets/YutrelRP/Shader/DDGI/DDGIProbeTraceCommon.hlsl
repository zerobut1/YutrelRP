#ifndef YUTREL_DDGI_PROBE_TRACE_COMMON_INCLUDED
#define YUTREL_DDGI_PROBE_TRACE_COMMON_INCLUDED

#include "Assets/YutrelRP/Shader/DDGI/DDGI.hlsl"
#include "Assets/YutrelRP/Shader/Utils/Common.hlsl"

static const uint DDGI_TRACE_ALBEDO_STATUS_MISS       = 0u;
static const uint DDGI_TRACE_ALBEDO_STATUS_SAMPLED    = 1u;
static const uint DDGI_TRACE_ALBEDO_STATUS_FALLBACK   = 2u;
static const uint DDGI_TRACE_ALBEDO_STATUS_INVALID_UV = 3u;

static const float3 DDGI_TRACE_FALLBACK_BASE_COLOR = float3(0.8f, 0.8f, 0.8f);

struct DDGIProbeTracePayload
{
    uint hitKind;
    float rayT;
    float3 normalWS;
    float3 geometricNormalWS;
    float3 baseColor;
    uint albedoStatus;
};

float3 DDGITraceFallbackNormalWS()
{
    return DDGISafeNormalize(-WorldRayDirection(), float3(0.0f, 1.0f, 0.0f));
}

float3 DDGITraceRayFacingNormalWS(float3 normalWS)
{
    float3 normal = DDGISafeNormalize(normalWS, DDGITraceFallbackNormalWS());
    return dot(normal, WorldRayDirection()) > 0.0f ? -normal : normal;
}

float3 DDGITraceFallbackBaseColor()
{
    return DDGI_TRACE_FALLBACK_BASE_COLOR;
}

float3 DDGITraceSanitizeBaseColor(float3 baseColor)
{
    return DDGIIsFinite3(baseColor) ? min(max(baseColor, 0.0f), 65504.0f) : DDGITraceFallbackBaseColor();
}

void DDGITraceCommitClosestHit(inout DDGIProbeTracePayload payload, float3 baseColor, uint albedoStatus, float3 geometricNormalWS)
{
    payload.hitKind           = HitKind() == HIT_KIND_TRIANGLE_FRONT_FACE ? 1u : 2u;
    payload.rayT              = RayTCurrent();
    payload.normalWS          = DDGITraceRayFacingNormalWS(geometricNormalWS);
    payload.geometricNormalWS = DDGISafeNormalize(geometricNormalWS, payload.normalWS);
    payload.baseColor         = DDGITraceSanitizeBaseColor(baseColor);
    payload.albedoStatus      = albedoStatus;
}

void DDGITraceCommitClosestHit(inout DDGIProbeTracePayload payload, float3 baseColor, uint albedoStatus)
{
    DDGITraceCommitClosestHit(payload, baseColor, albedoStatus, DDGITraceFallbackNormalWS());
}

#endif
