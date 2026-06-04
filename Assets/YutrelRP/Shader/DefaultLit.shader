Shader "YutrelRP/DefaultLit"
{
	Properties
	{
		_BaseColor ("Base Color", Color) = (0.4, 0.8, 1.0, 1)
		[Toggle] _UseAlphaClip ("Use Alpha Clip", Float) = 0
		_AlphaCutoff ("Alpha Cutoff", Range(0, 1)) = 0.5
		_Emissive ("Emissive", Color) = (0, 0, 0, 1)
		[Toggle(_USE_EMISSIVE_TEX)] _UseEmissiveTex ("Use Emissive Texture", Float) = 0
		_EmissiveTex ("Emissive", 2D) = "black" {}
		_Roughness ("Roughness", Range(0, 1)) = 0.5
		_Metallic ("Metallic", Range(0, 1)) = 0.0
		_Specular ("Specular", Range(0, 1)) = 0.5
		_MaterialAO ("Material AO", Range(0, 1)) = 1.0
		[Toggle(_USE_BASECOLOR_TEX)] _UseBaseColorTex ("Use BaseColor Texture", Float) = 0
		_BaseColorTex ("Base Color", 2D) = "white" {}
		[Toggle(_USE_NORMAL_TEX)] _UseNormalTex ("Use Normal Texture", Float) = 0
		_NormalTex ("Normal", 2D) = "bump" {}
		[Toggle(_USE_ROUGHNESS_TEX)] _UseRoughnessTex ("Use Roughness Texture", Float) = 0
		_RoughnessTex ("Roughness", 2D) = "white" {}
		[Toggle(_USE_METALLIC_TEX)] _UseMetallicTex ("Use Metallic Texture", Float) = 0
		_MetallicTex ("Metallic", 2D) = "black" {}
		[Toggle(_USE_MATERIAL_AO_TEX)] _UseMaterialAOTex ("Use Material AO Texture", Float) = 0
		_MaterialAOTex ("Material AO", 2D) = "white" {}
	}
	SubShader
	{
		HLSLINCLUDE
		#include "Utils/Common.hlsl"
		#include "DefaultLitSurfaceContract.hlsl"
		#include "StandardDefaultLitSurface.hlsl"
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
			#pragma shader_feature_local _USE_MATERIAL_AO_TEX
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
			#pragma shader_feature_local _USE_BASECOLOR_TEX
			#pragma vertex DefaultLitShadowCasterVertex
			#pragma fragment DefaultLitShadowCasterFragment
			#include "DefaultLit.hlsl"
			ENDHLSL
		}

		Pass
		{
			Name "DDGIRayTracing"
			Tags
			{
				"LightMode" = "DDGIRayTracing"
			}

			HLSLPROGRAM
			#pragma target 5.0
			#pragma multi_compile_instancing
			#pragma shader_feature_local _USE_BASECOLOR_TEX
			#pragma raytracing DDGIRayTracing
			#include "Assets/YutrelRP/Shader/DDGI/DDGITraceMaterial.hlsl"

			[shader("closesthit")]
			void DDGIRayTracingClosestHit(inout DDGIProbeTracePayload payload : SV_RayPayload,
				BuiltInTriangleIntersectionAttributes attributes)
			{
				bool uv_valid;
				float2 uv = DDGITraceMaterialHitUV(attributes, uv_valid);
				uint albedo_status = DDGITraceMaterialPassAlbedoStatus(uv_valid);
				float3 base_color = uv_valid
					? SampleStandardDefaultLitBaseColorLOD(uv, 0.0f).rgb
					: DDGITraceFallbackBaseColor();
				DDGITraceCommitClosestHit(payload, base_color, albedo_status);
			}
			ENDHLSL
		}
	}
}
