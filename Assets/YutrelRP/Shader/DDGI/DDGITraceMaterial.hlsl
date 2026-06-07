#ifndef YUTREL_DDGI_TRACE_MATERIAL_INCLUDED
#define YUTREL_DDGI_TRACE_MATERIAL_INCLUDED

#include "Assets/YutrelRP/Shader/DDGI/DDGIProbeTraceCommon.hlsl"
#include "Assets/YutrelRP/Shader/DefaultLitSurfaceContract.hlsl"
#include "UnityRayTracingMeshUtils.cginc"

float3 DDGITraceMaterialHitBarycentricWeights(BuiltInTriangleIntersectionAttributes attributes)
{
    float2 barycentrics = attributes.barycentrics;
    return float3(1.0f - barycentrics.x - barycentrics.y, barycentrics.x, barycentrics.y);
}

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
    float3 weights         = DDGITraceMaterialHitBarycentricWeights(attributes);

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

    float3 normal_WS = mul(normal_OS, (float3x3)WorldToObject3x4());
    return DDGISafeNormalize(normal_WS, DDGITraceFallbackNormalWS());
}

float3 DDGITraceMaterialInterpolateVertexAttribute3(BuiltInTriangleIntersectionAttributes attributes, uint attributeType)
{
    uint3 triangle_indices = UnityRayTracingFetchTriangleIndices(PrimitiveIndex());
    float3 weights         = DDGITraceMaterialHitBarycentricWeights(attributes);

    float3 v0 = UnityRayTracingFetchVertexAttribute3(triangle_indices.x, attributeType);
    float3 v1 = UnityRayTracingFetchVertexAttribute3(triangle_indices.y, attributeType);
    float3 v2 = UnityRayTracingFetchVertexAttribute3(triangle_indices.z, attributeType);
    return v0 * weights.x + v1 * weights.y + v2 * weights.z;
}

float4 DDGITraceMaterialInterpolateVertexAttribute4(BuiltInTriangleIntersectionAttributes attributes, uint attributeType)
{
    uint3 triangle_indices = UnityRayTracingFetchTriangleIndices(PrimitiveIndex());
    float3 weights         = DDGITraceMaterialHitBarycentricWeights(attributes);

    float4 v0 = UnityRayTracingFetchVertexAttribute4(triangle_indices.x, attributeType);
    float4 v1 = UnityRayTracingFetchVertexAttribute4(triangle_indices.y, attributeType);
    float4 v2 = UnityRayTracingFetchVertexAttribute4(triangle_indices.z, attributeType);
    return v0 * weights.x + v1 * weights.y + v2 * weights.z;
}

float3 DDGITraceMaterialFallbackTangentWS(float3 normalWS)
{
    float3 axis = abs(normalWS.y) < 0.999f ? float3(0.0f, 1.0f, 0.0f) : float3(1.0f, 0.0f, 0.0f);
    return DDGISafeNormalize(cross(axis, normalWS), float3(1.0f, 0.0f, 0.0f));
}

float3 DDGITraceMaterialShadingNormalWS(BuiltInTriangleIntersectionAttributes attributes, float3 geometricNormalWS)
{
    if (!UnityRayTracingHasVertexAttribute(kVertexAttributeNormal))
    {
        return geometricNormalWS;
    }

    float3 normal_OS = DDGITraceMaterialInterpolateVertexAttribute3(attributes, kVertexAttributeNormal);
    float3 normal_WS = mul(normal_OS, (float3x3)WorldToObject3x4());
    return DDGISafeNormalize(normal_WS, geometricNormalWS);
}

DefaultLitSurfaceInput DDGITraceMaterialBuildDefaultLitSurfaceInput(BuiltInTriangleIntersectionAttributes attributes,
                                                                    float2 uv,
                                                                    float3 geometricNormalWS)
{
    DefaultLitSurfaceInput input;
    input.uv        = uv;
    input.normal_WS = DDGITraceMaterialShadingNormalWS(attributes, geometricNormalWS);

    float3 fallback_tangent_WS = DDGITraceMaterialFallbackTangentWS(input.normal_WS);
    if (UnityRayTracingHasVertexAttribute(kVertexAttributeTangent))
    {
        float4 tangent_OS = DDGITraceMaterialInterpolateVertexAttribute4(attributes, kVertexAttributeTangent);
        float3 tangent_WS = TransformObjectToWorldDir(tangent_OS.xyz);
        tangent_WS        = tangent_WS - input.normal_WS * dot(input.normal_WS, tangent_WS);
        input.tangent_WS  = DDGISafeNormalize(tangent_WS, fallback_tangent_WS);

        float tangent_sign = (tangent_OS.w >= 0.0f ? 1.0f : -1.0f) * GetOddNegativeScale();
        input.bitangent_WS = DDGISafeNormalize(cross(input.normal_WS, input.tangent_WS) * tangent_sign,
                                               cross(input.normal_WS, fallback_tangent_WS));
    }
    else
    {
        input.tangent_WS   = fallback_tangent_WS;
        input.bitangent_WS = DDGISafeNormalize(cross(input.normal_WS, input.tangent_WS), float3(0.0f, 0.0f, 1.0f));
    }

    return input;
}

void DDGITraceMaterialCommitClosestHit(inout DDGIProbeTracePayload payload,
                                       BuiltInTriangleIntersectionAttributes attributes,
                                       float3 baseColor,
                                       uint albedoStatus)
{
    float3 geometric_normal_WS = DDGITraceMaterialGeometricNormalWS();
    float3 shading_normal_WS   = DDGITraceMaterialShadingNormalWS(attributes, geometric_normal_WS);
    DDGITraceCommitClosestHit(payload, baseColor, albedoStatus, shading_normal_WS, geometric_normal_WS);
}

void DDGITraceMaterialCommitClosestHit(inout DDGIProbeTracePayload payload,
                                       BuiltInTriangleIntersectionAttributes attributes,
                                       float3 baseColor,
                                       uint albedoStatus,
                                       float3 shadingNormalWS)
{
    float3 geometric_normal_WS = DDGITraceMaterialGeometricNormalWS();
    DDGITraceCommitClosestHit(payload, baseColor, albedoStatus, shadingNormalWS, geometric_normal_WS);
}

#endif
