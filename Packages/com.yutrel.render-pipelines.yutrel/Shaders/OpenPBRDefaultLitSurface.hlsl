#ifndef YUTREL_OPENPBR_DEFAULTLIT_SURFACE_INCLUDED
#define YUTREL_OPENPBR_DEFAULTLIT_SURFACE_INCLUDED

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

DefaultLitAlphaClipData EvaluateDefaultLitAlphaClip(DefaultLitSurfaceInput input)
{
    return BuildOpenPBRAlphaClip(SampleOpenPBRBaseColor(input.uv).a);
}

DefaultLitSurfaceResult EvaluateDefaultLitSurface(DefaultLitSurfaceInput input)
{
    DefaultLitSurfaceResult result;

    // --- parse OpenPBR base-layer parameters (textures where available) ---
    float base_weight = saturate(UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _OpenPBRBaseWeight));
    float4 base_color_sample = SampleOpenPBRBaseColor(input.uv);
    float3 base_color = max(base_color_sample.rgb, 0.0f);
    float metalness = saturate(SampleOpenPBRMetalness(input.uv));
    float roughness = saturate(SampleOpenPBRRoughness(input.uv));
    float specular_weight = max(UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _OpenPBRSpecularWeight), 0.0f);
    float3 specular_color = max(UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _OpenPBRSpecularColor).rgb, 0.0f);
    float ior = max(UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _OpenPBRSpecularIOR), 1.0e-6f);
    float diffuse_roughness = saturate(
        UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _OpenPBRBaseDiffuseRoughness));
    // specular_roughness_anisotropy is intentionally ignored in v1 (isotropic).
    // UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _OpenPBRSpecularRoughnessAnisotropy)

    // --- BasePass folds (see file header) ---
    result.surface.base_color = base_weight * base_color; // weighted_base_color
    float sqrt_f0_raw = (ior - 1.0f) / (ior + 1.0f);
    float weighted_f0 = min(specular_weight * Square(sqrt_f0_raw), 0.9999f);
    result.surface.sqrt_f0 = sqrt(weighted_f0);

    result.surface.emissive          = 0.0f;
    result.surface.normal_WS         = SampleOpenPBRNormal(input);
    result.surface.roughness         = roughness;
    result.surface.metallic          = metalness;
    result.surface.specular          = specular_weight;
    result.surface.material_AO       = SampleOpenPBRMaterialAO(input.uv);
    result.surface.specular_color    = specular_color;
    result.surface.diffuse_roughness = diffuse_roughness;
    result.surface.shading_model_id  = SHADING_MODEL_OPENPBR;

    result.alpha_clip = BuildOpenPBRAlphaClip(base_color_sample.a);
    return result;
}

#endif
