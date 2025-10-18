#ifndef YUTREL_DIRECTIONAL_LIGHT_PASS_INCLUDED
#define YUTREL_DIRECTIONAL_LIGHT_PASS_INCLUDED

#include "Utils/Lighting.hlsl"

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
    float4 GBuffer_A = tex2D(_GBuffer_A, input.uv);
    float4 GBuffer_B = tex2D(_GBuffer_B, input.uv);
    float4 GBuffer_C = tex2D(_GBuffer_C, input.uv);
    float device_z = tex2D(_SceneDepth, input.uv).r;


    // decode gbuffer
    float ShadingModel = GBuffer_A.a;
    clip(ShadingModel - 0.5f);

    Surface surface;
    surface.base_color = GBuffer_A.rgb;
    surface.normal = GBuffer_B.xyz * 2.0f - 1.0f;

    float3 out_color = GetLighting(surface);

    return float4(out_color, 1.0f);
}

#endif
