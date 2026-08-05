#ifndef YUTREL_OPENPBR_INCLUDED
#define YUTREL_OPENPBR_INCLUDED

// ---------------------------------------------------------------------------
// OpenPBR (DefaultLit / base layer) BRDF math, ported symbol-for-symbol from
// YutrelRender src/core/surfaces/openpbr.cpp (validated against Adobe's
// openpbr-bsdf golden data + white-furnace test). Do NOT "improve" these
// formulas without re-validating against YutrelRender.
//
// v1 scope: isotropic specular (alpha_x == alpha_y == alpha), world-space
// evaluation (all quantities are basis-free in the isotropic case). The
// anisotropic GGX form is documented but not needed until a tangent GBuffer
// exists.
//
// LUT textures are created/bound by Runtime/OpenPBR/OpenPBRLUTs.cs and
// contain the Adobe tables copied from YutrelRender (Apache-2.0).
// ---------------------------------------------------------------------------

#include "Common.hlsl"

#define OPENPBR_MIN_N_DOT_V 1e-4f
#define OPENPBR_MIN_ALPHA 1e-6f
#define OPENPBR_MIN_ENERGY_DENOMINATOR 1e-12f
#define OPENPBR_MIN_EON_EPS 1e-7f
#define OPENPBR_METAL_MMS_MIN_ALPHA 0.0016f // 0.04^2: skip metal MMS below this roughness
#define OPENPBR_IOR_MAX 2.5f
#define OPENPBR_INVERSE_IOR_MAX (1.0f / OPENPBR_IOR_MAX)

// ---------------------------------------------------------------------------
// LUT declarations (bound globally by OpenPBRLUTs.EnsureCreated)
// ---------------------------------------------------------------------------

TEXTURE3D(_OpenPBR_OpaqueDielectricEnergy);
SAMPLER(sampler_OpenPBR_OpaqueDielectricEnergy);
TEXTURE2D(_OpenPBR_OpaqueDielectricAverage);
SAMPLER(sampler_OpenPBR_OpaqueDielectricAverage);
TEXTURE2D(_OpenPBR_IdealMetalEnergy);
SAMPLER(sampler_OpenPBR_IdealMetalEnergy);
TEXTURE2D(_OpenPBR_IdealMetalAverage);
SAMPLER(sampler_OpenPBR_IdealMetalAverage);

// ---------------------------------------------------------------------------
// LUT index helpers (texel-center remap; matches YutrelRender remap_lut_index)
// ---------------------------------------------------------------------------

float OpenPBR_RemapExactIndex(float exact_index)
{
    const float inv_size = 1.0f / 32.0f;
    return clamp((exact_index + 0.5f) * inv_size, 0.5f * inv_size, 1.0f - 0.5f * inv_size);
}

float OpenPBR_IorToExactIndex(float ior)
{
    const float half_size = 16.0f;
    const float half_size_minus_one = 15.0f;
    float below_one = half_size_minus_one - (1.0f / ior - 1.0f) * (half_size_minus_one / 1.5f);
    float above_one = half_size + (ior - 1.0f) * (half_size_minus_one / 1.5f);
    return ior < 1.0f ? below_one : above_one;
}

float OpenPBR_AlphaToExactIndex(float alpha)
{
    return sqrt(alpha) * 31.0f;
}

float OpenPBR_CosThetaToExactIndex(float cos_theta)
{
    return cos_theta * 31.0f;
}

// Extrapolates the opaque-dielectric table value beyond its tabulated IOR
// range [0.4, 2.5] by scaling toward zero as F0 approaches 1.
float OpenPBR_ExtrapolateOpaqueIor(float table_value, float ior)
{
    const float f0_max = ((OPENPBR_IOR_MAX - 1.0f) / (OPENPBR_IOR_MAX + 1.0f)) *
                         ((OPENPBR_IOR_MAX - 1.0f) / (OPENPBR_IOR_MAX + 1.0f));
    float f0 = Square((ior - 1.0f) / (ior + 1.0f));
    float progress = (f0 - f0_max) / (1.0f - f0_max);
    return (ior > OPENPBR_IOR_MAX || ior < OPENPBR_INVERSE_IOR_MAX)
               ? (1.0f - progress) * table_value
               : table_value;
}

// ---------------------------------------------------------------------------
// LUT lookups.
// Sampling axis convention (must match OpenPBRLUTs.cs):
//   3D: uvw = (cos_theta, alpha, ior)  -- x=cos, y=alpha, z=ior
//   2D opaque avg: uv = (alpha, ior)   -- x=alpha, y=ior
//   2D metal energy: uv = (alpha, cos_theta)
//   1D metal avg: 32x1, uv = (alpha, 0.5)
// ---------------------------------------------------------------------------

