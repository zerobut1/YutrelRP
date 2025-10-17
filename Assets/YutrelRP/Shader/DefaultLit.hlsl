#ifndef YUTREL_DEFAULTLIT_INCLUDED
#define YUTREL_DEFAULTLIT_INCLUDED

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

Varyings DefaultlitVertex(Attributes input)
{
    Varyings output;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);
    float3 position_WS = TransformObjectToWorld(input.position_OS.xyz);
    float4 positon_CS = TransformWorldToHClip(position_WS);
    float3 normal_WS = TransformObjectToWorldNormal(input.normal_OS);
    float4 base_ST = UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _BaseColorTex_ST);

    output.position_CS = positon_CS;
    output.normal_WS = normal_WS;
    output.uv = input.uv * base_ST.xy + base_ST.zw;
    return output;
}

RTStruct DefaultlitFragment(Varyings input)
{
    RTStruct output;
    UNITY_SETUP_INSTANCE_ID(input);
    float3 texture_color = SAMPLE_TEXTURE2D(_BaseColorTex, sampler_BaseColorTex, input.uv).rgb;
    float3 emissive = UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _Emissive).rgb;
    float3 normal_WS = normalize(input.normal_WS);

    output.scene_color = float4(texture_color * emissive, 1.0f);
    output.GBuffer_A = float4(texture_color.rgb, 1.0f);
    output.GBuffer_B = float4(normal_WS, 0.0f);
    output.GBuffer_C = float4(0, 0, 0, 0);

    return output;
}

#endif
