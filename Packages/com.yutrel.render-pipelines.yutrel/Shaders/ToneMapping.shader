Shader "YutrelRP/ToneMapping"
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
		#include "ToneMapping.hlsl"
		ENDHLSL

		Pass
		{
			Name "None"

			HLSLPROGRAM
			#pragma target 5.0
			#pragma vertex DefaultFullScreenPassVertex
			#pragma fragment CopyPassFragment
			ENDHLSL
		}

		Pass
		{
			Name "ACES"

			HLSLPROGRAM
			#pragma target 5.0
			#pragma vertex DefaultFullScreenPassVertex
			#pragma fragment ToneMappingACESFragment
			ENDHLSL
		}

	}
}