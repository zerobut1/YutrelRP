Shader "YutrelRP/Unlit"
{
	Properties
	{
		_MainTex ("MainTex", 2D) = "white" {}
		_Emissive ("Emissive", Color) = (0, 0, 0, 1)
	}

	SubShader
	{
		HLSLINCLUDE
		#include "Utils/Common.hlsl"
		#include "UnlitInput.hlsl"
		ENDHLSL

		Pass
		{
			Tags
			{
				"LightMode" = "GBuffer"
			}

			HLSLPROGRAM
			#pragma target 5.0
			#pragma multi_compile_instancing
			#pragma vertex UnlitVertex
			#pragma fragment UnlitFragment
			#include "Unlit.hlsl"
			ENDHLSL
		}
	}
}