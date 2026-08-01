#ifndef YUTREL_ENDFIELD_CHARACTER_FORWARD_PASS_INCLUDED
#define YUTREL_ENDFIELD_CHARACTER_FORWARD_PASS_INCLUDED

#include "../Utils/ShadingModelStandard.hlsl"

struct EndfieldCharacterForwardAttributes
{
    float3 position_OS : POSITION;
    float3 normal_OS : NORMAL;
    float4 tangent_OS : TANGENT;
    float2 uv : TEXCOORD0;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct EndfieldCharacterForwardVaryings
{
    float4 position_CS : SV_POSITION;
    float3 position_WS : VAR_POSITION;
    float3 normal_WS : VAR_NORMAL;
    float3 tangent_WS : VAR_TANGENT;
    float3 bitangent_WS : VAR_BITANGENT;
    float2 uv : VAR_BASE_UV;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

EndfieldCharacterForwardVaryings EndfieldCharacterForwardVertex(EndfieldCharacterForwardAttributes input)
{
    EndfieldCharacterForwardVaryings output;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);

    float3 position_WS  = TransformObjectToWorld(input.position_OS);
    float3 normal_WS    = TransformObjectToWorldNormal(input.normal_OS);
    float3 tangent_WS   = normalize(TransformObjectToWorldDir(input.tangent_OS.xyz));
    float tangent_sign  = input.tangent_OS.w * GetOddNegativeScale();
    float3 bitangent_WS = normalize(cross(normal_WS, tangent_WS) * tangent_sign);

    output.position_CS  = TransformWorldToHClip(position_WS);
    output.position_WS  = position_WS;
    output.normal_WS    = normal_WS;
    output.tangent_WS   = tangent_WS;
    output.bitangent_WS = bitangent_WS;
    output.uv           = input.uv;
    return output;
}

StandardSurface EndfieldCharacterBuildStandardSurface(
    EndfieldCharacterPBRSurfaceData source,
    float3 position_WS)
{
    StandardSurface surface;
    surface.diffuse_color        = source.diffuse_color;
    surface.normal_WS            = source.normal_WS;
    surface.perceptual_roughness = source.perceptual_roughness;
    surface.roughness            = source.roughness;
    surface.f0                   = source.f0;
    surface.position_WS          = position_WS;
    surface.view_direction_WS    = GetWorldSpaceViewDirectionForSurface(position_WS);
    surface.NoV                  = clamp(dot(surface.normal_WS, surface.view_direction_WS), MIN_N_DOT_V, 1.0f);
    surface.material_AO          = source.material_AO;
    return surface;
}

float3 EndfieldCharacterAdjustSaturation(float3 color, float saturation)
{
    float luminance = Luminance(color);
    return lerp(luminance.xxx, color, saturation);
}

float3 EndfieldCharacterApplyDiffuseRampTint(float3 color, float3 ramp_color)
{
    float max_rgb = max(ramp_color.r, max(ramp_color.g, ramp_color.b));
    float min_rgb = min(ramp_color.r, min(ramp_color.g, ramp_color.b));
    float chroma  = max_rgb - min_rgb;

    float3 tint           = lerp(1.0f.xxx, ramp_color, chroma);
    float3 tinted_color   = color * tint;
    float luminance_scale = clamp(
        Luminance(color) / max(Luminance(tinted_color), 0.001f),
        0.0f,
        1.5f);
    return tinted_color * luminance_scale;
}

float3 EndfieldCharacterEvaluateDiffuseRamp(StandardSurface surface, Light light)
{
    float ramp_offset = UNITY_ACCESS_INSTANCED_PROP(EndfieldCharacterPerMaterial, _EndfieldDiffuseRampOffset);
    float main_u      = saturate(dot(surface.normal_WS, light.direction) * 0.5f + 0.5f + ramp_offset);
    float4 main_ramp  = SAMPLE_TEXTURE2D_LOD(
        _EndfieldDiffuseRamp,
        sampler_EndfieldDiffuseRamp,
        float2(main_u, 0.5f),
        0.0f);

    float3 camera_axis_WS = normalize(UNITY_MATRIX_V[2].xyz);
    float view_u          = saturate(dot(surface.normal_WS, camera_axis_WS) * 0.5f + 0.5f);
    float4 view_ramp      = SAMPLE_TEXTURE2D_LOD(
        _EndfieldDiffuseRamp,
        sampler_EndfieldDiffuseRamp,
        float2(view_u, 0.5f),
        0.0f);

    const float secondary_visibility = 1.0f;
    float ao_visibility              = surface.material_AO * secondary_visibility;
    float transition_weight          = saturate(main_ramp.a + ao_visibility * view_ramp.a);
    float visibility_gate            = min(main_ramp.a, min(surface.material_AO, secondary_visibility));
    float view_weight                = ao_visibility * view_ramp.a;

    float3 base_color  = surface.diffuse_color;
    float3 shadow_tone = EndfieldCharacterAdjustSaturation(base_color * 0.65f, 1.2f);
    float3 lit_tone    = EndfieldCharacterAdjustSaturation(base_color, 1.2f);

    float3 shaped_color = lerp(shadow_tone, base_color, transition_weight);
    shaped_color        = lerp(shaped_color, base_color, visibility_gate);

    float3 ramped_color  = EndfieldCharacterApplyDiffuseRampTint(shaped_color, main_ramp.rgb);
    float3 shadow_branch = lerp(base_color, lit_tone, view_weight);
    return lerp(shadow_branch, ramped_color, light.occlusion);
}

float4 EndfieldCharacterForwardFragment(
    EndfieldCharacterForwardVaryings input,
    bool is_front_face : SV_IsFrontFace) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(input);

    EndfieldCharacterSurfaceInput surface_input;
    surface_input.uv           = input.uv;
    surface_input.normal_WS    = input.normal_WS;
    surface_input.tangent_WS   = input.tangent_WS;
    surface_input.bitangent_WS = input.bitangent_WS;

    EndfieldCharacterPBRSurfaceData source = EndfieldCharacterEvaluatePBRSurface(surface_input);
    if (!is_front_face)
    {
        source.normal_WS = -source.normal_WS;
    }

    StandardSurface surface = EndfieldCharacterBuildStandardSurface(source, input.position_WS);
    float2 screen_uv        = input.position_CS.xy * _CameraBufferSize.xy;
    Light light             = GetDirectionalLight(0, screen_uv);

    float direct_intensity      = UNITY_ACCESS_INSTANCED_PROP(EndfieldCharacterPerMaterial, _EndfieldDirectIntensity);
    float reference_illuminance = UNITY_ACCESS_INSTANCED_PROP(
        EndfieldCharacterPerMaterial,
        _EndfieldReferenceIlluminance);
    light.illuminance = light.illuminance / max(reference_illuminance, 1.0f) * direct_intensity;

    StandardBRDFTerms brdf = StandardEvaluateBRDF(surface, light);
    float3 light_radiance  = light.color * light.illuminance;
    float3 direct_diffuse  = EndfieldCharacterEvaluateDiffuseRamp(surface, light) * light_radiance;
    float3 direct_specular = brdf.specular * light_radiance * brdf.NoL * light.occlusion;
    return float4(direct_diffuse + direct_specular, 0.0f);
}

#endif
