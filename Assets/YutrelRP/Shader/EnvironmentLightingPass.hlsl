#ifndef YUTREL_ENVIRONMENT_LIGHTING_PASS_INCLUDED
#define YUTREL_ENVIRONMENT_LIGHTING_PASS_INCLUDED

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/EntityLighting.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/SphericalHarmonics.hlsl"
#include "Utils/ShadingModelStandard.hlsl"

#ifndef UNITY_SPECCUBE_LOD_STEPS
#define UNITY_SPECCUBE_LOD_STEPS 6
#endif

TEXTURECUBE(_EnvironmentReflectionCube);
SAMPLER(sampler_EnvironmentReflectionCube);

float4 _EnvironmentReflectionCube_HDR;
float _EnvironmentReflectionAvailable;

float4 _AmbientProbeSHAr;
float4 _AmbientProbeSHAg;
float4 _AmbientProbeSHAb;
float4 _AmbientProbeSHBr;
float4 _AmbientProbeSHBg;
float4 _AmbientProbeSHBb;
float4 _AmbientProbeSHC;

float EnvironmentPerceptualRoughnessToMipmapLevel(float perceptual_roughness)
{
    perceptual_roughness = perceptual_roughness * (1.7f - 0.7f * perceptual_roughness);
    return perceptual_roughness * UNITY_SPECCUBE_LOD_STEPS;
}

float3 EvaluateEnvironmentDiffuse(float3 normal_WS)
{
    float3 irradiance = SHEvalLinearL0L1(normal_WS, _AmbientProbeSHAr, _AmbientProbeSHAg, _AmbientProbeSHAb);
    irradiance += SHEvalLinearL2(normal_WS, _AmbientProbeSHBr, _AmbientProbeSHBg, _AmbientProbeSHBb, _AmbientProbeSHC);
    return max(0.0f, irradiance);
}

float3 EvaluateEnvironmentSpecular(StandardSurface surface)
{
    if (_EnvironmentReflectionAvailable < 0.5f)
    {
        return 0.0f;
    }

    float3 reflection_direction = reflect(-surface.view_direction_WS, surface.normal_WS);
    float mip_level             = EnvironmentPerceptualRoughnessToMipmapLevel(surface.perceptual_roughness);
    float4 encoded_specular =
        SAMPLE_TEXTURECUBE_LOD(_EnvironmentReflectionCube, sampler_EnvironmentReflectionCube, reflection_direction, mip_level);
    float3 prefiltered_specular = DecodeHDREnvironment(encoded_specular, _EnvironmentReflectionCube_HDR);
    float2 environment_brdf     = SAMPLE_TEXTURE2D(_BRDF_LUT, sampler_BRDF_LUT, float2(surface.NoV, surface.perceptual_roughness)).rg;

    return prefiltered_specular * (surface.f0 * environment_brdf.x + environment_brdf.y);
}

float4 EnvironmentLightingFragment(FullScreenVaryings input) : SV_Target
{
    EncodedGBuffer gbuffer;
    gbuffer.scene_color = float4(0, 0, 0, 0);
    gbuffer.GBuffer_A   = SAMPLE_TEXTURE2D(_GBuffer_A, sampler_GBuffer_A, input.uv);
    gbuffer.GBuffer_B   = SAMPLE_TEXTURE2D(_GBuffer_B, sampler_GBuffer_B, input.uv);
    gbuffer.GBuffer_C   = SAMPLE_TEXTURE2D(_GBuffer_C, sampler_GBuffer_C, input.uv);
    gbuffer.scene_depth = SAMPLE_TEXTURE2D(_SceneDepth, sampler_SceneDepth, input.uv).r;
    gbuffer.uv          = input.uv;

    GBufferData gbuffer_data = DecodeGBuffer(gbuffer);
    if (gbuffer_data.shading_model_id != 1)
    {
        discard;
    }

    StandardSurface surface = GBuffer2StandardSurface(gbuffer_data);
    float screen_space_AO   = 1.0f;
    float final_diffuse_AO  = min(surface.material_AO, screen_space_AO);
    float3 diffuse_IBL      = EvaluateEnvironmentDiffuse(surface.normal_WS) * surface.diffuse_color * final_diffuse_AO;
    float3 specular_IBL     = EvaluateEnvironmentSpecular(surface);

    return float4(diffuse_IBL + specular_IBL, 0.0f);
}

#endif
