Shader "YutrelRP/Endfield/Character"
{
	Properties
	{
		[MainTexture] _EndfieldBaseMap ("Base Map", 2D) = "white" {}
		[MainColor] _EndfieldBaseColor ("Base Color", Color) = (1, 1, 1, 1)
		[Normal] _EndfieldNormalMap ("Normal Map", 2D) = "bump" {}
		_EndfieldNormalScale ("Normal Scale", Range(0, 2)) = 1
		_EndfieldAlphaCutoff ("Alpha Cutoff", Range(0, 1)) = 0.177
	}

	SubShader
	{
		Tags
		{
			"RenderType" = "TransparentCutout"
			"Queue" = "AlphaTest"
		}

		HLSLINCLUDE
		#include "../Utils/Common.hlsl"
		#include "EndfieldCharacterInput.hlsl"
		#include "EndfieldCharacterSurface.hlsl"
		ENDHLSL

		Pass
		{
			Name "EndfieldBase"
			Tags
			{
				"LightMode" = "EndfieldBase"
			}

			Cull Off
			ZWrite On

			HLSLPROGRAM
			#pragma target 5.0
			#pragma multi_compile_instancing
			#pragma vertex EndfieldCharacterBaseVertex
			#pragma fragment EndfieldCharacterBaseFragment
			#include "EndfieldCharacterBasePass.hlsl"
			ENDHLSL
		}

		Pass
		{
			Name "ShadowCaster"
			Tags
			{
				"LightMode" = "ShadowCaster"
			}

			Cull Off
			ZWrite On
			ColorMask 0

			HLSLPROGRAM
			#pragma target 5.0
			#pragma multi_compile_instancing
			#pragma vertex EndfieldCharacterShadowCasterVertex
			#pragma fragment EndfieldCharacterShadowCasterFragment
			#include "EndfieldCharacterShadowCasterPass.hlsl"
			ENDHLSL
		}
	}

	FallBack Off
}
