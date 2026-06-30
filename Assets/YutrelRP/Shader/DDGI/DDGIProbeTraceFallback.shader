Shader "Hidden/YutrelRP/DDGI/ProbeTraceFallback"
{
    SubShader
    {
        Pass
        {
            Name "DDGIProbeTrace"
            Tags
            {
                "LightMode" = "DDGIProbeTrace"
            }

            HLSLPROGRAM
            #pragma target 5.0
            #pragma raytracing DDGIProbeTrace

            #include "UnityRayTracingMeshUtils.cginc"
            #include "DDGIProbeTraceCommon.hlsl"

            float3 DDGITraceGeometricNormalWS()
            {
                uint3 triangleIndices = UnityRayTracingFetchTriangleIndices(PrimitiveIndex());
                float3 p0 = UnityRayTracingFetchVertexAttribute3(triangleIndices.x, kVertexAttributePosition);
                float3 p1 = UnityRayTracingFetchVertexAttribute3(triangleIndices.y, kVertexAttributePosition);
                float3 p2 = UnityRayTracingFetchVertexAttribute3(triangleIndices.z, kVertexAttributePosition);
                float3 normalOS = cross(p1 - p0, p2 - p0);
                float3 normalWS = mul(normalOS, (float3x3)WorldToObject3x4());
                return DDGITraceSafeNormalize(normalWS, DDGITraceFallbackNormalWS());
            }

            [shader("closesthit")]
            void DDGIProbeTraceClosestHit(
                inout DDGIProbeTracePayload payload : SV_RayPayload,
                BuiltInTriangleIntersectionAttributes attributes)
            {
                DDGITraceCommitClosestHit(payload, DDGI_TRACE_FALLBACK_BASE_COLOR, DDGITraceGeometricNormalWS());
            }
            ENDHLSL
        }
    }
}
