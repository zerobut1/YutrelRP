#ifndef YUTREL_DDGI_PROBE_RAY_DATA_INCLUDED
#define YUTREL_DDGI_PROBE_RAY_DATA_INCLUDED

#include "DDGICommon.hlsl"

static const float DDGI_PROBE_RAY_MISS_DISTANCE  = 1.0e27f;
static const float DDGI_PROBE_RAY_BACKFACE_SCALE = 0.2f;

struct DDGIProbeRayData
{
    float3 radiance01;
    float signedDistance;
};

float2 DDGIEncodeProbeRayData(float3 radiance01, float signedDistance)
{
    return float2(asfloat(DDGIPackRadiance01(radiance01)), signedDistance);
}

DDGIProbeRayData DDGIDecodeProbeRayData(float2 raw)
{
    DDGIProbeRayData data;
    data.radiance01     = DDGIUnpackRadiance01(asuint(raw.x));
    data.signedDistance = raw.y;
    return data;
}

float2 DDGIEncodeProbeRayMiss(float3 radiance01)
{
    return DDGIEncodeProbeRayData(radiance01, DDGI_PROBE_RAY_MISS_DISTANCE);
}

float2 DDGIEncodeProbeRayFrontface(float3 radiance01, float distance)
{
    return DDGIEncodeProbeRayData(radiance01, max(distance, 0.0f));
}

float2 DDGIEncodeProbeRayBackface(float distance)
{
    return DDGIEncodeProbeRayData(float3(0.0f, 0.0f, 0.0f), -abs(distance) * DDGI_PROBE_RAY_BACKFACE_SCALE);
}

bool DDGIProbeRayIsMiss(DDGIProbeRayData data)
{
    return data.signedDistance >= DDGI_PROBE_RAY_MISS_DISTANCE * 0.5f;
}

bool DDGIProbeRayIsBackface(DDGIProbeRayData data)
{
    return data.signedDistance < 0.0f;
}

#endif
