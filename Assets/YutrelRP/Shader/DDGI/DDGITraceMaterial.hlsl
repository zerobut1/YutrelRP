#ifndef YUTREL_DDGI_TRACE_MATERIAL_INCLUDED
#define YUTREL_DDGI_TRACE_MATERIAL_INCLUDED

#include "Assets/YutrelRP/Shader/DDGI/DDGIProbeTraceCommon.hlsl"
#include "UnityRayTracingMeshUtils.cginc"

bool DDGITraceMaterialHitHasUV0()
{
    return UnityRayTracingHasVertexAttribute(kVertexAttributeTexCoord0);
}

float2 DDGITraceMaterialHitUV(BuiltInTriangleIntersectionAttributes attributes, out bool uvValid)
{
    uvValid = false;
    if (!DDGITraceMaterialHitHasUV0())
    {
        return 0.0f.xx;
    }

    uint3 triangle_indices = UnityRayTracingFetchTriangleIndices(PrimitiveIndex());
    float2 barycentrics    = attributes.barycentrics;
    float3 weights         = float3(1.0f - barycentrics.x - barycentrics.y, barycentrics.x, barycentrics.y);

    float2 uv0 = UnityRayTracingFetchVertexAttribute2(triangle_indices.x, kVertexAttributeTexCoord0);
    float2 uv1 = UnityRayTracingFetchVertexAttribute2(triangle_indices.y, kVertexAttributeTexCoord0);
    float2 uv2 = UnityRayTracingFetchVertexAttribute2(triangle_indices.z, kVertexAttributeTexCoord0);
    float2 uv  = uv0 * weights.x + uv1 * weights.y + uv2 * weights.z;

    uvValid = DDGIIsFinite2(uv);
    return uvValid ? uv : 0.0f.xx;
}

float3 DDGITraceMaterialGeometricNormalWS()
{
    uint3 triangle_indices = UnityRayTracingFetchTriangleIndices(PrimitiveIndex());
    float3 p0              = UnityRayTracingFetchVertexAttribute3(triangle_indices.x, kVertexAttributePosition);
    float3 p1              = UnityRayTracingFetchVertexAttribute3(triangle_indices.y, kVertexAttributePosition);
    float3 p2              = UnityRayTracingFetchVertexAttribute3(triangle_indices.z, kVertexAttributePosition);
    float3 normal_OS       = cross(p1 - p0, p2 - p0);
    if (!DDGIIsFinite3(normal_OS) || dot(normal_OS, normal_OS) <= 1.0e-10f)
    {
        return DDGITraceFallbackNormalWS();
    }

    float3 normal_WS = TransformObjectToWorldNormal(normal_OS);
    return DDGISafeNormalize(normal_WS, DDGITraceFallbackNormalWS());
}

void DDGITraceMaterialCommitClosestHit(inout DDGIProbeTracePayload payload,
                                       BuiltInTriangleIntersectionAttributes attributes,
                                       float3 baseColor,
                                       uint albedoStatus)
{
    DDGITraceCommitClosestHit(payload, baseColor, albedoStatus, DDGITraceMaterialGeometricNormalWS());
}

#endif
