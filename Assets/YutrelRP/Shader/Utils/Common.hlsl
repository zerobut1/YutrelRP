#ifndef YUTREL_COMMON_INCLUDED
#define YUTREL_COMMON_INCLUDED

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonMaterial.hlsl"
#include "UnityInput.hlsl"

#define UNITY_MATRIX_M unity_ObjectToWorld
#define UNITY_MATRIX_I_M unity_WorldToObject
#define UNITY_MATRIX_V unity_MatrixV
#define UNITY_MATRIX_I_V unity_MatrixInvV
#define UNITY_MATRIX_VP unity_MatrixVP
#define UNITY_MATRIX_I_VP unity_MatrixInvVP
#define UNITY_MATRIX_P glstate_matrix_projection
#define UNITY_PREV_MATRIX_M unity_prev_MatrixM
#define UNITY_PREV_MATRIX_I_M unity_prev_MatrixIM

#if defined(_SHADOW_MASK_ALWAYS) || defined(_SHADOW_MASK_DISTANCE)
#define SHADOWS_SHADOWMASK
#endif

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/SpaceTransforms.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/UnityInstancing.hlsl"

SAMPLER(sampler_linear_clamp);
SAMPLER(sampler_point_clamp);

bool IsOrthographicCamera()
{
    return unity_OrthoParams.w;
}

float OrthographicDepthBufferToLinear(float rawDepth)
{
#if UNITY_REVERSED_Z
    rawDepth = 1.0 - rawDepth;
#endif
    return (_ProjectionParams.z - _ProjectionParams.y) * rawDepth +
           _ProjectionParams.y;
}

float Square(float x)
{
    return x * x;
}

float DistanceSquared(float3 pA, float3 pB)
{
    return dot(pA - pB, pA - pB);
}

struct FullScreenVaryings
{
    float4 position_CS : SV_POSITION;
    float2 uv : VAR_SCREEN_UV;
};

FullScreenVaryings DefaultFullScreenPassVertex(uint vertexID : SV_VertexID)
{
    FullScreenVaryings output;
    output.position_CS = GetFullScreenTriangleVertexPosition(vertexID);
    output.uv          = GetFullScreenTriangleTexCoord(vertexID);
    return output;
}

float2 FullScreenUVToPositionNDC(float2 uv)
{
    float2 position_NDC = uv;
#if UNITY_UV_STARTS_AT_TOP
    position_NDC.y = 1.0f - position_NDC.y;
#endif
    if (_ProjectionParams.x < 0.0)
    {
        position_NDC.y = 1.0f - position_NDC.y;
    }
    return position_NDC;
}

float3 ComputeWorldSpacePositionFromFullScreenUV(float2 uv, float device_depth)
{
    return ComputeWorldSpacePosition(FullScreenUVToPositionNDC(uv), device_depth, UNITY_MATRIX_I_VP);
}

float3 GetWorldSpaceViewDirectionForSurface(float3 position_WS)
{
    if (IsOrthographicCamera())
    {
        return normalize(UNITY_MATRIX_I_V._m02_m12_m22);
    }

    return normalize(_WorldSpaceCameraPos - position_WS);
}

#endif
