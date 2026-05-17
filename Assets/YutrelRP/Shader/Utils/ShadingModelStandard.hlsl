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
};

StandardSurface GBuffer2StandardSurface(GBufferData data)
{
    StandardSurface surface;

    float metallic = saturate(data.metallic);
    float specular = saturate(data.specular);

    surface.diffuse_color = (1.0f - metallic) * data.base_color;
    surface.normal_WS = data.normal_WS;
    float perceptual_roughness = clamp(data.roughness, 0.045, 1.0);
    surface.roughness = perceptual_roughness * perceptual_roughness;
    float dielectric_f0 = 0.08f * specular;
    surface.f0 = lerp(float3(dielectric_f0, dielectric_f0, dielectric_f0), data.base_color, metallic);
    surface.position_WS = ComputeWorldSpacePosition(data.uv, data.scene_depth, UNITY_MATRIX_I_VP);
    surface.view_direction_WS = normalize(_WorldSpaceCameraPos - surface.position_WS);
    surface.NoV = clamp(dot(surface.normal_WS, surface.view_direction_WS), MIN_N_DOT_V, 1.0f);

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

    out_color = Fd + Fr;

    out_color = out_color * light.color * light.intensity * NoL * light.occlusion;

    return out_color;
}

#endif
