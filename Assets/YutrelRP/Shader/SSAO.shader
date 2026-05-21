Shader "YutrelRP/SSAO"
{
	Properties {}

	SubShader
	{
		Cull Off
		ZTest Always
		ZWrite Off

		HLSLINCLUDE
		#include "Utils/Common.hlsl"
		#include "SSAOPass.hlsl"
		ENDHLSL

		Pass
		{
			Name "SSAO"

			HLSLPROGRAM
			#pragma target 5.0
			#pragma vertex DefaultFullScreenPassVertex
			#pragma fragment SSAOFragment
			ENDHLSL
		}
	}
}
