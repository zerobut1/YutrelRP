Shader "YutrelRP/Endfield/Character"
{
	Properties
	{
		[MainTexture] _EndfieldBaseMap ("Base Map", 2D) = "white" {}
		[MainColor] _EndfieldBaseColor ("Base Color", Color) = (1, 1, 1, 1)
		[Normal] _EndfieldNormalMap ("Normal Map", 2D) = "bump" {}
		_EndfieldNormalScale ("Normal Scale", Range(0, 2)) = 1
		[NoScaleOffset] _EndfieldPackedMap ("Packed Map", 2D) = "white" {}
		[NoScaleOffset] _EndfieldColorLUT ("Material Color LUT", 2D) = "white" {}
		[Toggle(_ENDFIELD_COLOR_LUT)] _EndfieldUseColorLUT ("Use Material Color LUT", Float) = 1
		_EndfieldAlphaCutoff ("Alpha Cutoff", Range(0, 1)) = 0.177
		[Min(0)] _EndfieldDirectIntensity ("Direct Intensity", Float) = 1
		[Min(1)] _EndfieldReferenceIlluminance ("Reference Illuminance", Float) = 50000
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
		#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
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
			Name "EndfieldForward"
			Tags
			{
				"LightMode" = "EndfieldForward"
			}

			Cull Off
			ZTest Equal
			ZWrite On
			Blend Off

			HLSLPROGRAM
			#pragma target 5.0
			#pragma multi_compile_instancing
			#pragma shader_feature_local_fragment _ENDFIELD_COLOR_LUT
			#pragma vertex EndfieldCharacterForwardVertex
			#pragma fragment EndfieldCharacterForwardFragment
			#include "EndfieldCharacterForwardPass.hlsl"
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
