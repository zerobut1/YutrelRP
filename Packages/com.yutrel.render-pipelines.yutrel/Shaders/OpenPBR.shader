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
        [Enum(UnityEngine.Rendering.CullMode)] _CullMode ("Cull Mode", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "YutrelPipeline"
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
            #pragma vertex DefaultLitShadowCasterVertex
            #pragma fragment DefaultLitShadowCasterFragment
            #include "DefaultLit.hlsl"
            ENDHLSL
        }
    }
}
