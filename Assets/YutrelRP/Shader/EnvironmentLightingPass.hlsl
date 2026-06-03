#ifndef YUTREL_ENVIRONMENT_LIGHTING_PASS_INCLUDED
#define YUTREL_ENVIRONMENT_LIGHTING_PASS_INCLUDED

#include "DDGI/DDGI.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/EntityLighting.hlsl"
#include "Utils/ShadingModelStandard.hlsl"

TEXTURECUBE(_EnvironmentReflectionCube);
SAMPLER(sampler_EnvironmentReflectionCube);
TEXTURE2D(_ScreenSpaceAO);
SAMPLER(sampler_ScreenSpaceAO);
TEXTURE2D_ARRAY(_DDGIProbeIrradiance);
SAMPLER(sampler_DDGIProbeIrradiance);
TEXTURE2D_ARRAY(_DDGIProbeDistance);
SAMPLER(sampler_DDGIProbeDistance);

float4 _EnvironmentReflectionCube_HDR;
float _EnvironmentIntensity;
float _EnvironmentDiffuseMultiplier;
float _EnvironmentSpecularMultiplier;
float _IblRoughnessOneLevel;
float4 _DDGIProbeCount;
float4 _DDGIProbeIrradianceDimensions;
float4 _DDGIProbeDistanceDimensions;
float _DDGIProbeRayDataMaxDistance;
float3 _DDGIVolumeMinWS;
float3 _DDGIVolumeMaxWS;
float3 _DDGIProbeSpacingWS;
float _DDGIGatherValid;
float _DDGIDiffuseIntensity;

float4 _IblSH0;
float4 _IblSH1;
float4 _IblSH2;
float4 _IblSH3;
float4 _IblSH4;
float4 _IblSH5;
float4 _IblSH6;
float4 _IblSH7;
float4 _IblSH8;

float EnvironmentPerceptualRoughnessToMipmapLevel(float perceptual_roughness)
{
    return _IblRoughnessOneLevel * perceptual_roughness * (2.0f - perceptual_roughness);
}

float3 EvaluateEnvironmentDiffuse(float3 normal_WS)
{
    // Filament cmgen writes SH3 coefficients pre-scaled for shader reconstruction.
    // The Lambert 1/pi factor is already baked into these coefficients.
    float3 irradiance = _IblSH0.rgb;
    irradiance += _IblSH1.rgb * normal_WS.y;
    irradiance += _IblSH2.rgb * normal_WS.z;
    irradiance += _IblSH3.rgb * normal_WS.x;
    irradiance += _IblSH4.rgb * (normal_WS.y * normal_WS.x);
    irradiance += _IblSH5.rgb * (normal_WS.y * normal_WS.z);
    irradiance += _IblSH6.rgb * (3.0f * normal_WS.z * normal_WS.z - 1.0f);
    irradiance += _IblSH7.rgb * (normal_WS.z * normal_WS.x);
    irradiance += _IblSH8.rgb * (normal_WS.x * normal_WS.x - normal_WS.y * normal_WS.y);
    return max(0.0f, irradiance);
}

uint3 GetDDGIProbeCount()
{
    return uint3((uint)max(_DDGIProbeCount.x, 1.0f), (uint)max(_DDGIProbeCount.y, 1.0f), (uint)max(_DDGIProbeCount.z, 1.0f));
}

float EvaluateDDGICoverage(float3 position_WS)
{
    return _DDGIGatherValid > 0.5f ? DDGIVolumeCoverage(position_WS, _DDGIVolumeMinWS, _DDGIVolumeMaxWS, _DDGIProbeSpacingWS) : 0.0f;
}

