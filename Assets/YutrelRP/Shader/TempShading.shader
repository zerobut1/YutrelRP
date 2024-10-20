Shader "YutrelRP/TempShading"
{
    Properties {}

    SubShader
    {

        Pass
        {
            Name "TempShading"

            ZWrite Off ZTest Always Blend Off Cull Off

            HLSLPROGRAM
            #pragma enable_d3d11_debug_symbols
            #pragma vertex Vert
            #pragma fragment frag

            #include "Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            sampler2D _GBuffer_A;
            sampler2D _GBuffer_B;
            sampler2D _GBuffer_C;

            float4 frag(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;

                float4 GBufferA = tex2D(_GBuffer_A, uv);
                float4 GBufferB = tex2D(_GBuffer_B, uv);
                float4 GBufferC = tex2D(_GBuffer_C, uv);

                
                return float4(GBufferA.r, GBufferB.g, GBufferC.b, 1.0);
            }
            ENDHLSL
        }
    }
}