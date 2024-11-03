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
            Blend One One
            Cull Off

            HLSLPROGRAM
            #pragma enable_d3d11_debug_symbols
            #pragma vertex vert
            #pragma fragment frag
            #include "Utils/Transformation.hlsl"

            struct a2v
            {
                float4 vertex : POSITION;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };

            CBUFFER_START(_light_buffer)
                float4 _sun_light_direction;
                float4 _sun_light_color;
            CBUFFER_END

            sampler2D _GBuffer_A;
            sampler2D _GBuffer_B;
            sampler2D _GBuffer_C;

            v2f vert(a2v _in)
            {
                v2f _out;
                _out.vertex = _in.vertex;
                _out.uv = _in.vertex * float2(0.5, -0.5) + 0.5;

                return _out;
            }

            float4 frag(v2f _in) : SV_Target
            {
                float4 GBuffer_A = tex2D(_GBuffer_A, _in.uv);
                float4 GBuffer_B = tex2D(_GBuffer_B, _in.uv);
                float4 GBuffer_C = tex2D(_GBuffer_C, _in.uv);

                // float4 color = float4(GBufferA.r, GBufferB.g, GBufferC.b, 1.0);
                // return _sun_light_direction;
                // return _sun_light_color * color;
                // return float4(uv, 0, 0);
                // return GBufferB;
                return float4(GBuffer_A.rgb * _sun_light_color, 1.0);
            }
            ENDHLSL
        }
    }
}