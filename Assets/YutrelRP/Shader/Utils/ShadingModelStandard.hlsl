#ifndef YUTREL_SHADING_MODEL_STANDARD_INCLUDED
#define YUTREL_SHADING_MODEL_STANDARD_INCLUDED

#include "Light.hlsl"
#include "GBuffer.hlsl"
#include "BRDF.hlsl"

struct StandardSurface
{
    float3 diffuse_color;
    float3 normal_WS;
    float roughness;
    float3 f0;
    float3 position_WS;
    float3 view_direction_WS;
    float NoV;
    float2 DFG;
    float3 energy_compensation;
};

StandardSurface GBuffer2StandardSurface(GBufferData data)
{
    StandardSurface surface;

    surface.diffuse_color = (1.0f - data.metallic) * data.base_color;
    surface.normal_WS = data.normal_WS;
    float perceptual_roughness = clamp(data.roughness, 0.045, 1.0);
    surface.roughness = perceptual_roughness * perceptual_roughness;
    surface.f0 = 0.16 * data.specular * data.specular * (1.0 - data.metallic) + data.metallic * data.base_color;
    surface.position_WS = ComputeWorldSpacePosition(data.uv, data.scene_depth, UNITY_MATRIX_I_VP);
    surface.view_direction_WS = normalize(_WorldSpaceCameraPos - surface.position_WS);
    surface.NoV = saturate(dot(surface.normal_WS, surface.view_direction_WS));
    surface.DFG = SAMPLE_TEXTURE2D(_BRDF_LUT, sampler_BRDF_LUT, float2(surface.NoV, perceptual_roughness)).rg;
    surface.energy_compensation = 1.0 + surface.f0 * (1.0 / surface.DFG.r - 1.0);

    return surface;
}

float3 StandardShading(StandardSurface surface, Light light)
{
    float3 out_color = float3(0, 0, 0);

    float3 h = normalize(surface.view_direction_WS + light.direction);

    float NoV = surface.NoV;
    float NoL = saturate(dot(surface.normal_WS, light.direction));
    float NoH = saturate(dot(surface.normal_WS, h));
    float LoH = saturate(dot(light.direction, h));

    // diffuse
    float3 Fd = surface.diffuse_color * diffuse();

    // specular
    float D = distribution(surface.roughness, NoH);
    float V = visibility(surface.roughness, NoV, NoL);
    float3 F = fresnel(surface.f0, LoH);

    float3 Fr = (D * V) * F;

    out_color = Fd + Fr * surface.energy_compensation;

    out_color = out_color * light.color * light.intensity * NoL * light.attenuation;

    return out_color;
}

#endif