float OpenPBR_E_OpaqueDielectricEnergy(float ior, float alpha, float cos_theta)
{
    float3 uvw = float3(
        OpenPBR_RemapExactIndex(OpenPBR_CosThetaToExactIndex(cos_theta)),
        OpenPBR_RemapExactIndex(OpenPBR_AlphaToExactIndex(alpha)),
        OpenPBR_RemapExactIndex(OpenPBR_IorToExactIndex(ior)));
    float value = SAMPLE_TEXTURE3D(_OpenPBR_OpaqueDielectricEnergy, sampler_OpenPBR_OpaqueDielectricEnergy, uvw).r;
    return OpenPBR_ExtrapolateOpaqueIor(value, ior);
}

float OpenPBR_E_OpaqueDielectricAverage(float ior, float alpha)
{
    float2 uv = float2(
        OpenPBR_RemapExactIndex(OpenPBR_AlphaToExactIndex(alpha)),
        OpenPBR_RemapExactIndex(OpenPBR_IorToExactIndex(ior)));
    float value = SAMPLE_TEXTURE2D(_OpenPBR_OpaqueDielectricAverage, sampler_OpenPBR_OpaqueDielectricAverage, uv).r;
    return OpenPBR_ExtrapolateOpaqueIor(value, ior);
}

float OpenPBR_E_IdealMetalEnergy(float alpha, float cos_theta)
{
    float2 uv = float2(
        OpenPBR_RemapExactIndex(OpenPBR_AlphaToExactIndex(alpha)),
        OpenPBR_RemapExactIndex(OpenPBR_CosThetaToExactIndex(cos_theta)));
    return SAMPLE_TEXTURE2D(_OpenPBR_IdealMetalEnergy, sampler_OpenPBR_IdealMetalEnergy, uv).r;
}

float OpenPBR_E_IdealMetalAverage(float alpha)
{
    float2 uv = float2(OpenPBR_RemapExactIndex(OpenPBR_AlphaToExactIndex(alpha)), 0.5f);
    return SAMPLE_TEXTURE2D(_OpenPBR_IdealMetalAverage, sampler_OpenPBR_IdealMetalAverage, uv).r;
}

// ---------------------------------------------------------------------------
// Fresnel
// ---------------------------------------------------------------------------

// Real unpolarized dielectric Fresnel (r_parallel/r_perpendicular) with
// entering/exiting IOR swap and total-internal-reflection clamp.
float OpenPBR_FresnelDielectric(float cos_theta_i_in, float eta_i_in, float eta_t_in)
{
    float cos_theta_i = clamp(cos_theta_i_in, -1.0f, 1.0f);
    bool entering = cos_theta_i > 0.0f;
    float eta_i = entering ? eta_i_in : eta_t_in;
    float eta_t = entering ? eta_t_in : eta_i_in;
    cos_theta_i = abs(cos_theta_i);

    float sin_theta_i = sqrt(max(0.0f, 1.0f - cos_theta_i * cos_theta_i));
    float sin_theta_t = (eta_i / eta_t) * sin_theta_i;
    float cos_theta_t = sqrt(max(0.0f, 1.0f - sin_theta_t * sin_theta_t));

    float r_parallel = (eta_t * cos_theta_i - eta_i * cos_theta_t) /
                       (eta_t * cos_theta_i + eta_i * cos_theta_t);
    float r_perp = (eta_i * cos_theta_i - eta_t * cos_theta_t) /
                   (eta_i * cos_theta_i + eta_t * cos_theta_t);
    float f = 0.5f * (r_parallel * r_parallel + r_perp * r_perp);
    return sin_theta_t < 1.0f ? f : 1.0f;
}

// F82-tint Schlick helper coefficient.
float3 OpenPBR_MetalSchlickB(float3 f0, float3 f82_tint)
{
    const float cos_theta_max = 1.0f / 7.0f;
    const float one_minus_cos = 1.0f - cos_theta_max;
    const float one_minus_cos_5 = pow(one_minus_cos, 5.0f);
    const float one_minus_cos_6 = one_minus_cos_5 * one_minus_cos;
    return (f0 + (1.0f - f0) * one_minus_cos_5) * (1.0f - f82_tint) /
           (cos_theta_max * one_minus_cos_6);
}

// F82-tint Schlick metallic Fresnel.
float3 OpenPBR_MetalF82(float3 f0, float3 f82_tint, float cos_theta)
{
    float3 b = OpenPBR_MetalSchlickB(f0, f82_tint);
    float one_minus_cos = 1.0f - cos_theta;
    return saturate(f0 + ((1.0f - f0) - b * cos_theta * one_minus_cos) * pow(one_minus_cos, 5.0f));
}

