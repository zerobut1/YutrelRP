#ifndef YUTREL_UNLIT_INCLUDED
#define YUTREL_UNLIT_INCLUDED

#include "UnlitInput.hlsl"

struct Attributes
{
    float3 position_OS : POSITION;
    float2 uv : TEXCOORD0;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct Varyings
{
    float4 position_CS : SV_POSITION;
    float2 uv : VAR_BASE_UV;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

Varyings UnlitVertex(Attributes input)
{
    Varyings output;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);
    float3 position_WS = TransformObjectToWorld(input.position_OS.xyz);
    output.position_CS = TransformWorldToHClip(position_WS);

    float4 baseST = UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _MainTex_ST);
    output.uv = input.uv * baseST.xy + baseST.zw;

    return output;
}

float4 UnlitFragment(Varyings input) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(input);
    float4 texture_color = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
    float4 emissive = UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _Emissive);
    return texture_color * emissive;
}

#endif
