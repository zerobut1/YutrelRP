Shader "YutrelRP/DirectionalLightPass"
{
	Properties {}

	SubShader
	{
		HLSLINCLUDE
		#include "Utils/Common.hlsl"
		ENDHLSL

		Pass
		{
			ZWrite Off
			Blend One One
			Cull Off

			HLSLPROGRAM
			#pragma target 5.0
			#pragma vertex DefaultFullScreenPassVertex
			#pragma fragment DirectionalLightFragment
			#include "DirectionalLightPass.hlsl"
			ENDHLSL
		}
	}
}