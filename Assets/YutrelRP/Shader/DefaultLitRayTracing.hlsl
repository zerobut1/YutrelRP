#ifndef YUTREL_DEFAULTLIT_RAYTRACING_INCLUDED
#define YUTREL_DEFAULTLIT_RAYTRACING_INCLUDED

#include "Assets/YutrelRP/Shader/DDGI/DDGIProbeTraceCommon.hlsl"
#include "Assets/YutrelRP/Shader/DefaultLitSurfaceContract.hlsl"
#include "Assets/YutrelRP/Shader/StandardDefaultLitSurface.hlsl"
#include "UnityRayTracingMeshUtils.cginc"

struct DefaultLitRayTracingAttributes
{
    float2 barycentrics;
};

struct DefaultLitRayTracingVertex
{
    float3 normal_OS;
    float4 tangent_OS;
    float2 uv;
};

DefaultLitRayTracingVertex FetchDefaultLitRayTracingVertex(uint vertex_index)
{
    DefaultLitRayTracingVertex vertex;
    vertex.normal_OS = UnityRayTracingFetchVertexAttribute3(vertex_index, kVertexAttributeNormal);

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
    vertex.normal_OS  = v0.normal_OS * barycentrics.x +
                        v1.normal_OS * barycentrics.y +
                        v2.normal_OS * barycentrics.z;
    vertex.tangent_OS = v0.tangent_OS * barycentrics.x +
                        v1.tangent_OS * barycentrics.y +
                        v2.tangent_OS * barycentrics.z;
    vertex.uv         = v0.uv * barycentrics.x +
                        v1.uv * barycentrics.y +
                        v2.uv * barycentrics.z;
    return vertex;
}

DefaultLitSurfaceInput BuildDefaultLitRayTracingSurfaceInput(DefaultLitRayTracingAttributes attributes)
{
    DefaultLitRayTracingVertex vertex = InterpolateDefaultLitRayTracingVertex(attributes);

    DefaultLitSurfaceInput input;
    input.uv           = vertex.uv;
    input.normal_WS    = normalize(TransformObjectToWorldNormal(vertex.normal_OS));
    input.tangent_WS   = normalize(TransformObjectToWorldDir(vertex.tangent_OS.xyz));
    float tangent_sign = vertex.tangent_OS.w * GetOddNegativeScale();
    input.bitangent_WS = normalize(cross(input.normal_WS, input.tangent_WS) * tangent_sign);
    return input;
}

[shader("anyhit")] void DDGIProbeTraceAnyHit(
    inout DDGIProbeTracePayload payload : SV_RayPayload,
    DefaultLitRayTracingAttributes attributes : SV_IntersectionAttributes)
{
    DefaultLitSurfaceInput input       = BuildDefaultLitRayTracingSurfaceInput(attributes);
    float4 base_color                  = SampleStandardDefaultLitBaseColorLOD(input.uv, 0.0f);
    DefaultLitAlphaClipData alpha_clip = BuildStandardDefaultLitAlphaClip(base_color.a);

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
    float4 base_color            = SampleStandardDefaultLitBaseColorLOD(input.uv, 0.0f);
    float3 normal_WS             = SampleStandardDefaultLitNormalLOD(input, 0.0f);
    DDGITraceCommitClosestHit(payload, base_color.rgb, normal_WS);
}

#endif
