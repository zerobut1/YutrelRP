#ifndef YUTREL_DEFAULTLIT_INPUT_INCLUDED
#define YUTREL_DEFAULTLIT_INPUT_INCLUDED

TEXTURE2D(_BaseColorTex);
SAMPLER(sampler_BaseColorTex);
TEXTURE2D(_NormalTex);
SAMPLER(sampler_NormalTex);
TEXTURE2D(_SmoothnessTex);
SAMPLER(sampler_SmoothnessTex);
TEXTURE2D(_MetallicTex);
SAMPLER(sampler_MetallicTex);

UNITY_INSTANCING_BUFFER_START(UnityPerMaterial)
    UNITY_DEFINE_INSTANCED_PROP(float4, _BaseColorTex_ST)
    UNITY_DEFINE_INSTANCED_PROP(float, _UseAlphaClip)
    UNITY_DEFINE_INSTANCED_PROP(float4, _NormalTex_ST)
    UNITY_DEFINE_INSTANCED_PROP(float4, _SmoothnessTex_ST)
    UNITY_DEFINE_INSTANCED_PROP(float4, _MetallicTex_ST)
UNITY_INSTANCING_BUFFER_END(UnityPerMaterial)

#endif
