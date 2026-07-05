#ifndef YUTREL_DEFAULTLIT_RAYTRACING_INCLUDED
#define YUTREL_DEFAULTLIT_RAYTRACING_INCLUDED

#include "Assets/YutrelRP/Shader/DDGI/DDGIProbeTraceCommon.hlsl"
#include "Assets/YutrelRP/Shader/DefaultLitSurfaceContract.hlsl"
#include "UnityRayTracingMeshUtils.cginc"

struct DefaultLitRayTracingAttributes
{
    float2 barycentrics;
};

struct DefaultLitRayTracingVertex
{
    float3 position_OS;
    float3 normal_OS;
    float4 tangent_OS;
    float2 uv;
};

DefaultLitRayTracingVertex FetchDefaultLitRayTracingVertex(uint vertex_index)
{
    DefaultLitRayTracingVertex vertex;
    vertex.position_OS = UnityRayTracingFetchVertexAttribute3(vertex_index, kVertexAttributePosition);

    vertex.normal_OS = UnityRayTracingHasVertexAttribute(kVertexAttributeNormal)
                           ? UnityRayTracingFetchVertexAttribute3(vertex_index, kVertexAttributeNormal)
                           : float3(0.0f, 1.0f, 0.0f);

    vertex.tangent_OS = UnityRayTracingHasVertexAttribute(kVertexAttributeTangent)
                            ? UnityRayTracingFetchVertexAttribute4(vertex_index, kVertexAttributeTangent)
                            : float4(1.0f, 0.0f, 0.0f, 1.0f);

    vertex.uv = UnityRayTracingHasVertexAttribute(kVertexAttributeTexCoord0)
                    ? UnityRayTracingFetchVertexAttribute4(vertex_index, kVertexAttributeTexCoord0).xy
                    : 0.0f;

    return vertex;
}

DefaultLitRayTracingVertex InterpolateDefaultLitRayTracingVertex(DefaultLitRayTracingAttributes attributes)
{
    uint3 triangle_indices        = UnityRayTracingFetchTriangleIndices(PrimitiveIndex());
    DefaultLitRayTracingVertex v0 = FetchDefaultLitRayTracingVertex(triangle_indices.x);
    DefaultLitRayTracingVertex v1 = FetchDefaultLitRayTracingVertex(triangle_indices.y);
    DefaultLitRayTracingVertex v2 = FetchDefaultLitRayTracingVertex(triangle_indices.z);
    float3 barycentrics           = float3(
        1.0f - attributes.barycentrics.x - attributes.barycentrics.y,
        attributes.barycentrics.x,
        attributes.barycentrics.y);

    DefaultLitRayTracingVertex vertex;
    vertex.position_OS = v0.position_OS * barycentrics.x +
                         v1.position_OS * barycentrics.y +
                         v2.position_OS * barycentrics.z;
    vertex.normal_OS   = v0.normal_OS * barycentrics.x +
                         v1.normal_OS * barycentrics.y +
                         v2.normal_OS * barycentrics.z;
    vertex.tangent_OS  = v0.tangent_OS * barycentrics.x +
                         v1.tangent_OS * barycentrics.y +
                         v2.tangent_OS * barycentrics.z;
    vertex.uv          = v0.uv * barycentrics.x +
                         v1.uv * barycentrics.y +
                         v2.uv * barycentrics.z;
    return vertex;
}

float3 ComputeDefaultLitRayTracingGeometricNormalWS(DefaultLitRayTracingAttributes attributes)
{
    uint3 triangle_indices = UnityRayTracingFetchTriangleIndices(PrimitiveIndex());
    float3 p0              = UnityRayTracingFetchVertexAttribute3(triangle_indices.x, kVertexAttributePosition);
    float3 p1              = UnityRayTracingFetchVertexAttribute3(triangle_indices.y, kVertexAttributePosition);
    float3 p2              = UnityRayTracingFetchVertexAttribute3(triangle_indices.z, kVertexAttributePosition);
    float3 normal_OS       = cross(p1 - p0, p2 - p0);
    float3 normal_WS       = mul(normal_OS, (float3x3)WorldToObject3x4());
    return DDGITraceSafeNormalize(normal_WS, DDGITraceFallbackNormalWS());
}

