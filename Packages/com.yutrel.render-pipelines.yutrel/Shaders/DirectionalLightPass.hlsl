#ifndef YUTREL_DIRECTIONAL_LIGHT_PASS_INCLUDED
#define YUTREL_DIRECTIONAL_LIGHT_PASS_INCLUDED

#include "Utils/ShadingModelStandard.hlsl"

int _LightIndex;

// Host projects may define this guard and provide the same function before
// including this file. A handled result is already in scene-color exposure
// space; the package must not apply its Standard pre-exposure a second time.
#ifndef YUTREL_DIRECTIONAL_LIGHT_EXTENSION_INCLUDED
bool TryEvaluateDirectionalLightExtension(EncodedGBuffer encoded_gbuffer,
                                          GBufferData gbuffer_data, float4 position_cs, int light_index, out float4 color)
{
    color = 0.0f;
    return false;
}
#endif

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

    float3 out_color = float3(0, 0, 0);
    switch (gbuffer_data.shading_model_id)
    {
    case SHADING_MODEL_STANDARD:
    {
        StandardSurface surface = GBuffer2StandardSurface(gbuffer_data);
        Light light             = GetDirectionalLight(_LightIndex, gbuffer.uv);

        out_color = StandardShading(surface, light);
        break;
    }
    default:
    {
        float4 extension_color;
        if (TryEvaluateDirectionalLightExtension(
                gbuffer,
                gbuffer_data,
                input.position_CS,
                _LightIndex,
                extension_color))
        {
            return extension_color;
        }
        discard;
        break;
    }
    }

    return float4(ApplyPreExposure(out_color), 0.0f);
}

#endif
