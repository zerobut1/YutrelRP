Shader "YutrelRP/GTAO"
{
	Properties {}

	SubShader
	{
		Cull Off
		ZTest Always
		ZWrite Off

		HLSLINCLUDE
		#include "Utils/Common.hlsl"
		#include "GTAOPass.hlsl"
		ENDHLSL

		Pass
		{
			Name "GTAO"

			HLSLPROGRAM
			#pragma target 5.0
			#pragma vertex DefaultFullScreenPassVertex
			#pragma fragment GTAOFragment
			ENDHLSL
		}
	}
}