float3 TransformDefaultLitRayTracingObjectToWorldNormal(float3 normal_OS)
{
    return DDGITraceSafeNormalize(
        mul(normal_OS, (float3x3)WorldToObject3x4()),
        DDGITraceFallbackNormalWS());
}

float3 DefaultLitRayTracingAnyPerpendicularVector(float3 normal_WS)
{
    float3 axis = abs(normal_WS.y) < 0.9f ? float3(0.0f, 1.0f, 0.0f) : float3(1.0f, 0.0f, 0.0f);
    return DDGITraceSafeNormalize(cross(axis, normal_WS), float3(1.0f, 0.0f, 0.0f));
}

float3 TransformDefaultLitRayTracingObjectToWorldTangent(float3 tangent_OS, float3 normal_WS)
{
    float3 tangent_WS = mul((float3x3)ObjectToWorld3x4(), tangent_OS);
    tangent_WS        = tangent_WS - normal_WS * dot(tangent_WS, normal_WS);
    return DDGITraceSafeNormalize(tangent_WS, DefaultLitRayTracingAnyPerpendicularVector(normal_WS));
}

DefaultLitSurfaceInput BuildDefaultLitRayTracingSurfaceInput(DefaultLitRayTracingAttributes attributes)
{
    DefaultLitRayTracingVertex vertex = InterpolateDefaultLitRayTracingVertex(attributes);

    DefaultLitSurfaceInput input;
    input.uv           = vertex.uv;
    input.normal_WS    = TransformDefaultLitRayTracingObjectToWorldNormal(vertex.normal_OS);
    input.tangent_WS   = TransformDefaultLitRayTracingObjectToWorldTangent(vertex.tangent_OS.xyz, input.normal_WS);
    float tangent_sign = vertex.tangent_OS.w * GetOddNegativeScale();
    input.bitangent_WS = normalize(cross(input.normal_WS, input.tangent_WS) * tangent_sign);
    return input;
}

[shader("anyhit")] void DDGIProbeTraceAnyHit(
    inout DDGIProbeTracePayload payload : SV_RayPayload,
    DefaultLitRayTracingAttributes attributes : SV_IntersectionAttributes)
{
    DefaultLitSurfaceInput input       = BuildDefaultLitRayTracingSurfaceInput(attributes);
    float4 base_color                  = SampleDefaultLitRayTracingBaseColorLOD(input.uv, 0.0f);
    DefaultLitAlphaClipData alpha_clip = BuildDefaultLitRayTracingAlphaClip(base_color.a);

    if (alpha_clip.enabled > 0.5f && alpha_clip.alpha < alpha_clip.cutoff)
    {
        IgnoreHit();
    }
}

    [shader("closesthit")] void DDGIProbeTraceClosestHit(
        inout DDGIProbeTracePayload payload : SV_RayPayload,
        DefaultLitRayTracingAttributes attributes : SV_IntersectionAttributes)
{
    DefaultLitSurfaceInput input = BuildDefaultLitRayTracingSurfaceInput(attributes);
    float base_color_lod         = GetDefaultLitRayTracingBaseColorGIMipLevel();
    float normal_lod             = GetDefaultLitRayTracingNormalGIMipLevel();
    float4 base_color            = SampleDefaultLitRayTracingBaseColorLOD(input.uv, base_color_lod);
    float3 geometric_normal_WS   = ComputeDefaultLitRayTracingGeometricNormalWS(attributes);
    float3 visibility_normal_WS  = DDGITraceOrientNormal(input.normal_WS, geometric_normal_WS);
    float3 shading_normal_WS     = SampleDefaultLitRayTracingNormalLOD(input, normal_lod);
    shading_normal_WS            = DDGITraceKeepSameHemisphere(shading_normal_WS, visibility_normal_WS);
    float3 position_WS           = WorldRayOrigin() + WorldRayDirection() * RayTCurrent();
    DDGITraceCommitClosestHit(payload, base_color.rgb, position_WS, visibility_normal_WS, shading_normal_WS);
}

#endif
