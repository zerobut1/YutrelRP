Shader "YutrelRP/DefaultLit"
{
	Properties
	{
		_BaseColor ("Base Color", Color) = (0.4, 0.8, 1.0, 1)
		_Emissive ("Emissive", Color) = (0, 0, 0, 1)
		[Toggle(_USE_EMISSIVE_TEX)] _UseEmissiveTex ("Use Emissive Texture", Float) = 0
		_EmissiveTex ("Emissive", 2D) = "black" {}
		_Roughness ("Roughness", Range(0, 1)) = 0.5
		_Metallic ("Metallic", Range(0, 1)) = 0.0
		_Specular ("Specular", Range(0, 1)) = 0.5
		[Toggle(_USE_BASECOLOR_TEX)] _UseBaseColorTex ("Use BaseColor Texture", Float) = 0
		_BaseColorTex ("Base Color", 2D) = "white" {}
		[Toggle(_USE_NORMAL_TEX)] _UseNormalTex ("Use Normal Texture", Float) = 0
		_NormalTex ("Normal", 2D) = "bump" {}
		[Toggle(_USE_ROUGHNESS_TEX)] _UseRoughnessTex ("Use Roughness Texture", Float) = 0
		_RoughnessTex ("Roughness", 2D) = "white" {}
		[Toggle(_USE_METALLIC_TEX)] _UseMetallicTex ("Use Metallic Texture", Float) = 0
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
			#pragma target 5.0
			#pragma multi_compile_instancing
			#pragma shader_feature_local _USE_BASECOLOR_TEX
			#pragma shader_feature_local _USE_EMISSIVE_TEX
			#pragma shader_feature_local _USE_NORMAL_TEX
			#pragma shader_feature_local _USE_ROUGHNESS_TEX
			#pragma shader_feature_local _USE_METALLIC_TEX
			#pragma vertex DefaultLitVertex
			#pragma fragment DefaultLitFragment
			#include "DefaultLit.hlsl"
			ENDHLSL
		}

		Pass
		{
			Tags
			{
				"LightMode" = "ShadowCaster"
			}

			ColorMask 0

			HLSLPROGRAM
			#pragma target 5.0
			#pragma multi_compile_instancing
			#pragma vertex ShadowCasterPassVertex
			#pragma fragment ShadowCasterPassFragment
			#include "ShadowCaster.hlsl"
			ENDHLSL
		}
	}
}
