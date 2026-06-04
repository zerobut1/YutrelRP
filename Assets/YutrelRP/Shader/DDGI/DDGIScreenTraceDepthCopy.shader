Shader "YutrelRP/DDGIScreenTraceDepthCopy"
{
    SubShader
    {
        Pass
        {
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma target 5.0
            #pragma vertex DefaultFullScreenPassVertex
            #pragma fragment Fragment
            #include "Assets/YutrelRP/Shader/Utils/Common.hlsl"

            TEXTURE2D(_SceneDepth);
            SAMPLER(sampler_SceneDepth);

            float Fragment(FullScreenVaryings input) : SV_Target
            {
                return SAMPLE_TEXTURE2D(_SceneDepth, sampler_SceneDepth, input.uv).r;
            }
            ENDHLSL
        }
    }
}
