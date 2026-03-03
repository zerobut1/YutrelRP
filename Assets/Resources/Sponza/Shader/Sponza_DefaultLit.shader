Shader "YutrelRP/Sponza/DefaultLit"
{
	Properties
	{
		_BaseColorTex ("Base Color", 2D) = "white" {}
		_NormalTex ("Normal", 2D) = "bump" {}
		_SmoothnessTex ("Smoothness", 2D) = "white" {}
		_MetallicTex ("Metallic", 2D) = "black" {}
		[Toggle] _UseAlphaClip ("Use Alpha Clip", Float) = 0
		[Enum(Off,2,On,0)] _CullMode ("Double Face", Float) = 2
	}
	SubShader
	{

		HLSLINCLUDE
		#include "Assets/YutrelRP/Shader/Utils/Common.hlsl"
		#include "DefaultLitInput.hlsl"
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
			#include "DefaultLit.hlsl"
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
			#include "DefaultLit.hlsl"
			ENDHLSL
		}
	}
}
