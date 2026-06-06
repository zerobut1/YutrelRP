#ifndef YUTREL_DEFAULTLIT_INCLUDED
#define YUTREL_DEFAULTLIT_INCLUDED

#include "Assets/YutrelRP/Shader/DefaultLitSurfaceContract.hlsl"

struct Attributes
{
    float3 position_OS : POSITION;
    float3 normal_OS : NORMAL;
    float4 tangent_OS : TANGENT;
    float2 uv : TEXCOORD0;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct Varyings
{
    float4 position_CS : SV_POSITION;
    float3 normal_WS : VAR_NORMAL;
    float3 tangent_WS : VAR_TANGENT;
    float3 bitangent_WS : VAR_BITANGENT;
    float2 uv : VAR_BASE_UV;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct RTStruct
{
    float4 scene_color : SV_Target0;
    float4 GBuffer_A : SV_Target1;
    float4 GBuffer_B : SV_Target2;
    float4 GBuffer_C : SV_Target3;
};

DefaultLitSurfaceInput BuildDefaultLitSurfaceInput(Varyings input)
{
    DefaultLitSurfaceInput surface_input;
    surface_input.uv           = input.uv;
    surface_input.normal_WS    = input.normal_WS;
    surface_input.tangent_WS   = input.tangent_WS;
    surface_input.bitangent_WS = input.bitangent_WS;
    return surface_input;
}

Varyings DefaultLitVertex(Attributes input)
{
    Varyings output;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);
    float3 position_WS  = TransformObjectToWorld(input.position_OS.xyz);
    float4 position_CS  = TransformWorldToHClip(position_WS);
    float3 normal_WS    = TransformObjectToWorldNormal(input.normal_OS);
    float3 tangent_WS   = normalize(TransformObjectToWorldDir(input.tangent_OS.xyz));
    float tangent_sign  = input.tangent_OS.w * GetOddNegativeScale();
    float3 bitangent_WS = normalize(cross(normal_WS, tangent_WS) * tangent_sign);

    output.position_CS  = position_CS;
    output.normal_WS    = normal_WS;
    output.tangent_WS   = tangent_WS;
    output.bitangent_WS = bitangent_WS;
    output.uv           = input.uv;
    return output;
}

RTStruct DefaultLitFragment(Varyings input, bool is_front_face : SV_IsFrontFace)
{
    RTStruct output;
    UNITY_SETUP_INSTANCE_ID(input);

    DefaultLitSurfaceInput surface_input = BuildDefaultLitSurfaceInput(input);
    DefaultLitSurfaceResult surface      = EvaluateDefaultLitSurface(surface_input);
    ClipDefaultLitSurface(surface.alpha_clip);
    if (!is_front_face)
    {
        surface.surface.normal_WS = -surface.surface.normal_WS;
    }

    GBufferData gbuffer = DefaultLitSurfaceToGBuffer(surface.surface);

    EncodedGBuffer encoded_gbuffer = EncodeGBuffer(gbuffer);
    output.scene_color             = encoded_gbuffer.scene_color;
    output.GBuffer_A               = encoded_gbuffer.GBuffer_A;
    output.GBuffer_B               = encoded_gbuffer.GBuffer_B;
    output.GBuffer_C               = encoded_gbuffer.GBuffer_C;

    return output;
}

struct ShadowAttributes
{
    float3 position_OS : POSITION;
    float2 uv : TEXCOORD0;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct ShadowVaryings
{
    float4 position_CS_SS : SV_POSITION;
    float2 uv : VAR_BASE_UV;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

ShadowVaryings DefaultLitShadowCasterVertex(ShadowAttributes input)
{
    ShadowVaryings output;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);
    float3 position_WS    = TransformObjectToWorld(input.position_OS);
    output.position_CS_SS = TransformWorldToHClip(position_WS);
    output.uv             = input.uv;

// Shadow pancaking: clamp vertices behind the near plane to the near plane
// to prevent near-plane clipping artifacts (light leaking) in CSM.
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

DefaultLitSurfaceInput BuildDefaultLitShadowSurfaceInput(ShadowVaryings input)
{
    DefaultLitSurfaceInput surface_input;
    surface_input.uv           = input.uv;
    surface_input.normal_WS    = 0.0f;
    surface_input.tangent_WS   = 0.0f;
    surface_input.bitangent_WS = 0.0f;
    return surface_input;
}

void DefaultLitShadowCasterFragment(ShadowVaryings input)
{
    UNITY_SETUP_INSTANCE_ID(input);
    DefaultLitSurfaceInput surface_input = BuildDefaultLitShadowSurfaceInput(input);
    ClipDefaultLitSurface(EvaluateDefaultLitAlphaClip(surface_input));
}

#endif
