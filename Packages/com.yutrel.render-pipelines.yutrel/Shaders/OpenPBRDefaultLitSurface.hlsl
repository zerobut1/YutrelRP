#ifndef YUTREL_OPENPBR_DEFAULTLIT_SURFACE_INCLUDED
#define YUTREL_OPENPBR_DEFAULTLIT_SURFACE_INCLUDED

#include "OpenPBRMaterialSurfaceContract.hlsl"

// ---------------------------------------------------------------------------
// OpenPBR (DefaultLit / base layer) surface evaluation.
//
// BasePass folds the view-independent quantities into the GBuffer:
//   - A.rgb = weighted_base_color      = base_color * base_weight
//   - C.r   = specular_roughness (perceptual)
//   - C.g   = base_metalness
//   - C.b   = specular_weight
//   - C.a   = material AO (= 1 for now)
//   - D.rgb = specular_color
//   - D.a   = sqrt(weighted_f0), weighted_f0 = min(specular_weight * f0(specular_ior), 0.9999)
//   - B.a   = base_diffuse_roughness
// Matching the GBuffer layout in docs/OpenPBR/OpenPBR_DefaultLit_Implementation.md §5.
//
// v1: specular_roughness_anisotropy is parsed but ignored (isotropic only;
// anisotropic specular needs a tangent GBuffer).
// ---------------------------------------------------------------------------

TEXTURE2D(_OpenPBRBaseColorTex);
SAMPLER(sampler_OpenPBRBaseColorTex);
TEXTURE2D(_OpenPBRNormalTex);
SAMPLER(sampler_OpenPBRNormalTex);
TEXTURE2D(_OpenPBRRoughnessTex);
SAMPLER(sampler_OpenPBRRoughnessTex);
TEXTURE2D(_OpenPBRMetalnessTex);
SAMPLER(sampler_OpenPBRMetalnessTex);
TEXTURE2D(_OpenPBRMaterialAOTex);
SAMPLER(sampler_OpenPBRMaterialAOTex);

UNITY_INSTANCING_BUFFER_START(UnityPerMaterial)
UNITY_DEFINE_INSTANCED_PROP(float, _OpenPBRBaseWeight)
UNITY_DEFINE_INSTANCED_PROP(float4, _OpenPBRBaseColor)
UNITY_DEFINE_INSTANCED_PROP(float, _OpenPBRBaseMetalness)
UNITY_DEFINE_INSTANCED_PROP(float, _OpenPBRBaseDiffuseRoughness)
UNITY_DEFINE_INSTANCED_PROP(float, _OpenPBRSpecularWeight)
UNITY_DEFINE_INSTANCED_PROP(float4, _OpenPBRSpecularColor)
UNITY_DEFINE_INSTANCED_PROP(float, _OpenPBRSpecularRoughness)
UNITY_DEFINE_INSTANCED_PROP(float, _OpenPBRSpecularRoughnessAnisotropy)
UNITY_DEFINE_INSTANCED_PROP(float, _OpenPBRSpecularIOR)
UNITY_DEFINE_INSTANCED_PROP(float, _OpenPBRUseAlphaClip)
UNITY_DEFINE_INSTANCED_PROP(float, _OpenPBRAlphaCutoff)
UNITY_DEFINE_INSTANCED_PROP(float4, _OpenPBRBaseColorTex_ST)
UNITY_DEFINE_INSTANCED_PROP(float4, _OpenPBRNormalTex_ST)
UNITY_DEFINE_INSTANCED_PROP(float4, _OpenPBRRoughnessTex_ST)
UNITY_DEFINE_INSTANCED_PROP(float4, _OpenPBRMetalnessTex_ST)
UNITY_DEFINE_INSTANCED_PROP(float4, _OpenPBRMaterialAOTex_ST)
UNITY_INSTANCING_BUFFER_END(UnityPerMaterial)

