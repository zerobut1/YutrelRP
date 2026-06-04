#ifndef YUTREL_STANDARD_DEFAULTLIT_SURFACE_INCLUDED
#define YUTREL_STANDARD_DEFAULTLIT_SURFACE_INCLUDED

TEXTURE2D(_BaseColorTex);
SAMPLER(sampler_BaseColorTex);
TEXTURE2D(_EmissiveTex);
SAMPLER(sampler_EmissiveTex);
TEXTURE2D(_NormalTex);
SAMPLER(sampler_NormalTex);
TEXTURE2D(_RoughnessTex);
SAMPLER(sampler_RoughnessTex);
TEXTURE2D(_MetallicTex);
SAMPLER(sampler_MetallicTex);
TEXTURE2D(_MaterialAOTex);
SAMPLER(sampler_MaterialAOTex);

UNITY_INSTANCING_BUFFER_START(UnityPerMaterial)
UNITY_DEFINE_INSTANCED_PROP(float4, _BaseColor)
UNITY_DEFINE_INSTANCED_PROP(float, _UseAlphaClip)
UNITY_DEFINE_INSTANCED_PROP(float, _AlphaCutoff)
UNITY_DEFINE_INSTANCED_PROP(float4, _Emissive)
UNITY_DEFINE_INSTANCED_PROP(float, _Roughness)
UNITY_DEFINE_INSTANCED_PROP(float, _Metallic)
UNITY_DEFINE_INSTANCED_PROP(float, _Specular)
UNITY_DEFINE_INSTANCED_PROP(float, _MaterialAO)
UNITY_DEFINE_INSTANCED_PROP(float, _UseBaseColorTex)
UNITY_DEFINE_INSTANCED_PROP(float, _UseEmissiveTex)
UNITY_DEFINE_INSTANCED_PROP(float, _UseNormalTex)
UNITY_DEFINE_INSTANCED_PROP(float, _UseRoughnessTex)
UNITY_DEFINE_INSTANCED_PROP(float, _UseMetallicTex)
UNITY_DEFINE_INSTANCED_PROP(float, _UseMaterialAOTex)
UNITY_DEFINE_INSTANCED_PROP(float4, _BaseColorTex_ST)
UNITY_DEFINE_INSTANCED_PROP(float4, _EmissiveTex_ST)
UNITY_DEFINE_INSTANCED_PROP(float4, _NormalTex_ST)
UNITY_DEFINE_INSTANCED_PROP(float4, _RoughnessTex_ST)
UNITY_DEFINE_INSTANCED_PROP(float4, _MetallicTex_ST)
UNITY_DEFINE_INSTANCED_PROP(float4, _MaterialAOTex_ST)
UNITY_INSTANCING_BUFFER_END(UnityPerMaterial)

float4 SampleStandardDefaultLitBaseColor(float2 uv)
{
#if defined(_USE_BASECOLOR_TEX)
    float4 base_color_ST = UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _BaseColorTex_ST);
    float2 base_color_uv = TransformDefaultLitTextureUV(uv, base_color_ST);
    return SAMPLE_TEXTURE2D(_BaseColorTex, sampler_BaseColorTex, base_color_uv);
#else
    return UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _BaseColor);
#endif
}

float4 SampleStandardDefaultLitBaseColorLOD(float2 uv, float lod)
{
#if defined(_USE_BASECOLOR_TEX)
    float4 base_color_ST = UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _BaseColorTex_ST);
    float2 base_color_uv = TransformDefaultLitTextureUV(uv, base_color_ST);
    return SAMPLE_TEXTURE2D_LOD(_BaseColorTex, sampler_BaseColorTex, base_color_uv, lod);
#else
    return UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _BaseColor);
#endif
}

DefaultLitAlphaClipData BuildStandardDefaultLitAlphaClip(float alpha)
{
    DefaultLitAlphaClipData alpha_clip;
    alpha_clip.alpha   = alpha;
    alpha_clip.cutoff  = UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _AlphaCutoff);
    alpha_clip.enabled = UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _UseAlphaClip);
    return alpha_clip;
}

DefaultLitAlphaClipData EvaluateDefaultLitAlphaClip(DefaultLitSurfaceInput input)
{
    return BuildStandardDefaultLitAlphaClip(SampleStandardDefaultLitBaseColor(input.uv).a);
}

DefaultLitSurfaceResult EvaluateDefaultLitSurface(DefaultLitSurfaceInput input)
{
    DefaultLitSurfaceResult result;

    float4 base_color_sample  = SampleStandardDefaultLitBaseColor(input.uv);
    result.surface.base_color = base_color_sample.rgb;
    result.alpha_clip         = BuildStandardDefaultLitAlphaClip(base_color_sample.a);

#if defined(_USE_EMISSIVE_TEX)
    float4 emissive_ST      = UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _EmissiveTex_ST);
    float2 emissive_uv      = TransformDefaultLitTextureUV(input.uv, emissive_ST);
    result.surface.emissive = SAMPLE_TEXTURE2D(_EmissiveTex, sampler_EmissiveTex, emissive_uv).rgb;
#else
    result.surface.emissive = UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _Emissive).rgb;
#endif

#if defined(_USE_NORMAL_TEX)
    float4 normal_ST         = UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _NormalTex_ST);
    float2 normal_uv         = TransformDefaultLitTextureUV(input.uv, normal_ST);
    float4 packed_normal     = SAMPLE_TEXTURE2D(_NormalTex, sampler_NormalTex, normal_uv);
    result.surface.normal_WS = DefaultLitTangentNormalToWorld(packed_normal, input);
#else
    result.surface.normal_WS = input.normal_WS;
#endif

#if defined(_USE_ROUGHNESS_TEX)
    float4 roughness_ST      = UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _RoughnessTex_ST);
    float2 roughness_uv      = TransformDefaultLitTextureUV(input.uv, roughness_ST);
    result.surface.roughness = SAMPLE_TEXTURE2D(_RoughnessTex, sampler_RoughnessTex, roughness_uv).r;
#else
    result.surface.roughness = UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _Roughness);
#endif

#if defined(_USE_METALLIC_TEX)
    float4 metallic_ST      = UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _MetallicTex_ST);
    float2 metallic_uv      = TransformDefaultLitTextureUV(input.uv, metallic_ST);
    result.surface.metallic = SAMPLE_TEXTURE2D(_MetallicTex, sampler_MetallicTex, metallic_uv).r;
#else
    result.surface.metallic = UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _Metallic);
#endif

#if defined(_USE_MATERIAL_AO_TEX)
    float4 material_ao_ST      = UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _MaterialAOTex_ST);
    float2 material_ao_uv      = TransformDefaultLitTextureUV(input.uv, material_ao_ST);
    result.surface.material_AO = SAMPLE_TEXTURE2D(_MaterialAOTex, sampler_MaterialAOTex, material_ao_uv).r;
#else
    result.surface.material_AO = UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _MaterialAO);
#endif

    result.surface.material_AO      = saturate(result.surface.material_AO);
    result.surface.specular         = UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _Specular);
    result.surface.shading_model_id = 1;
    return result;
}

#endif
