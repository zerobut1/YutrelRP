#ifndef YUTREL_DEFAULTLIT_INCLUDED
#define YUTREL_DEFAULTLIT_INCLUDED

#include "Utils/GBuffer.hlsl"

struct Attributes
{
    float3 position_OS : POSITION;
    float3 normal_OS : NORMAL;
    float4 tangent_OS : TANGENT;
    float2 uv : TEXCOORD0;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct Varyings
{
    float4 position_CS : SV_POSITION;
    float3 normal_WS : VAR_NORMAL;
    float3 tangent_WS : VAR_TANGENT;
    float3 bitangent_WS : VAR_BITANGENT;
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
    float3 tangent_WS = normalize(TransformObjectToWorldDir(input.tangent_OS.xyz));
    float tangent_sign = input.tangent_OS.w * GetOddNegativeScale();
    float3 bitangent_WS = normalize(cross(normal_WS, tangent_WS) * tangent_sign);

    output.position_CS = position_CS;
    output.normal_WS = normal_WS;
    output.tangent_WS = tangent_WS;
    output.bitangent_WS = bitangent_WS;
    output.uv = input.uv;
    return output;
}

RTStruct DefaultLitFragment(Varyings input)
{
    RTStruct output;
    UNITY_SETUP_INSTANCE_ID(input);

    GBufferData gbuffer;
#if defined(_USE_BASECOLOR_TEX)
    float4 base_color_ST = UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _BaseColorTex_ST);
    float2 base_color_uv = input.uv * base_color_ST.xy + base_color_ST.zw;
    gbuffer.base_color = SAMPLE_TEXTURE2D(_BaseColorTex, sampler_BaseColorTex, base_color_uv).rgb;
#else
    gbuffer.base_color = UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _BaseColor).rgb;
#endif

#if defined(_USE_EMISSIVE_TEX)
    float4 emissive_ST = UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _EmissiveTex_ST);
    float2 emissive_uv = input.uv * emissive_ST.xy + emissive_ST.zw;
    gbuffer.emissive = SAMPLE_TEXTURE2D(_EmissiveTex, sampler_EmissiveTex, emissive_uv).rgb;
#else
    gbuffer.emissive = UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _Emissive).rgb;
#endif

#if defined(_USE_NORMAL_TEX)
    float4 normal_ST = UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _NormalTex_ST);
    float2 normal_uv = input.uv * normal_ST.xy + normal_ST.zw;
    float4 packed_normal = SAMPLE_TEXTURE2D(_NormalTex, sampler_NormalTex, normal_uv);
    float3 normal_TS = UnpackNormal(packed_normal);
    gbuffer.normal_WS = normalize(
        normal_TS.x * input.tangent_WS +
        normal_TS.y * input.bitangent_WS +
        normal_TS.z * input.normal_WS
    );
#else
    gbuffer.normal_WS = input.normal_WS;
#endif

#if defined(_USE_ROUGHNESS_TEX)
    float4 roughness_ST = UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _RoughnessTex_ST);
    float2 roughness_uv = input.uv * roughness_ST.xy + roughness_ST.zw;
    gbuffer.roughness = SAMPLE_TEXTURE2D(_RoughnessTex, sampler_RoughnessTex, roughness_uv).r;
#else
    gbuffer.roughness = UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _Roughness);
#endif

#if defined(_USE_METALLIC_TEX)
    float4 metallic_ST = UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _MetallicTex_ST);
    float2 metallic_uv = input.uv * metallic_ST.xy + metallic_ST.zw;
    gbuffer.metallic = SAMPLE_TEXTURE2D(_MetallicTex, sampler_MetallicTex, metallic_uv).r;
#else
    gbuffer.metallic = UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _Metallic);
#endif

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
