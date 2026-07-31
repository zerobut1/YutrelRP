#ifndef YUTREL_ENDFIELD_CHARACTER_FORWARD_PASS_INCLUDED
#define YUTREL_ENDFIELD_CHARACTER_FORWARD_PASS_INCLUDED

#include "../Utils/Light.hlsl"

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
    output.normal_WS    = normal_WS;
    output.tangent_WS   = tangent_WS;
    output.bitangent_WS = bitangent_WS;
    output.uv           = input.uv;
    return output;
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

    EndfieldCharacterSurfaceData surface = EndfieldCharacterEvaluateSurface(surface_input);
    if (!is_front_face)
    {
        surface.normal_WS = -surface.normal_WS;
    }

    float2 screen_uv = input.position_CS.xy * _CameraBufferSize.xy;
    Light light      = GetDirectionalLight(0, screen_uv);

    float half_lambert = dot(surface.normal_WS, light.direction) * 0.5f + 0.5f;
    float ramp_offset  = UNITY_ACCESS_INSTANCED_PROP(EndfieldCharacterPerMaterial, _EndfieldRampOffset);
    float ramp_u       = saturate(half_lambert * light.occlusion + ramp_offset);
    float4 ramp        = SAMPLE_TEXTURE2D_LOD(
        _EndfieldDirectRamp,
        sampler_EndfieldDirectRamp,
        float2(ramp_u, 0.5f),
        0.0f);

    float direct_intensity      = UNITY_ACCESS_INSTANCED_PROP(EndfieldCharacterPerMaterial, _EndfieldDirectIntensity);
    float reference_illuminance = UNITY_ACCESS_INSTANCED_PROP(
        EndfieldCharacterPerMaterial,
        _EndfieldReferenceIlluminance);
    float light_strength = light.illuminance / max(reference_illuminance, 1.0f) * direct_intensity;

    float3 direct_color =
        surface.base_color.rgb * ramp.rgb * ramp.a * light.color * light_strength;
    return float4(direct_color, 0.0f);
}

#endif