float4 SampleOpenPBRBaseColor(float2 uv)
{
#if defined(_USE_BASECOLOR_TEX)
    float4 base_color_ST = UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _OpenPBRBaseColorTex_ST);
    float2 base_color_uv = TransformDefaultLitTextureUV(uv, base_color_ST);
    return SAMPLE_TEXTURE2D(_OpenPBRBaseColorTex, sampler_OpenPBRBaseColorTex, base_color_uv);
#else
    return UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _OpenPBRBaseColor);
#endif
}

float4 SampleOpenPBRBaseColorLOD(float2 uv, float lod)
{
#if defined(_USE_BASECOLOR_TEX)
    float4 base_color_ST = UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _OpenPBRBaseColorTex_ST);
    float2 base_color_uv = TransformDefaultLitTextureUV(uv, base_color_ST);
    return SAMPLE_TEXTURE2D_LOD(_OpenPBRBaseColorTex, sampler_OpenPBRBaseColorTex, base_color_uv, lod);
#else
    return UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _OpenPBRBaseColor);
#endif
}

float GetOpenPBRBaseColorGIMipLevel()
{
#if defined(_USE_BASECOLOR_TEX)
    uint width;
    uint height;
    uint mipCount;
    _OpenPBRBaseColorTex.GetDimensions(0, width, height, mipCount);
    return DefaultLitRayTracingGIMipLevel(mipCount);
#else
    return 0.0f;
#endif
}

float GetOpenPBRAlphaClipGIMipLevel()
{
#if defined(_USE_BASECOLOR_TEX)
    uint width;
    uint height;
    uint mipCount;
    _OpenPBRBaseColorTex.GetDimensions(0, width, height, mipCount);
    return DefaultLitRayTracingGIAlphaClipMipLevel(mipCount);
#else
    return 0.0f;
#endif
}

float3 SampleOpenPBRNormal(DefaultLitSurfaceInput input)
{
#if defined(_USE_NORMAL_TEX)
    float4 normal_ST     = UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _OpenPBRNormalTex_ST);
    float2 normal_uv     = TransformDefaultLitTextureUV(input.uv, normal_ST);
    float4 packed_normal = SAMPLE_TEXTURE2D(_OpenPBRNormalTex, sampler_OpenPBRNormalTex, normal_uv);
    return DefaultLitTangentNormalToWorld(packed_normal, input);
#else
    return input.normal_WS;
#endif
}

float GetOpenPBRNormalGIMipLevel()
{
#if defined(_USE_NORMAL_TEX)
    uint width;
    uint height;
    uint mipCount;
    _OpenPBRNormalTex.GetDimensions(0, width, height, mipCount);
    return DefaultLitRayTracingGIMipLevel(mipCount);
#else
    return 0.0f;
#endif
}

float3 SampleOpenPBRNormalLOD(DefaultLitSurfaceInput input, float lod)
{
#if defined(_USE_NORMAL_TEX)
    float4 normal_ST     = UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _OpenPBRNormalTex_ST);
    float2 normal_uv     = TransformDefaultLitTextureUV(input.uv, normal_ST);
    float4 packed_normal = SAMPLE_TEXTURE2D_LOD(_OpenPBRNormalTex, sampler_OpenPBRNormalTex, normal_uv, lod);
    return DefaultLitTangentNormalToWorld(packed_normal, input);
#else
    return input.normal_WS;
#endif
}

float SampleOpenPBRRoughness(float2 uv)
{
#if defined(_USE_ROUGHNESS_TEX)
    float4 roughness_ST = UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _OpenPBRRoughnessTex_ST);
    float2 roughness_uv = TransformDefaultLitTextureUV(uv, roughness_ST);
    return SAMPLE_TEXTURE2D(_OpenPBRRoughnessTex, sampler_OpenPBRRoughnessTex, roughness_uv).r;
#else
    return UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _OpenPBRSpecularRoughness);
#endif
}

float SampleOpenPBRMetalness(float2 uv)
{
#if defined(_USE_METALLIC_TEX)
    float4 metallic_ST = UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _OpenPBRMetalnessTex_ST);
    float2 metallic_uv = TransformDefaultLitTextureUV(uv, metallic_ST);
    return SAMPLE_TEXTURE2D(_OpenPBRMetalnessTex, sampler_OpenPBRMetalnessTex, metallic_uv).r;
