#ifndef YUTREL_AMBIENT_OCCLUSION_INCLUDED
#define YUTREL_AMBIENT_OCCLUSION_INCLUDED

#include "GBuffer.hlsl"

float _AORadius;
float _AOIntensity;
float _AOBias;
float _AOSampleCount;
float _AODirectionCount;
float _AOStepCount;
float _AOThickness;
float _AOSliceCount;
float _AOSamplesPerSlice;
float _AODenoiseRadius;

struct AOSurfaceData
{
    float2 uv;
    float raw_depth;
    float linear_depth;
    float3 position_WS;
    float3 position_VS;
    float3 normal_WS;
    float3 normal_VS;
};

bool AOIsInsideScreen(float2 uv)
{
    return all(uv >= 0.0f) && all(uv <= 1.0f);
}

bool AOIsValidDepth(float raw_depth)
{
#if UNITY_REVERSED_Z
    return raw_depth > 0.0f;
#else
    return raw_depth < 1.0f;
#endif
}

float AOSafeRadius()
{
    return max(_AORadius, 0.0f);
}

float AOSafeIntensity()
{
    return max(_AOIntensity, 0.0f);
}

float AOSafeBias()
{
    return saturate(_AOBias);
}

float AOSafeThickness()
{
    return max(_AOThickness, 0.0001f);
}

int AOClampedCount(float value, int max_count)
{
    return clamp((int)round(value), 0, max_count);
}

float2 AOViewRadiusToUV(float radius, float linear_depth)
{
    float safe_depth      = max(linear_depth, 0.001f);
    float2 projection     = float2(abs(UNITY_MATRIX_P[0][0]), abs(UNITY_MATRIX_P[1][1]));
    float2 projected_size = 0.5f * radius * projection / safe_depth;
    return max(projected_size, 1.0f / max(_ScreenParams.xy, 1.0f));
}

float AOInterleavedGradientNoise(float2 position_SS)
{
    return frac(52.9829189f * frac(dot(position_SS, float2(0.06711056f, 0.00583715f))));
}

float2 AORotateDirection(float2 direction, float angle)
{
    float s;
    float c;
    sincos(angle, s, c);
    return float2(direction.x * c - direction.y * s, direction.x * s + direction.y * c);
}

float3 AOAnyPerpendicularVector(float3 normal)
{
    float3 axis = abs(normal.y) < 0.9f ? float3(0.0f, 1.0f, 0.0f) : float3(1.0f, 0.0f, 0.0f);
    return normalize(cross(normal, axis));
}

bool AOLoadSurface(float2 uv, out AOSurfaceData surface)
{
    surface = (AOSurfaceData)0;
    if (!AOIsInsideScreen(uv))
    {
        return false;
    }

    float raw_depth = SAMPLE_TEXTURE2D(_SceneDepth, sampler_SceneDepth, uv).r;
    if (!AOIsValidDepth(raw_depth))
    {
        return false;
    }

    EncodedGBuffer gbuffer;
    gbuffer.scene_color = float4(0.0f, 0.0f, 0.0f, 0.0f);
    gbuffer.GBuffer_A   = SAMPLE_TEXTURE2D(_GBuffer_A, sampler_GBuffer_A, uv);
    gbuffer.GBuffer_B   = SAMPLE_TEXTURE2D(_GBuffer_B, sampler_GBuffer_B, uv);
    gbuffer.GBuffer_C   = SAMPLE_TEXTURE2D(_GBuffer_C, sampler_GBuffer_C, uv);
    gbuffer.scene_depth = raw_depth;
    gbuffer.uv          = uv;

    GBufferData gbuffer_data = DecodeGBuffer(gbuffer);
    if (gbuffer_data.shading_model_id != 1)
    {
        return false;
    }

    surface.uv           = uv;
    surface.raw_depth    = raw_depth;
    surface.position_WS  = ComputeWorldSpacePositionFromFullScreenUV(uv, raw_depth);
    surface.position_VS  = mul(UNITY_MATRIX_V, float4(surface.position_WS, 1.0f)).xyz;
    surface.linear_depth = LinearEyeDepth(surface.position_WS, UNITY_MATRIX_V);
    surface.normal_WS    = normalize(gbuffer_data.normal_WS);
    surface.normal_VS    = normalize(mul((float3x3)UNITY_MATRIX_V, surface.normal_WS));

    return true;
}

float AOApplyIntensity(float occlusion)
{
    return saturate(1.0f - occlusion * AOSafeIntensity());
}

float4 AOOutput(float ao)
{
    return float4(ao, ao, ao, ao);
}

#endif
