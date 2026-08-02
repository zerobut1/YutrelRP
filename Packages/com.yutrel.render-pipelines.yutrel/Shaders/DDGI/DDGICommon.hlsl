#ifndef YUTREL_DDGI_COMMON_INCLUDED
#define YUTREL_DDGI_COMMON_INCLUDED

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Macros.hlsl"

float4 _DDGIProbeRayRotationRow0;
float4 _DDGIProbeRayRotationRow1;
float4 _DDGIProbeRayRotationRow2;
float3 _DDGIProbeBoundsMin;
float3 _DDGIProbeSpacing;
float3 _DDGIProbeCount;
int _DDGIProbeRelocationEnabled;
int _DDGIProbeClassificationEnabled;

#define DDGI_FIXED_RAY_COUNT 32
#define DDGI_PROBE_STATE_ACTIVE 0.0f
#define DDGI_PROBE_STATE_INACTIVE 1.0f

static const float DDGI_PROBE_RAY_MISS_DISTANCE  = 1.0e27f;
static const float DDGI_PROBE_RAY_BACKFACE_SCALE = 0.2f;

struct DDGIProbeRayData
{
    float3 radiance01;
    float signedDistance;
};

int3 DDGIProbeCount()
{
    return (int3)_DDGIProbeCount;
}

uint3 DDGIProbeCountU()
{
    return (uint3)max(DDGIProbeCount(), int3(1, 1, 1));
}

int DDGIProbeIndex(int3 coords)
{
    int3 count = DDGIProbeCount();
    return coords.x + coords.z * count.x + coords.y * count.x * count.z;
}

int DDGIProbePlaneIndex(int3 coords)
{
    int3 count = DDGIProbeCount();
    return coords.x + coords.z * count.x;
}

int3 DDGIProbeCoords(int index)
{
    uint3 count    = DDGIProbeCountU();
    uint safeIndex = (uint)max(index, 0);
    return int3(
        safeIndex % count.x,
        safeIndex / (count.x * count.z),
        (safeIndex / count.x) % count.z);
}

int3 DDGIProbeCoordsFromPlaneIndex(int planeIndex, int probeY)
{
    uint3 count         = DDGIProbeCountU();
    uint safePlaneIndex = (uint)max(planeIndex, 0);
    return int3(safePlaneIndex % count.x, probeY, safePlaneIndex / count.x);
}

int3 DDGIProbeDataCoords(int3 coords)
{
    return int3(coords.x, coords.z, coords.y);
}

float3 DDGIProbeBaseWorldPosition(int3 coords)
{
    return _DDGIProbeBoundsMin + _DDGIProbeSpacing * (float3)coords;
}

float3 DDGIProbeWorldPosition(Texture2DArray<float4> probeData, int3 coords)
{
    float3 position = DDGIProbeBaseWorldPosition(coords);
    if (_DDGIProbeRelocationEnabled != 0)
    {
        position += probeData.Load(int4(DDGIProbeDataCoords(coords), 0)).xyz * _DDGIProbeSpacing;
    }

    return position;
}

float DDGILoadProbeState(Texture2DArray<float4> probeData, int3 coords)
{
    return probeData.Load(int4(DDGIProbeDataCoords(coords), 0)).w;
}

float3 DDGIProbeAtlasUV(int probeIndex, float2 octantCoordinates, int interiorTexels)
{
    int3 probeCount  = DDGIProbeCount();
    int3 probeCoords = DDGIProbeCoords(probeIndex);
    float tile       = (float)interiorTexels + 2.0f;
    float2 atlasSize = float2(probeCount.x, probeCount.z) * tile;
    float2 uv        = float2(probeCoords.x, probeCoords.z) * tile + tile * 0.5f;
    uv += octantCoordinates * ((float)interiorTexels * 0.5f);
    return float3(uv / atlasSize, probeCoords.y);
}

float3 DDGISphericalFibonacci(uint sampleIndex, uint sampleCount)
{
    const float b  = 0.61803398874989484820f;
    float phi      = TWO_PI * frac((float)sampleIndex * b);
    float cosTheta = 1.0f - (2.0f * (float)sampleIndex + 1.0f) / (float)max(sampleCount, 1u);
    float sinTheta = sqrt(saturate(1.0f - cosTheta * cosTheta));
    return float3(cos(phi) * sinTheta, sin(phi) * sinTheta, cosTheta);
}

float3 DDGIRotateProbeRayDirection(float3 direction)
{
    return normalize(float3(
        dot(direction, float3(_DDGIProbeRayRotationRow0.x, _DDGIProbeRayRotationRow1.x, _DDGIProbeRayRotationRow2.x)),
        dot(direction, float3(_DDGIProbeRayRotationRow0.y, _DDGIProbeRayRotationRow1.y, _DDGIProbeRayRotationRow2.y)),
        dot(direction, float3(_DDGIProbeRayRotationRow0.z, _DDGIProbeRayRotationRow1.z, _DDGIProbeRayRotationRow2.z))));
}

bool DDGIUsesFixedRays(uint rayCount)
{
    return (_DDGIProbeRelocationEnabled != 0 || _DDGIProbeClassificationEnabled != 0) &&
           rayCount > (uint)DDGI_FIXED_RAY_COUNT;
}

bool DDGIIsFixedRay(uint rayIndex, uint rayCount)
{
    return DDGIUsesFixedRays(rayCount) && rayIndex < (uint)DDGI_FIXED_RAY_COUNT;
}

float3 DDGIGetProbeRayDirection(uint rayIndex, uint rayCount)
{
    uint safeRayCount = max(rayCount, 1u);
    float3 direction  = DDGISphericalFibonacci(rayIndex, safeRayCount);

    if (DDGIUsesFixedRays(safeRayCount))
    {
        if (rayIndex < (uint)DDGI_FIXED_RAY_COUNT)
        {
            direction = DDGISphericalFibonacci(rayIndex, (uint)DDGI_FIXED_RAY_COUNT);
        }
        else
        {
            uint randomIndex = rayIndex - (uint)DDGI_FIXED_RAY_COUNT;
            uint randomCount = safeRayCount - (uint)DDGI_FIXED_RAY_COUNT;
            direction        = DDGIRotateProbeRayDirection(DDGISphericalFibonacci(randomIndex, randomCount));
        }
    }
    else
    {
        direction = DDGIRotateProbeRayDirection(direction);
    }

    return normalize(direction);
}

uint DDGIPackRadiance01(float3 value)
{
    float3 radiance = saturate(value);
    return (uint)round(radiance.r * 1023.0f) |
           ((uint)round(radiance.g * 1023.0f) << 10) |
           ((uint)round(radiance.b * 1023.0f) << 20);
}

float DDGIMaxComponent(float3 value)
{
    return max(max(value.x, value.y), value.z);
}

float3 DDGIUnpackRadiance01(uint packedRadiance)
{
    return float3(
        (float)(packedRadiance & 0x000003FFu) / 1023.0f,
        (float)((packedRadiance >> 10) & 0x000003FFu) / 1023.0f,
        (float)((packedRadiance >> 20) & 0x000003FFu) / 1023.0f);
}

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
    static const float threshold = 1.0f / 255.0f;
    if (DDGIMaxComponent(radiance01) <= threshold)
    {
        radiance01 = 0.0f;
    }

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
