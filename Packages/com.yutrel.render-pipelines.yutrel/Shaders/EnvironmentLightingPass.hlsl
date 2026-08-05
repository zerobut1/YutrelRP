#ifndef YUTREL_ENVIRONMENT_LIGHTING_PASS_INCLUDED
#define YUTREL_ENVIRONMENT_LIGHTING_PASS_INCLUDED

#include "EnvironmentLighting.hlsl"

TEXTURE2D(_ScreenSpaceAO);
SAMPLER(sampler_ScreenSpaceAO);

float4 EnvironmentLightingFragment(FullScreenVaryings input) : SV_Target
{
    EncodedGBuffer gbuffer;
    gbuffer.scene_color = float4(0, 0, 0, 0);
    gbuffer.GBuffer_A   = SAMPLE_TEXTURE2D(_GBuffer_A, sampler_GBuffer_A, input.uv);
    gbuffer.GBuffer_B   = SAMPLE_TEXTURE2D(_GBuffer_B, sampler_GBuffer_B, input.uv);
    gbuffer.GBuffer_C   = SAMPLE_TEXTURE2D(_GBuffer_C, sampler_GBuffer_C, input.uv);
    gbuffer.GBuffer_D   = SAMPLE_TEXTURE2D(_GBuffer_D, sampler_GBuffer_D, input.uv);
    gbuffer.scene_depth = SAMPLE_TEXTURE2D(_SceneDepth, sampler_SceneDepth, input.uv).r;
    gbuffer.uv          = input.uv;

    GBufferData gbuffer_data = DecodeGBuffer(gbuffer);
    // v1: OpenPBR environment lighting (IBL) is not implemented yet; keep the
    // existing Standard-only path so OpenPBR pixels retain their direct light.
    if (gbuffer_data.shading_model_id != SHADING_MODEL_STANDARD)
    {
        discard;
    }

    StandardSurface surface = GBuffer2StandardSurface(gbuffer_data);
    float screen_space_AO   = saturate(SAMPLE_TEXTURE2D(_ScreenSpaceAO, sampler_ScreenSpaceAO, input.uv).r);
    float final_diffuse_AO  = min(surface.material_AO, screen_space_AO);
    float3 diffuse_lighting = EvaluateEnvironmentDiffuseSH(surface.normal_WS) * _EnvironmentIntensity *
                              _EnvironmentDiffuseMultiplier;
    EnvironmentLightingResult environment =
        EvaluateEnvironmentLighting(surface, diffuse_lighting, final_diffuse_AO, true);

    return float4(ApplyPreExposure(environment.diffuse + environment.specular), 0.0f);
}

#endif
