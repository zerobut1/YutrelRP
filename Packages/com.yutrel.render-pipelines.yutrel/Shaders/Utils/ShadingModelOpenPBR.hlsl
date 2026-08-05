#ifndef YUTREL_SHADING_MODEL_OPENPBR_INCLUDED
#define YUTREL_SHADING_MODEL_OPENPBR_INCLUDED

// ---------------------------------------------------------------------------
// OpenPBR (DefaultLit / base layer) deferred shading model.
//
// Ported symbol-for-symbol from YutrelRender src/core/surfaces/openpbr.cpp
// (populate_closure + evaluate_impl), isotropic (alpha_x == alpha_y == alpha),
// evaluated in world space.
//
// OpenPBREvaluateBRDF returns the BRDF value f that ALREADY includes the
// cos(theta) factor (wi.z), exactly like the reference evaluate_impl. The
// caller must NOT multiply by NoL again.
//
// v1 limitations (documented):
//   - specular_roughness_anisotropy ignored (no tangent GBuffer).
//   - specular_ior < 1 (relative to the ambient medium) reconstructs the
//     non-inverted IOR from sqrt(weighted_f0); matches YutrelRender for the
//     entire normal specular_ior range [1, 3].
// ---------------------------------------------------------------------------

#include "OpenPBR.hlsl"
#include "GBuffer.hlsl"
#include "Light.hlsl"

struct OpenPBRSurface
{
    float3 weighted_base_color;
    float3 specular_color;
    float3 normal_WS;
    float3 position_WS;
    float3 view_direction_WS;
    float dielectricness; // 1 - base_metalness
    float darkened_metal; // base_metalness * specular_weight
    float weighted_ior;   // dielectric IOR after specular_weight
    float alpha;          // specular_roughness^2, clamped to [1e-6, 1]
    float diffuse_roughness;
    float NoV;
    // View-side LUT caches (light-independent, computed once per pixel).
    float dielectric_view_compensation;
    float metal_view_energy_complement;
    float metal_average;
    float3 metal_mms_scale;
    float3 diffuse_albedo;
};

OpenPBRSurface GBuffer2OpenPBRSurface(GBufferData data)
{
    OpenPBRSurface surface;

    surface.weighted_base_color = max(data.base_color, 0.0f);
    surface.specular_color      = max(data.specular_color, 0.0f);
    surface.normal_WS           = data.normal_WS;

    float metalness       = saturate(data.metallic);
    float specular_weight = max(data.specular, 0.0f);
    surface.dielectricness = 1.0f - metalness;
    surface.darkened_metal = metalness * specular_weight;

    float sqrt_f0 = saturate(data.sqrt_f0);
    surface.weighted_ior = (1.0f + sqrt_f0) / max(1.0f - sqrt_f0, OPENPBR_MIN_ENERGY_DENOMINATOR);
    surface.alpha = max(Square(saturate(data.roughness)), OPENPBR_MIN_ALPHA);
    surface.diffuse_roughness = saturate(data.diffuse_roughness);

    surface.position_WS       = ComputeWorldSpacePositionFromFullScreenUV(data.uv, data.scene_depth);
    surface.view_direction_WS = GetWorldSpaceViewDirectionForSurface(surface.position_WS);
    surface.NoV               = clamp(dot(surface.normal_WS, surface.view_direction_WS), OPENPBR_MIN_N_DOT_V, 1.0f);

    surface.diffuse_albedo = surface.weighted_base_color * surface.dielectricness;

    // F82 hemisphere-average of the metal Fresnel (closed form) -> metal MMS scale.
    float3 metal_average_fresnel = OpenPBR_MetalAverageFresnel(
        surface.weighted_base_color, surface.specular_color);
    surface.metal_mms_scale = metal_average_fresnel * metal_average_fresnel * surface.darkened_metal;

    // View-side energy compensation (independent of the light direction).
    surface.dielectric_view_compensation =
        OpenPBR_E_OpaqueDielectricEnergy(surface.weighted_ior, surface.alpha, surface.NoV) /
        max(OpenPBR_E_OpaqueDielectricAverage(surface.weighted_ior, surface.alpha),
            OPENPBR_MIN_ENERGY_DENOMINATOR);
    surface.metal_view_energy_complement = OpenPBR_E_IdealMetalEnergy(surface.alpha, surface.NoV);
    surface.metal_average = OpenPBR_E_IdealMetalAverage(surface.alpha);

    return surface;
}

// Per-light BRDF evaluation. Returns f * light.color * illuminance * occlusion,
// where f already contains the cos(theta) factor (matching YutrelRender).
float3 OpenPBREvaluateBRDF(OpenPBRSurface surface, Light light)
{
    float3 L   = light.direction;
    float  NoL = dot(surface.normal_WS, L);
    if (NoL <= 0.0f)
    {
        return 0.0f;
    }

    float3 V   = surface.view_direction_WS;
    float3 H   = normalize(V + L);
    float  NoH = saturate(dot(surface.normal_WS, H));
    float  LoH = saturate(dot(L, H));
    float  NoV = surface.NoV;

    // --- specular lobe: D * G1(wo) * G1(wi) / (4 NoV) * combined Fresnel ---
    float3 fresnel = surface.specular_color * surface.dielectricness *
                     OpenPBR_FresnelDielectric(LoH, 1.0f, surface.weighted_ior);
    fresnel += OpenPBR_MetalF82(surface.weighted_base_color, surface.specular_color, LoH) *
               surface.darkened_metal;

    float D      = OpenPBR_D_GGX(surface.alpha, NoH);
    float G1_v   = OpenPBR_G1_GGX(surface.alpha, NoV);
    float G1_l   = OpenPBR_G1_GGX(surface.alpha, NoL);
    float3 specular = fresnel * D * G1_v * G1_l / (4.0f * NoV);

    // --- metal multiple-scattering lobe (only when roughness >= 0.04) ---
    float metal_light = OpenPBR_E_IdealMetalEnergy(surface.alpha, NoL);
    float metal_factors = surface.metal_view_energy_complement * metal_light /
                          max(surface.metal_average, OPENPBR_MIN_ENERGY_DENOMINATOR);
    metal_factors = min(metal_factors, 1.0f / max(NoL, OPENPBR_MIN_ENERGY_DENOMINATOR));
    float3 metal_ms = surface.alpha >= OPENPBR_METAL_MMS_MIN_ALPHA
                          ? surface.metal_mms_scale * metal_factors * INV_PI * NoL
                          : 0.0f;

    // --- diffuse lobe: EON * dielectric energy compensation ---
    float dielectric_light = OpenPBR_E_OpaqueDielectricEnergy(surface.weighted_ior, surface.alpha, NoL);
    float diffuse_factor   = surface.dielectric_view_compensation * dielectric_light;
    float VoL              = dot(V, L); // raw dot, matching YutrelRender (not saturated)
    float3 diffuse = OpenPBR_EON(surface.diffuse_albedo, surface.diffuse_roughness, NoV, NoL, VoL) *
                     diffuse_factor * NoL;

    float3 f = specular + metal_ms + diffuse;
    return f * light.color * light.illuminance * light.occlusion;
}

#endif // YUTREL_SHADING_MODEL_OPENPBR_INCLUDED
