Shader "YutrelRP/TempShading"
{
    Properties {}
    SubShader
    {

        Pass
        {
            Name "TempShading"

            ZWrite Off
            ZTest Always
            Blend Off
            Cull Off

            HLSLPROGRAM
            #pragma enable_d3d11_debug_symbols
            #pragma vertex vert
            #pragma fragment frag

            #include "Core.hlsl"

            sampler2D _GBuffer_A;
            sampler2D _GBuffer_B;
            sampler2D _GBuffer_C;

            CBUFFER_START(_light_buffer)
                float4 _sun_light_direction;
                float4 _sun_light_color;
            CBUFFER_END

            struct appdata
            {
                float4 vertex : POSITION;
                // float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };


            float4x4 unity_MatrixVP;
            float4x4 unity_ObjectToWorld;

            v2f vert(appdata IN)
            {
                v2f _out;
                // float4 world_pos = mul(unity_ObjectToWorld, IN.vertex);
                // _out.vertex = mul(unity_MatrixVP, world_pos);
                _out.vertex = IN.vertex;
                _out.uv = IN.vertex * float2(0.5, -0.5) + 0.5;

                return _out;
            }

            float4 frag(v2f input) : SV_Target
            {
                float2 uv = input.uv;

                float4 GBufferA = tex2D(_GBuffer_A, uv);
                float4 GBufferB = tex2D(_GBuffer_B, uv);
                float4 GBufferC = tex2D(_GBuffer_C, uv);

                float4 color = float4(GBufferA.r, GBufferB.g, GBufferC.b, 1.0);
                // return _sun_light_direction;
                return _sun_light_color * color;
                // return float4(uv, 0, 0);
            }
            ENDHLSL
        }
    }
}