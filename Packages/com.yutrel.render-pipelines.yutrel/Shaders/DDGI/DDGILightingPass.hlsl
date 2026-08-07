#ifndef YUTREL_DDGI_LIGHTING_PASS_INCLUDED
#define YUTREL_DDGI_LIGHTING_PASS_INCLUDED

#include "DDGILighting.hlsl"

TEXTURE2D(_ScreenSpaceAO);
SAMPLER(sampler_ScreenSpaceAO);

float4 DDGILightingFragment(FullScreenVaryings input) : SV_Target
{
    EncodedGBuffer gbuffer;
    gbuffer.scene_color = float4(0, 0, 0, 0);
    gbuffer.GBuffer_A   = SAMPLE_TEXTURE2D(_GBuffer_A, sampler_GBuffer_A, input.uv);
    gbuffer.GBuffer_B   = SAMPLE_TEXTURE2D(_GBuffer_B, sampler_GBuffer_B, input.uv);
    gbuffer.GBuffer_C   = SAMPLE_TEXTURE2D(_GBuffer_C, sampler_GBuffer_C, input.uv);
    gbuffer.GBuffer_D   = SAMPLE_TEXTURE2D(_GBuffer_D, sampler_GBuffer_D, input.uv);
    gbuffer.scene_depth = SAMPLE_TEXTURE2D(_SceneDepth, sampler_SceneDepth, input.uv).r;
    gbuffer.uv          = input.uv;

    GBufferData gbufferData = DecodeGBuffer(gbuffer);
    float screen_space_AO    = saturate(SAMPLE_TEXTURE2D(_ScreenSpaceAO, sampler_ScreenSpaceAO, input.uv).r);
    if (gbufferData.shading_model_id == SHADING_MODEL_STANDARD)
    {
        StandardSurface surface = GBuffer2StandardSurface(gbufferData);
        float final_diffuse_AO  = min(surface.material_AO, screen_space_AO);
        float3 diffuse          = surface.diffuse_color * EvaluateDDGIDiffuseLighting(surface) * final_diffuse_AO;
        return float4(ApplyPreExposure(diffuse), 0.0f);
    }

    if (gbufferData.shading_model_id == SHADING_MODEL_OPENPBR)
    {
        OpenPBRSurface surface  = GBuffer2OpenPBRSurface(gbufferData);
        float3 diffuse_lighting = EvaluateDDGIDiffuseLighting(surface);
        float final_diffuse_AO  = min(saturate(gbufferData.material_AO), screen_space_AO);
        float3 diffuse          = OpenPBREvaluateDiffuseIndirect(surface, diffuse_lighting) * final_diffuse_AO;
        return float4(ApplyPreExposure(diffuse), 0.0f);
    }

    discard;
    return 0.0f;
}

#endif
