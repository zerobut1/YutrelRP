Shader "YutrelRP/Unlit"
{
    Properties
    {
        _Emissive("Emissive", Color) = (0, 0, 0, 1)
    }

    SubShader
    {
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
                v2f _out;
                float4 world_pos = mul(unity_ObjectToWorld, IN.positionOS);
                _out.positionCS = mul(unity_MatrixVP, world_pos);

                return _out;
            }

            float4 _Emissive;

            RTStruct frag(v2f IN)
            {
                RTStruct _out;

                _out.GBufferA = _Emissive.rrrr;
                _out.GBufferB = _Emissive.gggg;
                _out.GBufferC = _Emissive.bbbb;

                return _out;
            }
            ENDHLSL
        }
    }
}