Shader "YutrelRP/DebugView"
{
	SubShader
	{
		Cull Off
		ZTest Always
		ZWrite Off

		HLSLINCLUDE
		#include "Utils/Common.hlsl"
		#include "DebugViewPass.hlsl"
		ENDHLSL

		Pass
		{
			HLSLPROGRAM
			#pragma target 5.0
			#pragma vertex DefaultFullScreenPassVertex
			#pragma fragment DebugViewPassFragment
			ENDHLSL
		}
	}
}