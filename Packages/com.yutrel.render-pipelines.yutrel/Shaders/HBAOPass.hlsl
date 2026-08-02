#ifndef YUTREL_HBAO_PASS_INCLUDED
#define YUTREL_HBAO_PASS_INCLUDED

#include "Utils/AmbientOcclusion.hlsl"

#define HBAO_MAX_DIRECTION_COUNT 16
#define HBAO_MAX_STEP_COUNT 16

float HBAOStepOcclusion(AOSurfaceData center, float2 sample_uv, float radius, float bias)
{
    AOSurfaceData sample_surface;
    if (!AOLoadSurface(sample_uv, sample_surface))
    {
        return 0.0f;
    }

    float3 sample_vector = sample_surface.position_WS - center.position_WS;
    float distance_sq    = max(dot(sample_vector, sample_vector), 0.000001f);
    float distance_value = sqrt(distance_sq);
    float range_weight   = saturate(1.0f - distance_value / radius);
    float depth_weight =
        saturate(AOSafeThickness() / max(abs(sample_surface.linear_depth - center.linear_depth), AOSafeThickness()));
    float horizon = saturate((dot(center.normal_WS, sample_vector * rsqrt(distance_sq)) - bias) / max(1.0f - bias, 0.001f));

    return horizon * range_weight * depth_weight;
}

float HBAOEstimateOcclusion(AOSurfaceData center, int direction_count, int step_count)
{
    float radius = AOSafeRadius();
    if (radius <= 0.0f || AOSafeIntensity() <= 0.0f || direction_count <= 0 || step_count <= 0)
    {
        return 0.0f;
    }

    float2 radius_uv = AOViewRadiusToUV(radius, center.linear_depth);
    float random     = AOInterleavedGradientNoise(center.uv * _ScreenParams.xy);
    float rotation   = random * TWO_PI;
    float bias       = AOSafeBias();
    float occlusion  = 0.0f;

    [loop] for (int direction_index = 0; direction_index < HBAO_MAX_DIRECTION_COUNT; direction_index++)
    {
        if (direction_index >= direction_count)
        {
            break;
        }

        float angle      = ((float)direction_index + random) * TWO_PI / (float)direction_count + rotation;
        float2 direction = float2(cos(angle), sin(angle));
        float horizon    = 0.0f;

        [loop] for (int step_index = 0; step_index < HBAO_MAX_STEP_COUNT; step_index++)
        {
            if (step_index >= step_count)
            {
                break;
            }

            float step_scale = ((float)step_index + 1.0f) / (float)step_count;
            float2 sample_uv = center.uv + direction * radius_uv * step_scale;
            horizon          = max(horizon, HBAOStepOcclusion(center, sample_uv, radius, bias));
        }

        occlusion += horizon;
    }

    return occlusion / (float)direction_count;
}

float4 HBAOFragment(FullScreenVaryings input) : SV_Target
{
    AOSurfaceData center;
    if (!AOLoadSurface(input.uv, center))
    {
        return AOOutput(1.0f);
    }

    int direction_count = AOClampedCount(_AODirectionCount, HBAO_MAX_DIRECTION_COUNT);
    int step_count      = AOClampedCount(_AOStepCount, HBAO_MAX_STEP_COUNT);
    float occlusion     = HBAOEstimateOcclusion(center, direction_count, step_count);
    return AOOutput(AOApplyIntensity(occlusion));
}

#endif
