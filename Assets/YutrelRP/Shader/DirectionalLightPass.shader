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
			#pragma enable_d3d11_debug_symbols
			#pragma vertex DirectionalLightVertex
			#pragma fragment DirectionalLightFragment
			#include "DirectionalLightPass.hlsl"
			ENDHLSL
		}
	}
}