#ifndef YUTREL_SPONZA_DEFAULTLIT_SURFACE_INCLUDED
#define YUTREL_SPONZA_DEFAULTLIT_SURFACE_INCLUDED

TEXTURE2D(_BaseColorTex);
SAMPLER(sampler_BaseColorTex);
TEXTURE2D(_NormalTex);
SAMPLER(sampler_NormalTex);
TEXTURE2D(_SmoothnessTex);
SAMPLER(sampler_SmoothnessTex);
TEXTURE2D(_MetallicTex);
SAMPLER(sampler_MetallicTex);
TEXTURE2D(_MaterialAOTex);
SAMPLER(sampler_MaterialAOTex);

UNITY_INSTANCING_BUFFER_START(UnityPerMaterial)
UNITY_DEFINE_INSTANCED_PROP(float4, _BaseColor)
UNITY_DEFINE_INSTANCED_PROP(float4, _BaseColorTex_ST)
UNITY_DEFINE_INSTANCED_PROP(float, _UseAlphaClip)
UNITY_DEFINE_INSTANCED_PROP(float, _AlphaCutoff)
UNITY_DEFINE_INSTANCED_PROP(float4, _NormalTex_ST)
UNITY_DEFINE_INSTANCED_PROP(float4, _SmoothnessTex_ST)
UNITY_DEFINE_INSTANCED_PROP(float4, _MetallicTex_ST)
UNITY_DEFINE_INSTANCED_PROP(float4, _MaterialAOTex_ST)
UNITY_INSTANCING_BUFFER_END(UnityPerMaterial)

float4 SampleSponzaDefaultLitBaseColor(float2 uv)
{
    float4 base_color_ST = UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _BaseColorTex_ST);
    float2 base_color_uv = TransformDefaultLitTextureUV(uv, base_color_ST);
    return SAMPLE_TEXTURE2D(_BaseColorTex, sampler_BaseColorTex, base_color_uv) *
           UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _BaseColor);
}

float4 SampleSponzaDefaultLitBaseColorLOD(float2 uv, float lod)
{
    float4 base_color_ST = UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _BaseColorTex_ST);
    float2 base_color_uv = TransformDefaultLitTextureUV(uv, base_color_ST);
    return SAMPLE_TEXTURE2D_LOD(_BaseColorTex, sampler_BaseColorTex, base_color_uv, lod) *
           UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _BaseColor);
}

DefaultLitAlphaClipData BuildSponzaDefaultLitAlphaClip(float alpha)
{
    DefaultLitAlphaClipData alpha_clip;
    alpha_clip.alpha   = alpha;
    alpha_clip.cutoff  = UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _AlphaCutoff);
    alpha_clip.enabled = UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _UseAlphaClip);
    return alpha_clip;
}

DefaultLitAlphaClipData EvaluateDefaultLitAlphaClip(DefaultLitSurfaceInput input)
{
    float alpha = SampleSponzaDefaultLitBaseColor(input.uv).a;
    return BuildSponzaDefaultLitAlphaClip(alpha);
}

DefaultLitSurfaceResult EvaluateDefaultLitSurface(DefaultLitSurfaceInput input)
{
    DefaultLitSurfaceResult result;

    float4 base_color_sample  = SampleSponzaDefaultLitBaseColor(input.uv);
    result.surface.base_color = base_color_sample.rgb;
    result.surface.emissive   = 0.0f;
    result.alpha_clip         = BuildSponzaDefaultLitAlphaClip(base_color_sample.a);

    float4 normal_ST         = UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _NormalTex_ST);
    float2 normal_uv         = TransformDefaultLitTextureUV(input.uv, normal_ST);
    float4 packed_normal     = SAMPLE_TEXTURE2D(_NormalTex, sampler_NormalTex, normal_uv);
    result.surface.normal_WS = DefaultLitTangentNormalToWorld(packed_normal, input);

    float4 smoothness_ST     = UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _SmoothnessTex_ST);
    float2 smoothness_uv     = TransformDefaultLitTextureUV(input.uv, smoothness_ST);
    float smoothness         = SAMPLE_TEXTURE2D(_SmoothnessTex, sampler_SmoothnessTex, smoothness_uv).r;
    result.surface.roughness = saturate(1.0f - smoothness);

    float4 metallic_ST      = UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _MetallicTex_ST);
    float2 metallic_uv      = TransformDefaultLitTextureUV(input.uv, metallic_ST);
    result.surface.metallic = SAMPLE_TEXTURE2D(_MetallicTex, sampler_MetallicTex, metallic_uv).r;

    float4 material_ao_ST     = UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _MaterialAOTex_ST);
    float2 material_ao_uv     = TransformDefaultLitTextureUV(input.uv, material_ao_ST);
    float material_ao_texture = SAMPLE_TEXTURE2D(_MaterialAOTex, sampler_MaterialAOTex, material_ao_uv).r;

    result.surface.specular         = 0.5f;
    result.surface.material_AO      = saturate(material_ao_texture);
    result.surface.shading_model_id = 1;
    return result;
}

#endif
