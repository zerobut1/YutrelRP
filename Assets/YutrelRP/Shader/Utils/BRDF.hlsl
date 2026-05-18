#ifndef YUTREL_BRDF_INCLUDED
#define YUTREL_BRDF_INCLUDED

#define MIN_N_DOT_V 1e-4
#define MIN_BRDF_DENOMINATOR 1e-5

// -------------------------------------------------------------
// Specular BRDF
// -------------------------------------------------------------
float D_GGX(float roughness, float NoH)
{
    float a = NoH * roughness;
    float k = roughness / (1.0 - NoH * NoH + a * a);
    return k * k * INV_PI;
}

float V_SmithGGXCorrelated(float roughness, float NoV, float NoL)
{
    NoV        = max(NoV, MIN_N_DOT_V);
    float a2   = roughness * roughness;
    float GGXV = NoL * sqrt((NoV - a2 * NoV) * NoV + a2);
    float GGXL = NoV * sqrt((NoL - a2 * NoL) * NoL + a2);
    return 0.5 / max(GGXV + GGXL, MIN_BRDF_DENOMINATOR);
}

float3 F_Schlick(float3 f0, float u)
{
    float f = pow(1.0 - u, 5.0);
    return f + f0 * (1.0 - f);
}

float distribution(float roughness, float NoH)
{
    return D_GGX(roughness, NoH);
}

float visibility(float roughness, float NoV, float NoL)
{
    return V_SmithGGXCorrelated(roughness, NoV, NoL);
}

float3 fresnel(float3 f0, float LoH)
{
    return F_Schlick(f0, LoH);
}

// -------------------------------------------------------------
// Diffuse BRDF
// -------------------------------------------------------------
float Fd_Lambert()
{
    return INV_PI;
}

float diffuse()
{
    return Fd_Lambert();
}

#endif
