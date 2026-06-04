#ifndef YUTREL_DDGI_PROBE_TRACE_COMMON_INCLUDED
#define YUTREL_DDGI_PROBE_TRACE_COMMON_INCLUDED

#include "Assets/YutrelRP/Shader/DDGI/DDGI.hlsl"

static const uint DDGI_TRACE_ALBEDO_STATUS_MISS       = 0u;
static const uint DDGI_TRACE_ALBEDO_STATUS_SAMPLED    = 1u;
static const uint DDGI_TRACE_ALBEDO_STATUS_FALLBACK   = 2u;
static const uint DDGI_TRACE_ALBEDO_STATUS_INVALID_UV = 3u;

static const uint DDGI_TRACE_MATERIAL_HAS_BASE_COLOR_TEXTURE = 1u;
static const uint DDGI_TRACE_MATERIAL_HAS_UV0                = 2u;

StructuredBuffer<uint2> _DDGITraceInstanceTriangleRanges;
StructuredBuffer<float4> _DDGITraceInstanceBaseColors;
StructuredBuffer<float4> _DDGITraceTriangleNormals;
StructuredBuffer<uint> _DDGITraceInstanceMaterialFlags;

struct DDGIProbeTracePayload
{
    uint hitKind;
    float rayT;
    float3 normalWS;
    float3 baseColor;
    uint albedoStatus;
};

uint DDGITraceInstanceMaterialFlags()
{
    return _DDGITraceInstanceMaterialFlags[InstanceID()];
}

bool DDGITraceInstanceHasBaseColorTexture()
{
    return (DDGITraceInstanceMaterialFlags() & DDGI_TRACE_MATERIAL_HAS_BASE_COLOR_TEXTURE) != 0u;
}

bool DDGITraceInstanceHasUV0()
{
    return (DDGITraceInstanceMaterialFlags() & DDGI_TRACE_MATERIAL_HAS_UV0) != 0u;
}

bool DDGITraceInstanceCanSampleBaseColorTexture()
{
    return DDGITraceInstanceHasBaseColorTexture() && DDGITraceInstanceHasUV0();
}

float3 DDGITraceFallbackNormalWS()
{
    return DDGISafeNormalize(-WorldRayDirection(), float3(0.0f, 1.0f, 0.0f));
}

float3 DDGITraceGeometricNormalWS()
{
    uint2 triangle_range = _DDGITraceInstanceTriangleRanges[InstanceID()];
    if (triangle_range.y == 0u)
    {
        return DDGITraceFallbackNormalWS();
    }

    uint normal_index = triangle_range.x + min(PrimitiveIndex(), triangle_range.y - 1u);
    float4 normal_WS  = _DDGITraceTriangleNormals[normal_index];
    return normal_WS.w > 0.5f ? DDGISafeNormalize(normal_WS.xyz, DDGITraceFallbackNormalWS()) : DDGITraceFallbackNormalWS();
}

float3 DDGITraceRayFacingNormalWS(float3 normalWS)
{
    float3 normal = DDGISafeNormalize(normalWS, DDGITraceFallbackNormalWS());
    return dot(normal, WorldRayDirection()) > 0.0f ? -normal : normal;
}

float3 DDGITraceFallbackBaseColor()
{
    return max(_DDGITraceInstanceBaseColors[InstanceID()].rgb, 0.0f);
}

float3 DDGITraceSanitizeBaseColor(float3 baseColor)
{
    return DDGIIsFinite3(baseColor) ? min(max(baseColor, 0.0f), 65504.0f) : DDGITraceFallbackBaseColor();
}

uint DDGITraceMaterialAlbedoStatus(bool uvValid)
{
    if (!DDGITraceInstanceHasUV0() || !uvValid)
    {
        return DDGI_TRACE_ALBEDO_STATUS_INVALID_UV;
    }

    if (!DDGITraceInstanceHasBaseColorTexture())
    {
        return DDGI_TRACE_ALBEDO_STATUS_FALLBACK;
    }

    return DDGI_TRACE_ALBEDO_STATUS_SAMPLED;
}

uint DDGITraceMaterialPassAlbedoStatus(bool uvValid)
{
    return (!DDGITraceInstanceHasUV0() || !uvValid)
               ? DDGI_TRACE_ALBEDO_STATUS_INVALID_UV
               : DDGI_TRACE_ALBEDO_STATUS_SAMPLED;
}

void DDGITraceCommitClosestHit(inout DDGIProbeTracePayload payload, float3 baseColor, uint albedoStatus)
{
    payload.hitKind      = HitKind() == HIT_KIND_TRIANGLE_FRONT_FACE ? 1u : 2u;
    payload.rayT         = RayTCurrent();
    payload.normalWS     = DDGITraceRayFacingNormalWS(DDGITraceGeometricNormalWS());
    payload.baseColor    = DDGITraceSanitizeBaseColor(baseColor);
    payload.albedoStatus = albedoStatus;
}

#endif
