Shader "YutrelRP/ShadowMask"
{
	SubShader
	{
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
			#pragma enable_d3d11_debug_symbols
			#pragma target 5.0
			#pragma vertex ShadowMaskPassVertex
			#pragma fragment ShadowMaskPassFragment
			ENDHLSL
		}

	}
}