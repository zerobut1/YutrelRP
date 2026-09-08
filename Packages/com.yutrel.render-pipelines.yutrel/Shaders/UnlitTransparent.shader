Shader "YutrelRP/Unlit Transparent"
{
    Properties
    {
        _MainTex ("Main Texture", 2D) = "white" {}
        [HDR] _Color ("Color", Color) = (1, 1, 1, 1)
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Source RGB Blend", Float) = 5
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Destination RGB Blend", Float) = 10
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlendAlpha ("Source Alpha Blend", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlendAlpha ("Destination Alpha Blend", Float) = 10
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("Depth Test", Float) = 4
        [Toggle] _ZWrite ("Depth Write", Float) = 0
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
    }
    SubShader
    {
        Tags { "RenderPipeline" = "YutrelPipeline" "RenderType" = "Transparent" "Queue" = "Transparent" }
        Pass
        {
            Name "UnlitTransparentForward"
            Tags { "LightMode" = "YutrelForwardOnly" }
            Blend [_SrcBlend] [_DstBlend], [_SrcBlendAlpha] [_DstBlendAlpha]
            ZTest [_ZTest]
            ZWrite [_ZWrite]
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 5.0
            #pragma vertex UnlitTransparentVertex
            #pragma fragment UnlitTransparentFragment
            #include "Packages/com.yutrel.render-pipelines.yutrel/Shaders/Utils/Common.hlsl"
            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _Color;
                float _SrcBlend, _DstBlend, _SrcBlendAlpha, _DstBlendAlpha;
                float _ZTest, _ZWrite, _Cull;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };
            Varyings UnlitTransparentVertex(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformWorldToHClip(TransformObjectToWorld(input.positionOS));
                output.uv = input.uv * _MainTex_ST.xy + _MainTex_ST.zw;
                return output;
            }
            float4 UnlitTransparentFragment(Varyings input) : SV_Target
            {
                float4 color = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv) * _Color;
                // Scene-linear RGB follows camera exposure; alpha is coverage.
                return float4(ApplyPreExposure(color.rgb), color.a);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
