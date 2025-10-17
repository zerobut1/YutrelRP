Shader "YutrelRP/DefaultLit"
{
	Properties
	{
		_Emissive ("Emissive", Color) = (0, 0, 0, 1)
		_BaseColorTex ("BaseColor", 2D) = "white" {}
		_NormalTex ("Normal", 2D) = "blue" {}
		_RoughnessTex ("Roughness", 2D) = "white" {}
		_MetallicTex ("Metallic", 2D) = "black" {}
	}
	SubShader
	{
		HLSLINCLUDE
		#include "Utils/Common.hlsl"
		#include "DefaultLitInput.hlsl"
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
			#pragma vertex DefaultlitVertex
			#pragma fragment DefaultlitFragment
			#include "DefaultLit.hlsl"
			ENDHLSL
		}
	}
}