// Cosine-weighted hemisphere average of the F82 metallic Fresnel (closed form).
float3 OpenPBR_MetalAverageFresnel(float3 f0, float3 f82_tint)
{
    float3 b = OpenPBR_MetalSchlickB(f0, f82_tint);
    return saturate(f0 + (1.0f - f0) * (1.0f / 21.0f) - b * (1.0f / 126.0f));
}

// ---------------------------------------------------------------------------
// GGX microfacet distribution & Smith masking (isotropic, world-space form).
// Equivalent to YutrelRender's anisotropic formulas with alpha_x == alpha_y.
// ---------------------------------------------------------------------------

float OpenPBR_D_GGX(float alpha, float NoH)
{
    // Matches YutrelRender openpbr_ggx_d: when the half-vector is (nearly)
    // perpendicular to the normal (cos4 < 1e-16) or tan2 is infinite, the
    // distribution evaluates to 0 instead of 1/0 -> Inf. Without this guard the
    // specular term can blow up to float16-Inf (65504) at grazing edges.
    float tan2_h = (1.0f - NoH * NoH) / max(NoH * NoH, OPENPBR_MIN_N_DOT_V * OPENPBR_MIN_N_DOT_V);
    float cos4_h = NoH * NoH * NoH * NoH;
    float e = tan2_h / max(alpha * alpha, OPENPBR_MIN_ALPHA * OPENPBR_MIN_ALPHA);
    float d = 1.0f / (PI * alpha * alpha * cos4_h * Square(1.0f + e));
    return (isinf(tan2_h) || cos4_h < 1.0e-16f) ? 0.0f : d;
}

float OpenPBR_G1_GGX(float alpha, float cos_theta_w)
{
    cos_theta_w = max(cos_theta_w, OPENPBR_MIN_N_DOT_V);
    float tan2_w = (1.0f - cos_theta_w * cos_theta_w) / (cos_theta_w * cos_theta_w);
    float slope2 = alpha * alpha * tan2_w;
    return 2.0f / (1.0f + sqrt(1.0f + slope2));
}

// ---------------------------------------------------------------------------
// Energy-conserving Fujii Oren-Nayar (EON) diffuse, incl. multi-scatter term.
// Basis-free: takes NoV, NoL and dot(V, L) directly.
// ---------------------------------------------------------------------------

float OpenPBR_FONConstantA()
{
    return 0.5f - 2.0f / (3.0f * PI);
}

float OpenPBR_FONConstantB()
{
    return 2.0f / 3.0f - 28.0f / (15.0f * PI);
}

float OpenPBR_E_FON_Approx(float mu, float roughness)
{
    const float g1 = 0.0571085289f;
    const float g2 = 0.491881867f;
    const float g3 = -0.332181442f;
    const float g4 = 0.0714429953f;
    float mucomp = 1.0f - mu;
    float GoverPi = mucomp * (g1 + mucomp * (g2 + mucomp * (g3 + mucomp * g4)));
    return (1.0f + roughness * GoverPi) / (1.0f + OpenPBR_FONConstantA() * roughness);
}

float3 OpenPBR_EON(float3 diffuse_albedo, float roughness, float NoV, float NoL, float VoL)
{
    const float eps = OPENPBR_MIN_EON_EPS;

    // Match YutrelRender exactly: no internal clamping; the caller guarantees
    // NoV, NoL > 0 and VoL = dot(V, L) (raw, not saturated).
    float mu_o = NoV;
    float mu_i = NoL;
    float s = VoL - mu_o * mu_i;
    float sovertF = s > 0.0f ? s / max(mu_i, mu_o) : s;

    float a = 1.0f / (1.0f + OpenPBR_FONConstantA() * roughness);
    float3 f_ss = diffuse_albedo * INV_PI * a * (1.0f + roughness * sovertF);

    float e_o = OpenPBR_E_FON_Approx(mu_o, roughness);
    float e_i = OpenPBR_E_FON_Approx(mu_i, roughness);
    float e_average = a * (1.0f + OpenPBR_FONConstantB() * roughness);

    float3 rho_ms = diffuse_albedo * diffuse_albedo * e_average /
                    max(1.0f - diffuse_albedo * (1.0f - e_average), eps);
    float3 f_ms = rho_ms * INV_PI * max(eps, 1.0f - e_o) * max(eps, 1.0f - e_i) /
                  max(eps, 1.0f - e_average);

    return f_ss + f_ms;
}

#endif // YUTREL_OPENPBR_INCLUDED
