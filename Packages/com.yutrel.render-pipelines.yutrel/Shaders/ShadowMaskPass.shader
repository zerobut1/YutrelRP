Shader "YutrelRP/ShadowMask"
{
	SubShader
	{
		Tags
		{
			"RenderPipeline" = "YutrelPipeline"
		}

		Cull Off
		ZTest Always
		ZWrite Off

		HLSLINCLUDE
		#include "Utils/Common.hlsl"
		#include "ShadowMaskPass.hlsl"
		ENDHLSL

		Pass
		{
			HLSLPROGRAM
			#pragma target 5.0
			#pragma vertex DefaultFullScreenPassVertex
			#pragma fragment ShadowMaskPassFragment
			#pragma multi_compile_local_fragment _DIRECTIONAL_SHADOW_FILTER_NONE _DIRECTIONAL_SHADOW_FILTER_LOW _DIRECTIONAL_SHADOW_FILTER_MEDIUM _DIRECTIONAL_SHADOW_FILTER_HIGH
			ENDHLSL
		}

	}
}