#else
    return UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _OpenPBRBaseMetalness);
#endif
}

float SampleOpenPBRMaterialAO(float2 uv)
{
#if defined(_USE_MATERIAL_AO_TEX)
    float4 material_ao_ST = UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _OpenPBRMaterialAOTex_ST);
    float2 material_ao_uv = TransformDefaultLitTextureUV(uv, material_ao_ST);
    return SAMPLE_TEXTURE2D(_OpenPBRMaterialAOTex, sampler_OpenPBRMaterialAOTex, material_ao_uv).r;
#else
    return 1.0f;
#endif
}

DefaultLitAlphaClipData BuildOpenPBRAlphaClip(float alpha)
{
    DefaultLitAlphaClipData alpha_clip;
    alpha_clip.alpha   = alpha;
    alpha_clip.cutoff  = UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _OpenPBRAlphaCutoff);
    alpha_clip.enabled = UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _OpenPBRUseAlphaClip);
    return alpha_clip;
}

float4 SampleDefaultLitRayTracingBaseColorLOD(float2 uv, float lod)
{
    float4 base_color = SampleOpenPBRBaseColorLOD(uv, lod);
    base_color.rgb *= saturate(UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _OpenPBRBaseWeight));
    return base_color;
}

float GetDefaultLitRayTracingBaseColorGIMipLevel()
{
    return GetOpenPBRBaseColorGIMipLevel();
}

float GetDefaultLitRayTracingAlphaClipGIMipLevel()
{
    return GetOpenPBRAlphaClipGIMipLevel();
}

float3 SampleDefaultLitRayTracingNormalLOD(DefaultLitSurfaceInput input, float lod)
{
    return SampleOpenPBRNormalLOD(input, lod);
}

float GetDefaultLitRayTracingNormalGIMipLevel()
{
    return GetOpenPBRNormalGIMipLevel();
}

DefaultLitAlphaClipData BuildDefaultLitRayTracingAlphaClip(float alpha)
{
    return BuildOpenPBRAlphaClip(alpha);
}

DefaultLitAlphaClipData EvaluateDefaultLitAlphaClip(DefaultLitSurfaceInput input)
{
    return BuildOpenPBRAlphaClip(SampleOpenPBRBaseColor(input.uv).a);
}

DefaultLitSurfaceResult EvaluateDefaultLitSurface(DefaultLitSurfaceInput input)
{
    OpenPBRMaterialValues values;
    float4 base_color_sample      = SampleOpenPBRBaseColor(input.uv);
    values.base_weight            = UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _OpenPBRBaseWeight);
    values.base_color             = base_color_sample.rgb;
    values.base_metalness         = SampleOpenPBRMetalness(input.uv);
    values.base_diffuse_roughness = UNITY_ACCESS_INSTANCED_PROP(
        UnityPerMaterial,
        _OpenPBRBaseDiffuseRoughness);
    values.specular_weight = UNITY_ACCESS_INSTANCED_PROP(
        UnityPerMaterial,
        _OpenPBRSpecularWeight);
    values.specular_color                = UNITY_ACCESS_INSTANCED_PROP(
                                               UnityPerMaterial,
                                               _OpenPBRSpecularColor)
                                               .rgb;
    values.specular_roughness            = SampleOpenPBRRoughness(input.uv);
    values.specular_roughness_anisotropy = UNITY_ACCESS_INSTANCED_PROP(
        UnityPerMaterial,
        _OpenPBRSpecularRoughnessAnisotropy);
    values.specular_ior     = UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _OpenPBRSpecularIOR);
    values.geometry_opacity = base_color_sample.a;
    values.shading_normal   = SampleOpenPBRNormal(input);

    DefaultLitSurfaceResult result;
    result.surface = OpenPBRMaterialValuesToOpenPBRSurface(
        values,
        SampleOpenPBRMaterialAO(input.uv));
    result.alpha_clip = BuildOpenPBRAlphaClip(base_color_sample.a);
    return result;
}

#endif
