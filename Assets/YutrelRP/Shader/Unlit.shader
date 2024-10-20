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

            struct v2f
            {
                float4 positionCS : SV_POSITION;
            };

            struct RTStruct
            {
                float4 GBufferA : SV_Target0;
                float4 GBufferB : SV_Target1;
                float4 GBufferC : SV_Target2;
            };

            v2f vert(Attributes IN)
            {
                v2f OUT;
                float4 worldPos = mul(unity_ObjectToWorld, IN.positionOS);
                OUT.positionCS = mul(unity_MatrixVP, worldPos);

                return OUT;
            }

            RTStruct frag(v2f IN)
            {
                RTStruct o;

                o.GBufferA = float4(0.4, 0.4, 0.4, 0.4);
                o.GBufferB = float4(0.8, 0.8, 0.8, 0.8);
                o.GBufferC = float4(1.0, 1.0, 1.0, 1.0);

                return o;
            }
            ENDHLSL
        }
    }
}