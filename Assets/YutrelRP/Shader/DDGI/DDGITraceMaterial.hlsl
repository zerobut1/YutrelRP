#ifndef YUTREL_DDGI_TRACE_MATERIAL_INCLUDED
#define YUTREL_DDGI_TRACE_MATERIAL_INCLUDED

#include "Assets/YutrelRP/Shader/DDGI/DDGIProbeTraceCommon.hlsl"
#include "UnityRayTracingMeshUtils.cginc"

float2 DDGITraceMaterialHitUV(BuiltInTriangleIntersectionAttributes attributes, out bool uvValid)
{
    uvValid = false;
    if (!DDGITraceInstanceHasUV0())
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

#endif
