#ifndef YUTREL_SSAO_PASS_INCLUDED
#define YUTREL_SSAO_PASS_INCLUDED

#include "Utils/AmbientOcclusion.hlsl"

#define SSAO_MAX_SAMPLE_COUNT 64

float SSAOEstimateOcclusion(AOSurfaceData center, int sample_count)
{
    float radius = AOSafeRadius();
    if (radius <= 0.0f || AOSafeIntensity() <= 0.0f || sample_count <= 0)
    {
        return 0.0f;
    }

    float2 radius_uv = AOViewRadiusToUV(radius, center.linear_depth);
    float random     = AOInterleavedGradientNoise(center.uv * _ScreenParams.xy);
    float rotation   = random * TWO_PI;
    float occlusion  = 0.0f;
    float weight_sum = 0.0f;
    float bias       = AOSafeBias();

    [loop] for (int i = 0; i < SSAO_MAX_SAMPLE_COUNT; i++)
    {
        if (i >= sample_count)
        {
            break;
        }

        float sample_index = (float)i + 0.5f;
        float angle        = sample_index * 2.39996323f + rotation;
        float radius_scale = sqrt(sample_index / (float)sample_count);
        float2 direction   = float2(cos(angle), sin(angle));
        float2 sample_uv   = center.uv + direction * radius_uv * radius_scale;

        AOSurfaceData sample_surface;
        if (!AOLoadSurface(sample_uv, sample_surface))
        {
            continue;
        }

        float3 sample_vector = sample_surface.position_WS - center.position_WS;
        float distance_sq    = max(dot(sample_vector, sample_vector), 0.000001f);
        float distance_value = sqrt(distance_sq);
        float range_weight   = saturate(1.0f - distance_value / radius);
        float normal_term    = saturate((dot(center.normal_WS, sample_vector * rsqrt(distance_sq)) - bias) / max(1.0f - bias, 0.001f));

        occlusion += normal_term * range_weight;
        weight_sum += range_weight;
    }

    return weight_sum > 0.0f ? occlusion / weight_sum : 0.0f;
}

float4 SSAOFragment(FullScreenVaryings input) : SV_Target
{
    AOSurfaceData center;
    if (!AOLoadSurface(input.uv, center))
    {
        return AOOutput(1.0f);
    }

    int sample_count = AOClampedCount(_AOSampleCount, SSAO_MAX_SAMPLE_COUNT);
    float occlusion  = SSAOEstimateOcclusion(center, sample_count);
    return AOOutput(AOApplyIntensity(occlusion));
}

#endif
