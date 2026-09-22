#ifndef YUTREL_DIRECTIONAL_LIGHT_PASS_INCLUDED
#define YUTREL_DIRECTIONAL_LIGHT_PASS_INCLUDED

#include "Utils/ShadingModelStandard.hlsl"

int _LightIndex;

float4 DirectionalLightFragment(FullScreenVaryings input) : SV_Target
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

    if (gbuffer_data.shading_model_id != SHADING_MODEL_STANDARD)
    {
        discard;
    }

    StandardSurface surface = GBuffer2StandardSurface(gbuffer_data);
    Light light             = GetDirectionalLight(_LightIndex, gbuffer.uv);
    float3 out_color        = StandardShading(surface, light);

    return float4(ApplyPreExposure(out_color), 0.0f);
}

#endif
