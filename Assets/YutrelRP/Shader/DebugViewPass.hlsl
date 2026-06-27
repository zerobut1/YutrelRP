#ifndef YUTREL_DEBUG_VIEW_PASS_INCLUDED
#define YUTREL_DEBUG_VIEW_PASS_INCLUDED

#include "Utils/GBuffer.hlsl"
#include "Utils/Shadow.hlsl"

TEXTURE2D(_ShadowMask);
SAMPLER(sampler_ShadowMask);
TEXTURE2D(_ScreenSpaceAO);
SAMPLER(sampler_ScreenSpaceAO);

int _DebugViewMode;
int _DebugViewIssue;

struct DebugViewShadowData
{
    int cascade_index;
    float strength;
};

float DebugViewFadedShadowStrength(float distance, float scale, float fade)
{
    return saturate((1.0f - distance * scale) * fade);
}

DebugViewShadowData GetDebugViewShadowData(float3 position_WS, float depth)
{
    DebugViewShadowData out_data;
    out_data.cascade_index = -1;
    out_data.strength      = DebugViewFadedShadowStrength(depth, _DirectionalShadowDistanceFade.x, _DirectionalShadowDistanceFade.y);

    for (int cascade_index = 0; cascade_index < _DirectionalShadowCascadeCount; cascade_index++)
    {
        float4 sphere  = _DirectionalShadowCascadeDatas[cascade_index].culling_sphere;
        float dist_sqr = DistanceSquared(position_WS, sphere.xyz);
        if (dist_sqr < sphere.w)
        {
            if (cascade_index == _DirectionalShadowCascadeCount - 1)
            {
                out_data.strength *= DebugViewFadedShadowStrength(
                    dist_sqr,
                    _DirectionalShadowCascadeDatas[cascade_index].data.x,
                    _DirectionalShadowDistanceFade.z);
            }

            out_data.cascade_index = cascade_index;
            break;
        }
    }
    out_data.strength = out_data.cascade_index == -1 ? 0.0f : out_data.strength;

    return out_data;
}

float4 GetCascadeColor(int cascade_index)
{
    switch (cascade_index)
    {
    case 0:
        return float4(1.0f, 0.0f, 0.0f, 1.0f);
    case 1:
        return float4(0.0f, 1.0f, 0.0f, 1.0f);
    case 2:
        return float4(0.0f, 0.0f, 1.0f, 1.0f);
    case 3:
        return float4(1.0f, 1.0f, 0.0f, 1.0f);
    default:
        return float4(0.0f, 0.0f, 0.0f, 1.0f);
    }
}

float DebugViewDither(float2 position_SS)
{
    // Stable screen-space interleaved gradient noise. Amplitude is one 8-bit
    // output step, enough to hide quantization bands without changing depth
    // ordering at debug-view scale.
    float noise = frac(52.9829189f * frac(dot(position_SS, float2(0.06711056f, 0.00583715f))));
    return (noise - 0.5f) / 255.0f;
}

float DebugViewLinearDepth01(float raw_depth)
{
#if UNITY_REVERSED_Z
    if (raw_depth <= 0.0f)
    {
        return 1.0f;
    }
#else
    if (raw_depth >= 1.0f)
    {
        return 1.0f;
    }
#endif

    float linear_depth;
    if (IsOrthographicCamera())
    {
        linear_depth = OrthographicDepthBufferToLinear(raw_depth);
    }
    else
    {
        linear_depth = LinearEyeDepth(raw_depth, _ZBufferParams);
    }

    float linear_depth_01 = saturate(linear_depth / _ProjectionParams.z);

    // Full camera-far linear depth is technically correct but often collapses
    // editor scenes to near-black when far clip is large. Keep near<far
    // ordering and white far/clear semantics while using a display curve so
    // intermediate scene depth remains visible, matching test-2's practical
    // debug-view intent.
    return sqrt(linear_depth_01);
}

bool DebugViewIsValidSurfaceDepth(float raw_depth)
{
#if UNITY_REVERSED_Z
    return raw_depth > 0.0f;
#else
    return raw_depth < 1.0f;
#endif
}

