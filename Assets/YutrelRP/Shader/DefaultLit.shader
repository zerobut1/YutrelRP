Shader "YutrelRP/DefaultLit"
{
    Properties
    {
        _Emissive ("Color", Color) = (1,1,1,1)
        _MainTex ("Texture", 2D) = "white" {}
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
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float3 normal_ws : NORMAL;
                float2 uv : TEXCOORD0;
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
                float4 _Emissive;
            CBUFFER_END

            v2f vert(a2v _in)
            {
                v2f _out;

                float3 position_ws = TransformObjectToWorld(_in.position_os);
                _out.vertex = TransformWorldToHClip(position_ws);
                _out.normal_ws = TransformObjectToWorldNormal(_in.normal_os);
                _out.uv = TRANSFORM_TEX(_in.uv, _MainTex);

                return _out;
            }

            RTStruct frag(v2f _in)
            {
                RTStruct _out;

                float4 albedo = tex2D(_MainTex, _in.uv) * _Emissive;

                _out.scene_color = float4(0, 0, 0, 0);
                _out.GBuffer_A = albedo;
                _out.GBuffer_B = float4(_in.normal_ws, 0.0f);
                _out.GBuffer_C = float4(0, 0, 0, 0);

                return _out;
            }
            ENDHLSL
        }
    }
}