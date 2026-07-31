#ifndef YUTREL_ENDFIELD_CHARACTER_BASE_PASS_INCLUDED
#define YUTREL_ENDFIELD_CHARACTER_BASE_PASS_INCLUDED

#include "../Utils/GBuffer.hlsl"

struct EndfieldCharacterBaseAttributes
{
    float3 position_OS : POSITION;
    float3 normal_OS : NORMAL;
    float4 tangent_OS : TANGENT;
    float2 uv : TEXCOORD0;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct EndfieldCharacterBaseVaryings
{
    float4 position_CS : SV_POSITION;
    float3 normal_WS : VAR_NORMAL;
    float3 tangent_WS : VAR_TANGENT;
    float3 bitangent_WS : VAR_BITANGENT;
    float2 uv : VAR_BASE_UV;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct EndfieldCharacterBaseOutput
{
    float4 scene_color : SV_Target0;
    float4 GBuffer_A : SV_Target1;
    float4 GBuffer_B : SV_Target2;
    float4 GBuffer_C : SV_Target3;
};

EndfieldCharacterBaseVaryings EndfieldCharacterBaseVertex(EndfieldCharacterBaseAttributes input)
{
    EndfieldCharacterBaseVaryings output;
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

EndfieldCharacterBaseOutput EndfieldCharacterBaseFragment(
    EndfieldCharacterBaseVaryings input,
    bool is_front_face : SV_IsFrontFace)
{
    UNITY_SETUP_INSTANCE_ID(input);

    EndfieldCharacterSurfaceInput surface_input;
    surface_input.uv           = input.uv;
    surface_input.normal_WS    = input.normal_WS;
    surface_input.tangent_WS   = input.tangent_WS;
    surface_input.bitangent_WS = input.bitangent_WS;

    EndfieldCharacterSurfaceData surface = EndfieldCharacterEvaluateSurface(surface_input);
    EndfieldCharacterClipAlpha(surface.base_color.a);
    if (!is_front_face)
    {
        surface.normal_WS = -surface.normal_WS;
    }

    GBufferData gbuffer      = (GBufferData)0;
    gbuffer.normal_WS        = surface.normal_WS;
    gbuffer.shading_model_id = SHADING_MODEL_ENDFIELD;
    EncodedGBuffer encoded   = EncodeGBuffer(gbuffer);

    EndfieldCharacterBaseOutput output;
    output.scene_color = encoded.scene_color;
    output.GBuffer_A   = encoded.GBuffer_A;
    output.GBuffer_B   = encoded.GBuffer_B;
    output.GBuffer_C   = encoded.GBuffer_C;
    return output;
}

#endif
