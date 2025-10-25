Shader "YutrelRP/DirectionalLightPass"
{
	Properties {}

	SubShader
	{
		HLSLINCLUDE
		#include "Utils/Common.hlsl"
		#include "DirectionalLightPassInput.hlsl"
		ENDHLSL

		Pass
		{
			ZWrite Off
			Blend One One
			Cull Off

			HLSLPROGRAM
			#pragma target 5.0
			#pragma vertex DirectionalLightVertex
			#pragma fragment DirectionalLightFragment
			#include "DirectionalLightPass.hlsl"
			ENDHLSL
		}
	}
}