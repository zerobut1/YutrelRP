Shader "YutrelRP/Sponza/DefaultLit"
{
	Properties
	{
		_BaseColorTex ("Base Color", 2D) = "white" {}
		_NormalTex ("Normal", 2D) = "bump" {}
		_SmoothnessTex ("Smoothness", 2D) = "white" {}
		_MetallicTex ("Metallic", 2D) = "black" {}
		_MaterialAOTex ("Material AO", 2D) = "white" {}
		[Toggle] _UseAlphaClip ("Use Alpha Clip", Float) = 0
		_AlphaCutoff ("Alpha Cutoff", Range(0, 1)) = 0.5
		[Enum(Off,2,On,0)] _CullMode ("Double Face", Float) = 2
	}
	SubShader
	{

		HLSLINCLUDE
		#include "Assets/YutrelRP/Shader/Utils/Common.hlsl"
		#include "Assets/YutrelRP/Shader/DefaultLitSurfaceContract.hlsl"
		#include "Sponza_DefaultLitSurface.hlsl"
		ENDHLSL

		Pass
		{
			Tags { "LightMode" = "GBuffer" }
			Cull [_CullMode]

			HLSLPROGRAM
			#pragma target 5.0
			#pragma multi_compile_instancing
			#pragma vertex DefaultLitVertex
			#pragma fragment DefaultLitFragment
			#include "Assets/YutrelRP/Shader/DefaultLit.hlsl"
			ENDHLSL
		}

		Pass
		{
			Tags { "LightMode" = "ShadowCaster" }

			ColorMask 0
			Cull [_CullMode]

			HLSLPROGRAM
			#pragma target 5.0
			#pragma multi_compile_instancing
			#pragma vertex DefaultLitShadowCasterVertex
			#pragma fragment DefaultLitShadowCasterFragment
			#include "Assets/YutrelRP/Shader/DefaultLit.hlsl"
			ENDHLSL
		}
	}
}
