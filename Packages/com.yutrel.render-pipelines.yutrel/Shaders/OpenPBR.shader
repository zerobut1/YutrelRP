Shader "YutrelRP/OpenPBR"
{
    Properties
    {
        _OpenPBRBaseWeight ("Base Weight", Range(0, 1)) = 1
        _OpenPBRBaseColor ("Base Color", Color) = (0.8, 0.8, 0.8, 1)
        _OpenPBRBaseMetalness ("Base Metalness", Range(0, 1)) = 0
        _OpenPBRBaseDiffuseRoughness ("Base Diffuse Roughness", Range(0, 1)) = 0
        [Min(0)] _OpenPBRSpecularWeight ("Specular Weight", Float) = 1
        _OpenPBRSpecularColor ("Specular Color", Color) = (1, 1, 1, 1)
        _OpenPBRSpecularRoughness ("Specular Roughness", Range(0, 1)) = 0.3
        _OpenPBRSpecularRoughnessAnisotropy ("Specular Roughness Anisotropy", Range(0, 1)) = 0
        [Min(0.001)] _OpenPBRSpecularIOR ("Specular IOR", Float) = 1.5
        [Toggle] _OpenPBRUseAlphaClip ("Use Alpha Clip", Float) = 0
        _OpenPBRAlphaCutoff ("Alpha Cutoff", Range(0, 1)) = 0.5
        [Toggle(_USE_BASECOLOR_TEX)] _OpenPBRUseBaseColorTex ("Use BaseColor Texture", Float) = 0
        _OpenPBRBaseColorTex ("Base Color", 2D) = "white" {}
        [Toggle(_USE_NORMAL_TEX)] _OpenPBRUseNormalTex ("Use Normal Texture", Float) = 0
        _OpenPBRNormalTex ("Normal", 2D) = "bump" {}
        [Toggle(_USE_ROUGHNESS_TEX)] _OpenPBRUseRoughnessTex ("Use Roughness Texture", Float) = 0
        _OpenPBRRoughnessTex ("Roughness", 2D) = "white" {}
        [Toggle(_USE_METALLIC_TEX)] _OpenPBRUseMetalnessTex ("Use Metallic Texture", Float) = 0
        _OpenPBRMetalnessTex ("Metallic", 2D) = "white" {}
        [Toggle(_USE_MATERIAL_AO_TEX)] _OpenPBRUseMaterialAOTex ("Use Material AO Texture", Float) = 0
        _OpenPBRMaterialAOTex ("Material AO", 2D) = "white" {}
        [Enum(UnityEngine.Rendering.CullMode)] _CullMode ("Cull Mode", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "YutrelPipeline"
            "YutrelMaterialType" = "OpenPBR"
        }

        HLSLINCLUDE
        #include "Utils/Common.hlsl"
        #include "DefaultLitSurfaceContract.hlsl"
        #include "OpenPBRDefaultLitSurface.hlsl"
        ENDHLSL

        Pass
        {
            Tags
            {
                "LightMode" = "GBuffer"
            }

            Cull [_CullMode]

            HLSLPROGRAM
            #pragma target 5.0
            #pragma multi_compile_instancing
            #pragma shader_feature_local _USE_BASECOLOR_TEX
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

            Cull [_CullMode]
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
            Name "DDGIProbeTrace"
            Tags
            {
                "LightMode" = "DDGIProbeTrace"
            }

            HLSLPROGRAM
            #pragma target 5.0
            #pragma multi_compile_instancing
            #pragma shader_feature_local_raytracing _USE_BASECOLOR_TEX
            #pragma shader_feature_local_raytracing _USE_NORMAL_TEX
            #pragma raytracing DDGIProbeTrace
            #include "DefaultLitRayTracing.hlsl"
            ENDHLSL
        }
    }
}
