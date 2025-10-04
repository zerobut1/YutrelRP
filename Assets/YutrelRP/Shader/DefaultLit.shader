Shader "YutrelRP/DefaultLit"
{
    Properties
    {
        _Emissive ("Color", Color) = (0, 0, 0, 1)
        _MainTex ("BaseColor", 2D) = "white" {}
        _NormalTex ("Normal", 2D) = "blue" {}
        _RoughnessTex ("Roughness", 2D) = "white" {}
        _MetallicTex ("Metallic", 2D) = "black" {}
    }
    SubShader
    {
        Tags
        {
            "RenderType"="Opaque"
        }
        LOD 100

        Pass
        {
            Tags
            {
                "LightMode" = "GBuffer"
            }

            HLSLPROGRAM
            #pragma enable_d3d11_debug_symbols
#pragma vertex vert
#pragma fragment frag
#include "Utils/Transformation.hlsl"

            struct a2v
            {
                float4 position_os : POSITION;
                float3 normal_os : NORMAL;
                float3 tangent_os : TANGENT;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float3 normal_ws : NORMAL;
                float3 tangent_ws : TANGENT;
                float2 uv : TEXCOORD0;
                float3 position_ws : TEXCOORD2;
            };

            struct RTStruct
            {
                float4 scene_color : SV_Target0;
                float4 GBuffer_A : SV_Target1;
                float4 GBuffer_B : SV_Target2;
                float4 GBuffer_C : SV_Target3;
            };

            CBUFFER_START(UnityPerMaterial)
                sampler2D _MainTex;
                float4 _MainTex_ST;
                sampler2D _NormalTex;
                sampler2D _RoughnessTex;
                sampler2D _MetallicTex;
                float4 _Emissive;
            CBUFFER_END

            v2f vert(a2v _in)
            {
                v2f _out;

                float3 position_ws = TransformObjectToWorld(_in.position_os);
                _out.vertex = TransformWorldToHClip(position_ws);
                _out.normal_ws = TransformObjectToWorldNormal(_in.normal_os);
                _out.tangent_ws = TransformObjectToWorldNormal(_in.tangent_os);
                _out.uv = TRANSFORM_TEX(_in.uv, _MainTex);
                _out.position_ws = position_ws;

                return _out;
            }

            RTStruct frag(v2f _in)
            {
                RTStruct _out;

                float4 albedo = tex2D(_MainTex, _in.uv);
                float3 normal = tex2D(_NormalTex, _in.uv).bgr * 2.0 - 1.0;
                float3x3 tangent_to_wolrd = CreateTangentToWorld(_in.normal_ws, _in.tangent_ws, -1.0f);
                normal = TransformTangentToWorld(normal, tangent_to_wolrd);

                float roughness = tex2D(_RoughnessTex, _in.uv).r;
                float metallic = tex2D(_MetallicTex, _in.uv).r;

                _out.scene_color = _Emissive;
                _out.GBuffer_A = albedo;
                _out.GBuffer_B = float4(normal, 0.0f);
                _out.GBuffer_C = float4(roughness, metallic, 0, 0);

                return _out;
            }
            ENDHLSL
        }
    }
}