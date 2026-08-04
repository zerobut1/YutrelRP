#ifndef YUTREL_OPENPBR_DEFAULTLIT_SURFACE_INCLUDED
#define YUTREL_OPENPBR_DEFAULTLIT_SURFACE_INCLUDED

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
UNITY_INSTANCING_BUFFER_END(UnityPerMaterial)

DefaultLitAlphaClipData EvaluateDefaultLitAlphaClip(DefaultLitSurfaceInput input)
{
    return DefaultLitAlphaClipOff();
}

DefaultLitSurfaceResult EvaluateDefaultLitSurface(DefaultLitSurfaceInput input)
{
    DefaultLitSurfaceResult result;
    float base_weight = saturate(
        UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _OpenPBRBaseWeight));
    float3 base_color = max(
        UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _OpenPBRBaseColor).rgb,
        0.0f);
    float metalness = saturate(
        UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _OpenPBRBaseMetalness));
    float roughness = saturate(
        UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _OpenPBRSpecularRoughness));
    float specular_weight = max(
        UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _OpenPBRSpecularWeight),
        0.0f);
    float ior = max(
        UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _OpenPBRSpecularIOR),
        1.0e-6f);
    float sqrt_f0 = (ior - 1.0f) / (ior + 1.0f);
    float weighted_f0 = specular_weight * Square(sqrt_f0);

    result.surface.base_color       = base_weight * base_color;
    result.surface.emissive         = 0.0f;
    result.surface.normal_WS        = input.normal_WS;
    result.surface.roughness        = roughness;
    result.surface.metallic         = metalness;
    result.surface.specular         = saturate(weighted_f0 / 0.08f);
    result.surface.material_AO      = 1.0f;
    result.surface.shading_model_id = SHADING_MODEL_STANDARD;
    result.alpha_clip               = DefaultLitAlphaClipOff();
    return result;
}

#endif
