#ifndef YUTREL_ENDFIELD_CHARACTER_PBR_FORWARD_PASS_INCLUDED
#define YUTREL_ENDFIELD_CHARACTER_PBR_FORWARD_PASS_INCLUDED

#include "../Utils/ShadingModelStandard.hlsl"

struct EndfieldCharacterPBRForwardAttributes
{
    float3 position_OS : POSITION;
    float3 normal_OS : NORMAL;
    float4 tangent_OS : TANGENT;
    float2 uv : TEXCOORD0;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct EndfieldCharacterPBRForwardVaryings
{
    float4 position_CS : SV_POSITION;
    float3 position_WS : VAR_POSITION;
    float3 normal_WS : VAR_NORMAL;
    float3 tangent_WS : VAR_TANGENT;
    float3 bitangent_WS : VAR_BITANGENT;
    float2 uv : VAR_BASE_UV;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

EndfieldCharacterPBRForwardVaryings EndfieldCharacterPBRForwardVertex(
    EndfieldCharacterPBRForwardAttributes input)
{
    EndfieldCharacterPBRForwardVaryings output;
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

float4 EndfieldCharacterPBRForwardFragment(
    EndfieldCharacterPBRForwardVaryings input,
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

    float direct_intensity      = UNITY_ACCESS_INSTANCED_PROP(EndfieldCharacterPBRPerMaterial, _EndfieldDirectIntensity);
    float reference_illuminance = UNITY_ACCESS_INSTANCED_PROP(
        EndfieldCharacterPBRPerMaterial,
        _EndfieldReferenceIlluminance);
    light.illuminance = light.illuminance / max(reference_illuminance, 1.0f) * direct_intensity;

    return float4(StandardShading(surface, light), 0.0f);
}

#endif
