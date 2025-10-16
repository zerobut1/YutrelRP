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
			#pragma enable_d3d11_debug_symbols
#pragma multi_compile_instancing
#pragma vertex UnlitVertex
#pragma fragment UnlitFragment
#include "Unlit.hlsl"
			ENDHLSL
		}
	}
}