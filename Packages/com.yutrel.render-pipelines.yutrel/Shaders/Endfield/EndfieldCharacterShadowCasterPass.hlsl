#ifndef YUTREL_ENDFIELD_CHARACTER_SHADOW_CASTER_PASS_INCLUDED
#define YUTREL_ENDFIELD_CHARACTER_SHADOW_CASTER_PASS_INCLUDED

struct EndfieldCharacterShadowCasterAttributes
{
    float3 position_OS : POSITION;
    float2 uv : TEXCOORD0;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct EndfieldCharacterShadowCasterVaryings
{
    float4 position_CS_SS : SV_POSITION;
    float2 uv : VAR_BASE_UV;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

EndfieldCharacterShadowCasterVaryings EndfieldCharacterShadowCasterVertex(
    EndfieldCharacterShadowCasterAttributes input)
{
    EndfieldCharacterShadowCasterVaryings output;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);

    float3 position_WS    = TransformObjectToWorld(input.position_OS);
    output.position_CS_SS = TransformWorldToHClip(position_WS);
    output.uv             = input.uv;

#if UNITY_REVERSED_Z
    output.position_CS_SS.z = min(
        output.position_CS_SS.z,
        output.position_CS_SS.w * UNITY_NEAR_CLIP_VALUE);
#else
    output.position_CS_SS.z = max(
        output.position_CS_SS.z,
        output.position_CS_SS.w * UNITY_NEAR_CLIP_VALUE);
#endif

    return output;
}

void EndfieldCharacterShadowCasterFragment(EndfieldCharacterShadowCasterVaryings input)
{
    UNITY_SETUP_INSTANCE_ID(input);
    EndfieldCharacterClipAlpha(EndfieldCharacterSampleBaseColor(input.uv).a);
}

#endif
