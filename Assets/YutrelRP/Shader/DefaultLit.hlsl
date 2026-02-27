#ifndef YUTREL_DEFAULTLIT_INCLUDED
#define YUTREL_DEFAULTLIT_INCLUDED

#include "Utils/GBuffer.hlsl"

struct Attributes
{
    float3 position_OS : POSITION;
    float3 normal_OS : NORMAL;
    float2 uv : TEXCOORD0;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct Varyings
{
    float4 position_CS : SV_POSITION;
    float3 normal_WS : VAR_NORMAL;
    float2 uv : VAR_BASE_UV;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct RTStruct
{
    float4 scene_color : SV_Target0;
    float4 GBuffer_A : SV_Target1;
    float4 GBuffer_B : SV_Target2;
    float4 GBuffer_C : SV_Target3;
};

Varyings DefaultLitVertex(Attributes input)
{
    Varyings output;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);
    float3 position_WS = TransformObjectToWorld(input.position_OS.xyz);
    float4 position_CS = TransformWorldToHClip(position_WS);
    float3 normal_WS = TransformObjectToWorldNormal(input.normal_OS);

    output.position_CS = position_CS;
    output.normal_WS = normal_WS;
    output.uv = input.uv;
    return output;
}

RTStruct DefaultLitFragment(Varyings input)
{
    RTStruct output;
    UNITY_SETUP_INSTANCE_ID(input);

    GBufferData gbuffer;
    float4 base_color_ST = UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _BaseColorTex_ST);
    float2 base_color_uv = input.uv * base_color_ST.xy + base_color_ST.zw;
    // gbuffer.base_color = SAMPLE_TEXTURE2D(_BaseColorTex, sampler_BaseColorTex, base_color_uv).rgb;
    gbuffer.base_color = UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _BaseColor).rgb;
    gbuffer.emissive = UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _Emissive).rgb;
    gbuffer.normal_WS = input.normal_WS;
    // gbuffer.roughness = SAMPLE_TEXTURE2D(_RoughnessTex, sampler_RoughnessTex, base_color_uv).rgb;
    gbuffer.roughness = UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _Roughness);
    // gbuffer.metallic = SAMPLE_TEXTURE2D(_MetallicTex, sampler_MetallicTex, base_color_uv).r;
    gbuffer.metallic = UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _Metallic);
    gbuffer.specular = UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _Specular);
    gbuffer.shading_model_id = 1;

    EncodedGBuffer encoded_gbuffer = EncodeGBuffer(gbuffer);
    output.scene_color = encoded_gbuffer.scene_color;
    output.GBuffer_A = encoded_gbuffer.GBuffer_A;
    output.GBuffer_B = encoded_gbuffer.GBuffer_B;
    output.GBuffer_C = encoded_gbuffer.GBuffer_C;

    return output;
}

#endif