float4 SampleDebugViewGBuffer(float2 uv)
{
    EncodedGBuffer gbuffer;
    gbuffer.GBuffer_A   = SAMPLE_TEXTURE2D(_GBuffer_A, sampler_GBuffer_A, uv);
    gbuffer.GBuffer_B   = SAMPLE_TEXTURE2D(_GBuffer_B, sampler_GBuffer_B, uv);
    gbuffer.GBuffer_C   = SAMPLE_TEXTURE2D(_GBuffer_C, sampler_GBuffer_C, uv);
    gbuffer.scene_depth = 0.0f;
    gbuffer.uv          = uv;

    GBufferData gbuffer_data = DecodeGBuffer(gbuffer);
    if (gbuffer_data.shading_model_id != 1)
    {
        return float4(0.0f, 0.0f, 0.0f, 1.0f);
    }

    if (_DebugViewMode == 1)
    {
        return float4(gbuffer_data.base_color, 1.0f);
    }
    if (_DebugViewMode == 2)
    {
        return float4(gbuffer_data.roughness.xxx, 1.0f);
    }
    if (_DebugViewMode == 3)
    {
        return float4(gbuffer_data.metallic.xxx, 1.0f);
    }
    if (_DebugViewMode == 4)
    {
        return float4(gbuffer_data.specular.xxx, 1.0f);
    }
    return float4(gbuffer_data.normal_WS * 0.5f + 0.5f, 1.0f);
}

float4 SampleDebugViewAmbientOcclusion(float2 uv)
{
    float scene_depth = SAMPLE_TEXTURE2D(_SceneDepth, sampler_SceneDepth, uv).r;
    if (!DebugViewIsValidSurfaceDepth(scene_depth))
    {
        return float4(1.0f, 1.0f, 1.0f, 1.0f);
    }

    EncodedGBuffer gbuffer;
    gbuffer.GBuffer_A   = SAMPLE_TEXTURE2D(_GBuffer_A, sampler_GBuffer_A, uv);
    gbuffer.GBuffer_B   = SAMPLE_TEXTURE2D(_GBuffer_B, sampler_GBuffer_B, uv);
    gbuffer.GBuffer_C   = SAMPLE_TEXTURE2D(_GBuffer_C, sampler_GBuffer_C, uv);
    gbuffer.scene_depth = scene_depth;
    gbuffer.uv          = uv;

    GBufferData gbuffer_data = DecodeGBuffer(gbuffer);
    if (gbuffer_data.shading_model_id != 1)
    {
        return float4(1.0f, 1.0f, 1.0f, 1.0f);
    }

    float material_AO     = saturate(gbuffer_data.material_AO);
    float screen_space_AO = saturate(SAMPLE_TEXTURE2D(_ScreenSpaceAO, sampler_ScreenSpaceAO, uv).r);
    float combined_AO     = min(material_AO, screen_space_AO);
    return float4(combined_AO.xxx, 1.0f);
}

float4 DebugViewPassFragment(FullScreenVaryings input) : SV_Target
{
    if (_DebugViewIssue != 0)
    {
        return float4(1.0f, 0.0f, 1.0f, 1.0f);
    }

    switch (_DebugViewMode)
    {
    case 1:
    case 2:
    case 3:
    case 4:
    case 5:
        return SampleDebugViewGBuffer(input.uv);

    case 6:
    {
        float debug_scene_depth = DebugViewLinearDepth01(SAMPLE_TEXTURE2D(_SceneDepth, sampler_SceneDepth, input.uv).r);
        if (debug_scene_depth < 1.0f)
        {
            debug_scene_depth = saturate(debug_scene_depth + DebugViewDither(input.position_CS.xy));
        }
        return float4(debug_scene_depth.xxx, 1.0f);
    }

    case 7:
        return SAMPLE_TEXTURE2D(_ShadowMask, sampler_ShadowMask, input.uv).rrrr;

    case 8:
    {
        float scene_depth = SAMPLE_TEXTURE2D(_SceneDepth, sampler_SceneDepth, input.uv).r;
        if (scene_depth <= 0.0f || scene_depth >= 1.0f)
        {
            return float4(0.0f, 0.0f, 0.0f, 1.0f);
        }

        float3 position_WS              = ComputeWorldSpacePositionFromFullScreenUV(input.uv, scene_depth);
        float linear_depth              = LinearEyeDepth(position_WS, UNITY_MATRIX_V);
        DebugViewShadowData shadow_data = GetDebugViewShadowData(position_WS, linear_depth);
        return GetCascadeColor(shadow_data.cascade_index);
    }

    case 9:
        return SampleDebugViewAmbientOcclusion(input.uv);
    }

    return float4(0.0f, 0.0f, 0.0f, 1.0f);
}

#endif
