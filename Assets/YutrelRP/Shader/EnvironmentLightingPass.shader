Shader "YutrelRP/EnvironmentLightingPass"
{
	Properties {}

	SubShader
	{
		HLSLINCLUDE
		#include "Utils/Common.hlsl"
		ENDHLSL

		Pass
		{
			ZTest Always
			ZWrite Off
			Blend One One
			Cull Off

			HLSLPROGRAM
			#pragma target 5.0
			#pragma vertex DefaultFullScreenPassVertex
			#pragma fragment EnvironmentLightingFragment
			#include "EnvironmentLightingPass.hlsl"
			ENDHLSL
		}
	}
}
