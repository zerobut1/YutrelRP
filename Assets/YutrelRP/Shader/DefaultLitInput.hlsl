#ifndef YUTREL_DEFAULTLIT_INPUT_INCLUDED
#define YUTREL_DEFAULTLIT_INPUT_INCLUDED

TEXTURE2D(_BaseColorTex);
SAMPLER(sampler_BaseColorTex);
TEXTURE2D(_NormalTex);
SAMPLER(sampler_NormalTex);
TEXTURE2D(_RoughnessTex);
SAMPLER(sampler_RoughnessTex);
TEXTURE2D(_MetallicTex);
SAMPLER(sampler_MetallicTex);

UNITY_INSTANCING_BUFFER_START(UnityPerMaterial)
    UNITY_DEFINE_INSTANCED_PROP(float4, _BaseColor)
    UNITY_DEFINE_INSTANCED_PROP(float4, _Emissive)
    UNITY_DEFINE_INSTANCED_PROP(float, _Roughness)
    UNITY_DEFINE_INSTANCED_PROP(float, _Metallic)
    UNITY_DEFINE_INSTANCED_PROP(float, _Specular)
    UNITY_DEFINE_INSTANCED_PROP(float4, _BaseColorTex_ST)
    UNITY_DEFINE_INSTANCED_PROP(float4, _NormalTex_ST)
    UNITY_DEFINE_INSTANCED_PROP(float4, _RoughnessTex_ST)
    UNITY_DEFINE_INSTANCED_PROP(float4, _MetallicTex_ST)
UNITY_INSTANCING_BUFFER_END(UnityPerMaterial)

#endif
