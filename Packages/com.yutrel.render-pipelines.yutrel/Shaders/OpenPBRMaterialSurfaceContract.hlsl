#ifndef YUTREL_OPENPBR_MATERIAL_SURFACE_CONTRACT_INCLUDED
#define YUTREL_OPENPBR_MATERIAL_SURFACE_CONTRACT_INCLUDED

#include "DefaultLitSurfaceContract.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
#include "Utils/Common.hlsl"

#define YUTREL_OPENPBR_MATERIAL_ABI_VERSION 2

struct OpenPBRMaterialValues
{
    float base_weight;
    float3 base_color;
    float base_metalness;
    float base_diffuse_roughness;
    float specular_weight;
    float3 specular_color;
    float specular_roughness;
    float specular_roughness_anisotropy;
    float specular_ior;
    float geometry_opacity;
    float3 shading_normal;
};

DefaultLitSurfaceData OpenPBRMaterialValuesToOpenPBRSurface(
    OpenPBRMaterialValues values,
    float material_AO)
{
    float base_weight     = saturate(values.base_weight);
    float3 base_color     = saturate(values.base_color);
    float specular_weight = saturate(values.specular_weight);
    float specular_ior    = max(values.specular_ior, 1.0e-6f);
    float sqrt_f0_raw     = (specular_ior - 1.0f) / (specular_ior + 1.0f);
    float weighted_f0     = min(specular_weight * Square(sqrt_f0_raw), 0.9999f);

    DefaultLitSurfaceData surface;
    surface.base_color        = base_weight * base_color;
    surface.emissive          = 0.0f;
    surface.normal_WS         = normalize(values.shading_normal);
    surface.roughness         = saturate(values.specular_roughness);
    surface.metallic          = saturate(values.base_metalness);
    surface.specular          = specular_weight;
    surface.material_AO       = saturate(material_AO);
    surface.shading_model_id  = SHADING_MODEL_OPENPBR;
    surface.specular_color    = saturate(values.specular_color);
    surface.sqrt_f0           = sqrt(weighted_f0);
    surface.diffuse_roughness = saturate(values.base_diffuse_roughness);
    return surface;
}

DefaultLitSurfaceData OpenPBRMaterialValuesToDefaultLitSurface(
    OpenPBRMaterialValues values,
    float material_AO)
{
    float metallic        = saturate(values.base_metalness);
    float base_weight     = saturate(values.base_weight);
    float specular_weight = max(values.specular_weight, 0.0f);
    float3 base_color     = saturate(values.base_color);
    float3 specular_tint  = saturate(values.specular_color);

    float3 diffuse_base  = base_weight * base_color;
    float3 metallic_base = diffuse_base * specular_weight * specular_tint;

    float specular_ior = max(values.specular_ior, 1.0e-4f);
    float sqrt_f0      = (specular_ior - 1.0f) / (specular_ior + 1.0f);
    float f0           = Square(sqrt_f0) * specular_weight * Luminance(specular_tint);

    DefaultLitSurfaceData surface;
    surface.base_color        = saturate(lerp(diffuse_base, metallic_base, metallic));
    surface.emissive          = 0.0f;
    surface.normal_WS         = normalize(values.shading_normal);
    surface.roughness         = saturate(values.specular_roughness);
    surface.metallic          = metallic;
    surface.specular          = saturate(f0 / 0.08f);
    surface.material_AO       = saturate(material_AO);
    surface.shading_model_id  = SHADING_MODEL_STANDARD;
    surface.specular_color    = 1.0f;
    surface.sqrt_f0           = sqrt(0.04f * surface.specular);
    surface.diffuse_roughness = 0.0f;
    return surface;
}

#endif