DDGIGatherSample EvaluateDDGIGather(float3 position_WS, float3 normal_WS, float3 view_direction_WS)
{
    uint3 probe_count       = GetDDGIProbeCount();
    uint irradiance_texels  = (uint)max(_DDGIProbeIrradianceDimensions.w - 2.0f, 1.0f);
    uint distance_texels    = (uint)max(_DDGIProbeDistanceDimensions.w - 2.0f, 1.0f);
    DDGIGatherSample sample = DDGISampleTrilinearGather(_DDGIProbeIrradiance, sampler_DDGIProbeIrradiance, _DDGIProbeDistance, sampler_DDGIProbeDistance, position_WS, normal_WS, view_direction_WS, _DDGIVolumeMinWS, _DDGIVolumeMaxWS, _DDGIProbeSpacingWS, probe_count, irradiance_texels, distance_texels, _DDGIProbeIrradianceDimensions, _DDGIProbeDistanceDimensions, _DDGIProbeRayDataMaxDistance);
    sample.irradiance *= _DDGIDiffuseIntensity;
    sample.coverage *= _DDGIGatherValid > 0.5f ? 1.0f : 0.0f;
    return sample;
}

float3 SampleEnvironmentDfg(StandardSurface surface)
{
    return SAMPLE_TEXTURE2D(_DFG_LUT, sampler_DFG_LUT, float2(surface.NoV, surface.perceptual_roughness)).rgb;
}

float3 EvaluateEnvironmentSpecularDfg(StandardSurface surface, float3 dfg)
{
    return lerp(dfg.xxx, dfg.yyy, surface.f0);
}

float EvaluateEnvironmentSpecularAO(StandardSurface surface, float diffuse_visibility)
{
    return saturate(pow(surface.NoV + diffuse_visibility, exp2(-16.0f * surface.roughness - 1.0f)) - 1.0f +
                    diffuse_visibility);
}

float3 GetEnvironmentSpecularDominantDirection(StandardSurface surface)
{
    float3 reflection_direction = reflect(-surface.view_direction_WS, surface.normal_WS);
    return lerp(reflection_direction, surface.normal_WS, surface.roughness * surface.roughness);
}

float3 EvaluateEnvironmentSpecular(StandardSurface surface, float3 specular_dfg, float3 energy_compensation,
                                   float specular_AO)
{
    float3 reflection_direction = GetEnvironmentSpecularDominantDirection(surface);
    float mip_level             = EnvironmentPerceptualRoughnessToMipmapLevel(surface.perceptual_roughness);
    float4 encoded_specular =
        SAMPLE_TEXTURECUBE_LOD(_EnvironmentReflectionCube, sampler_EnvironmentReflectionCube, reflection_direction, mip_level);
    float3 prefiltered_specular = DecodeHDREnvironment(encoded_specular, _EnvironmentReflectionCube_HDR);

    return prefiltered_specular * specular_dfg * energy_compensation * specular_AO * _EnvironmentIntensity *
           _EnvironmentSpecularMultiplier;
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

    StandardSurface surface    = GBuffer2StandardSurface(gbuffer_data);
    float screen_space_AO      = saturate(SAMPLE_TEXTURE2D(_ScreenSpaceAO, sampler_ScreenSpaceAO, input.uv).r);
    float final_diffuse_AO     = min(surface.material_AO, screen_space_AO);
    float3 dfg                 = SampleEnvironmentDfg(surface);
    float3 specular_dfg        = EvaluateEnvironmentSpecularDfg(surface, dfg);
    float3 energy_compensation = StandardEnergyCompensationFromDfgVisibility(surface, dfg.g);
    float specular_AO          = EvaluateEnvironmentSpecularAO(surface, final_diffuse_AO);
    float3 diffuse_scale       = surface.diffuse_color * (1.0f - specular_dfg) * final_diffuse_AO;
    float3 environment_diffuse = EvaluateEnvironmentDiffuse(surface.normal_WS) * diffuse_scale * _EnvironmentIntensity *
                                 _EnvironmentDiffuseMultiplier;
    DDGIGatherSample ddgi      = EvaluateDDGIGather(surface.position_WS, surface.normal_WS, surface.view_direction_WS);
    float3 diffuse_IBL         = environment_diffuse;
    if (ddgi.coverage > 0.0f)
    {
        float3 ddgi_diffuse = ddgi.irradiance * diffuse_scale;
        diffuse_IBL         = lerp(environment_diffuse, ddgi_diffuse, ddgi.coverage);
    }
    float3 specular_IBL = EvaluateEnvironmentSpecular(surface, specular_dfg, energy_compensation, specular_AO);

    return float4(ApplyPreExposure(diffuse_IBL + specular_IBL), 0.0f);
}

#endif
