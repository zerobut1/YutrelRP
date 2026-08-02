#ifndef YUTREL_SHADING_MODEL_STANDARD_INCLUDED
#define YUTREL_SHADING_MODEL_STANDARD_INCLUDED

#include "BRDF.hlsl"
#include "GBuffer.hlsl"
#include "Light.hlsl"

struct StandardSurface
{
    float3 diffuse_color;
    float3 normal_WS;
    float perceptual_roughness;
    float roughness;
    float3 f0;
    float3 position_WS;
    float3 view_direction_WS;
    float NoV;
    float material_AO;
};

struct StandardBRDFTerms
{
    float3 diffuse;
    float3 specular;
    float D;
    float V;
    float NoH;
    float NoL;
};

StandardSurface GBuffer2StandardSurface(GBufferData data)
{
    StandardSurface surface;

    float metallic = saturate(data.metallic);
    float specular = saturate(data.specular);

    surface.diffuse_color        = (1.0f - metallic) * data.base_color;
    surface.normal_WS            = data.normal_WS;
    float perceptual_roughness   = clamp(data.roughness, 0.045, 1.0);
    surface.perceptual_roughness = perceptual_roughness;
    surface.roughness            = perceptual_roughness * perceptual_roughness;
    float dielectric_f0          = 0.08f * specular;
    surface.f0                   = lerp(float3(dielectric_f0, dielectric_f0, dielectric_f0), data.base_color, metallic);
    surface.position_WS          = ComputeWorldSpacePositionFromFullScreenUV(data.uv, data.scene_depth);
    surface.view_direction_WS    = GetWorldSpaceViewDirectionForSurface(surface.position_WS);
    surface.NoV                  = clamp(dot(surface.normal_WS, surface.view_direction_WS), MIN_N_DOT_V, 1.0f);
    surface.material_AO          = saturate(data.material_AO);

    return surface;
}

float3 StandardEnergyCompensationFromDfgVisibility(StandardSurface surface, float dfg_visibility)
{
    dfg_visibility = max(dfg_visibility, 1e-4f);
    return 1.0f + surface.f0 * (1.0f / dfg_visibility - 1.0f);
}

float3 StandardEnergyCompensation(StandardSurface surface)
{
    float dfg_visibility = SAMPLE_TEXTURE2D_LOD(
                               _DFG_LUT,
                               sampler_DFG_LUT,
                               float2(surface.NoV, surface.perceptual_roughness),
                               0.0f)
                               .g;
    return StandardEnergyCompensationFromDfgVisibility(surface, dfg_visibility);
}

StandardBRDFTerms StandardEvaluateBRDF(StandardSurface surface, Light light)
{
    StandardBRDFTerms terms;

    float3 h = normalize(surface.view_direction_WS + light.direction);

    float NoV = surface.NoV;
    float NoL = saturate(dot(surface.normal_WS, light.direction));
    float NoH = saturate(dot(surface.normal_WS, h));
    float LoH = saturate(dot(light.direction, h));

    // diffuse
    terms.diffuse = surface.diffuse_color * diffuse();

    // specular
    float D  = distribution(surface.roughness, NoH);
    float V  = visibility(surface.roughness, NoV, NoL);
    float3 F = fresnel(surface.f0, LoH);

    terms.specular = (D * V) * F * StandardEnergyCompensation(surface);
    terms.D        = D;
    terms.V        = V;
    terms.NoH      = NoH;
    terms.NoL      = NoL;

    return terms;
}

float3 StandardShading(StandardSurface surface, Light light)
{
    StandardBRDFTerms terms = StandardEvaluateBRDF(surface, light);
    float3 out_color        = terms.diffuse + terms.specular;

    out_color = out_color * light.color * light.illuminance * terms.NoL * light.occlusion;

    return out_color;
}

#endif
