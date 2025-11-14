#ifndef YUTREL_DIRECTIONAL_LIGHT_PASS_INCLUDED
#define YUTREL_DIRECTIONAL_LIGHT_PASS_INCLUDED

#include "Utils/ShadingModelStandard.hlsl"

struct Attributes
{
    float4 position_OS : POSITION;
};

struct Varyings
{
    float4 position_CS : SV_POSITION;
    float2 uv : TEXCOORD0;
};

Varyings DirectionalLightVertex(Attributes input)
{
    Varyings output;
    output.position_CS = input.position_OS;
    output.uv = input.position_OS * float2(0.5, -0.5) + 0.5;
    return output;
}

float4 DirectionalLightFragment(Varyings input) : SV_Target
{
    EncodedGBuffer gbuffer;
    gbuffer.scene_color = float4(0, 0, 0, 0);
    gbuffer.GBuffer_A = tex2D(_GBuffer_A, input.uv);
    gbuffer.GBuffer_B = tex2D(_GBuffer_B, input.uv);
    gbuffer.GBuffer_C = tex2D(_GBuffer_C, input.uv);
    gbuffer.scene_depth = tex2D(_SceneDepth, input.uv).r;
    gbuffer.uv = input.uv;

    GBufferData gbuffer_data = DecodeGBuffer(gbuffer);

    float3 out_color = float3(0, 0, 0);
    switch (gbuffer_data.shading_model_id)
    {
    case 1:
        StandardSurface surface = GBuffer2StandardSurface(gbuffer_data);
        Light light = GetDirectionalLight(0, gbuffer.uv);

        out_color = StandardShading(surface, light);
        break;
    default:
        discard;
        break;
    }

    return float4(out_color, 0.0f);
}

#endif
