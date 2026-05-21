Shader "YutrelRP/HBAO"
{
	Properties {}

	SubShader
	{
		Cull Off
		ZTest Always
		ZWrite Off

		HLSLINCLUDE
		#include "Utils/Common.hlsl"
		#include "HBAOPass.hlsl"
		ENDHLSL

		Pass
		{
			Name "HBAO"

			HLSLPROGRAM
			#pragma target 5.0
			#pragma vertex DefaultFullScreenPassVertex
			#pragma fragment HBAOFragment
			ENDHLSL
		}
	}
}
