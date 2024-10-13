Shader "YutrelRP/Unlit"
{
    Properties {}

    SubShader
    {
        Pass
        {
            Tags
            {
                "LightMode" = "ExampleLightModeTag"
            }

            HLSLPROGRAM
            #pragma enable_d3d11_debug_symbols
            #pragma vertex vert
            #pragma fragment frag

            float4x4 unity_MatrixVP;
            float4x4 unity_ObjectToWorld;

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                float4 worldPos = mul(unity_ObjectToWorld, IN.positionOS);
                OUT.positionCS = mul(unity_MatrixVP, worldPos);

                return OUT;
            }

            float4 frag(Varyings IN) : SV_TARGET
            {
                return float4(0.4, 0.8, 1.0, 1.0);
            }
            ENDHLSL
        }
    }
}