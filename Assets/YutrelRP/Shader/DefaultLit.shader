Shader "YutrelRP/DefaultLit"
{
	Properties
	{
		_BaseColor ("Base Color", Color) = (0.4, 0.8, 1.0, 1)
		_Emissive ("Emissive", Color) = (0, 0, 0, 1)
		_Roughness ("Roughness", Range(0, 1)) = 0.5
		_Metallic ("Metallic", Range(0, 1)) = 0.0
		_Specular ("Specular", Range(0, 1)) = 0.5
		_BaseColorTex ("Base Color", 2D) = "white" {}
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
			#pragma target 5.0
			#pragma multi_compile_instancing
			#pragma vertex DefaultlitVertex
			#pragma fragment DefaultlitFragment
			#include "DefaultLit.hlsl"
			ENDHLSL
		}
	}
}