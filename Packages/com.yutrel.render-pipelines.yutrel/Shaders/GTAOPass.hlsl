#ifndef YUTREL_GTAO_PASS_INCLUDED
#define YUTREL_GTAO_PASS_INCLUDED

#include "Utils/AmbientOcclusion.hlsl"

#define GTAO_MAX_SLICE_COUNT 12
#define GTAO_MAX_SAMPLES_PER_SLICE 8

float GTAOHorizon(AOSurfaceData center, float2 sample_uv, float radius, float bias, float3 tangent_VS)
{
    AOSurfaceData sample_surface;
    if (!AOLoadSurface(sample_uv, sample_surface))
    {
        return 0.0f;
    }

    float3 sample_vector_VS = sample_surface.position_VS - center.position_VS;
    float distance_sq       = max(dot(sample_vector_VS, sample_vector_VS), 0.000001f);
    float distance_value    = sqrt(distance_sq);
    float range_weight      = saturate(1.0f - distance_value / radius);
    float depth_weight =
        saturate(AOSafeThickness() / max(abs(sample_surface.linear_depth - center.linear_depth), AOSafeThickness()));
    float3 sample_direction_VS = sample_vector_VS * rsqrt(distance_sq);
    float normal_elevation     = dot(center.normal_VS, sample_direction_VS);
    float tangent_alignment    = saturate(abs(dot(tangent_VS, sample_direction_VS)));

    return saturate((normal_elevation - bias) / max(1.0f - bias, 0.001f)) * tangent_alignment * range_weight * depth_weight;
}

float GTAOEstimateOcclusion(AOSurfaceData center, int slice_count, int samples_per_slice)
{
    float radius = AOSafeRadius();
    if (radius <= 0.0f || AOSafeIntensity() <= 0.0f || slice_count <= 0 || samples_per_slice <= 0)
    {
        return 0.0f;
    }

    float2 radius_uv = AOViewRadiusToUV(radius, center.linear_depth);
    float random     = AOInterleavedGradientNoise(center.uv * _ScreenParams.xy);
    float rotation   = random * PI;
    float bias       = AOSafeBias();
    float occlusion  = 0.0f;

    [loop] for (int slice_index = 0; slice_index < GTAO_MAX_SLICE_COUNT; slice_index++)
    {
        if (slice_index >= slice_count)
        {
            break;
        }

        float angle          = ((float)slice_index + random) * PI / (float)slice_count + rotation;
        float2 direction     = float2(cos(angle), sin(angle));
        float3 view_axis     = normalize(float3(direction, 0.0f));
        float3 tangent_VS    = view_axis - center.normal_VS * dot(center.normal_VS, view_axis);
        float tangent_length = length(tangent_VS);
        tangent_VS           = tangent_length > 0.0001f ? tangent_VS / tangent_length : AOAnyPerpendicularVector(center.normal_VS);

        float positive_horizon = 0.0f;
        float negative_horizon = 0.0f;

        [loop] for (int sample_index = 0; sample_index < GTAO_MAX_SAMPLES_PER_SLICE; sample_index++)
        {
            if (sample_index >= samples_per_slice)
            {
                break;
            }

            float sample_scale   = ((float)sample_index + 1.0f) / (float)samples_per_slice;
            float jittered_scale = saturate(sample_scale + (random - 0.5f) / (float)samples_per_slice);
            float2 positive_uv   = center.uv + direction * radius_uv * jittered_scale;
            float2 negative_uv   = center.uv - direction * radius_uv * jittered_scale;
            positive_horizon     = max(positive_horizon, GTAOHorizon(center, positive_uv, radius, bias, tangent_VS));
            negative_horizon     = max(negative_horizon, GTAOHorizon(center, negative_uv, radius, bias, tangent_VS));
        }

        occlusion += saturate(positive_horizon + negative_horizon);
    }

    return occlusion / (float)slice_count;
}

float4 GTAOFragment(FullScreenVaryings input) : SV_Target
{
    AOSurfaceData center;
    if (!AOLoadSurface(input.uv, center))
    {
        return AOOutput(1.0f);
    }

    int slice_count       = AOClampedCount(_AOSliceCount, GTAO_MAX_SLICE_COUNT);
    int samples_per_slice = AOClampedCount(_AOSamplesPerSlice, GTAO_MAX_SAMPLES_PER_SLICE);
    float occlusion       = GTAOEstimateOcclusion(center, slice_count, samples_per_slice);
    return AOOutput(AOApplyIntensity(occlusion));
}

#endif
