#ifndef YUTREL_ENDFIELD_CHARACTER_FORWARD_PASS_INCLUDED
#define YUTREL_ENDFIELD_CHARACTER_FORWARD_PASS_INCLUDED

struct EndfieldCharacterForwardAttributes
{
    float3 position_OS : POSITION;
    float2 uv : TEXCOORD0;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct EndfieldCharacterForwardVaryings
{
    float4 position_CS : SV_POSITION;
    float2 uv : VAR_BASE_UV;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

EndfieldCharacterForwardVaryings EndfieldCharacterForwardVertex(EndfieldCharacterForwardAttributes input)
{
    EndfieldCharacterForwardVaryings output;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);

    float3 position_WS = TransformObjectToWorld(input.position_OS);
    output.position_CS = TransformWorldToHClip(position_WS);
    output.uv          = input.uv;
    return output;
}

float4 EndfieldCharacterForwardFragment(EndfieldCharacterForwardVaryings input) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(input);
    float4 base_color = EndfieldCharacterSampleBaseColor(input.uv);
    return float4(base_color.rgb, 0.0f);
}

#endif
