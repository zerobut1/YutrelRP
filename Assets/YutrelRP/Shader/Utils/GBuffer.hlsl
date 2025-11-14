#ifndef YUTREL_GBUFFER_INCLUDED
#define YUTREL_GBUFFER_INCLUDED

#include "Common.hlsl"

SAMPLER(_GBuffer_A);
SAMPLER(_GBuffer_B);
SAMPLER(_GBuffer_C);
SAMPLER(_SceneDepth);

struct GBufferData
{
    float3 base_color;
    float3 emissive;
    float3 normal_WS;
    float2 uv;
    float scene_depth;
    float roughness;
    float metallic;
    float specular;
    int shading_model_id;
};

struct EncodedGBuffer
{
    float4 scene_color;
    // GBuffer_A: RGB = Base Color, A = ShadingModelID
    float4 GBuffer_A;
    // GBuffer_B: RGB = World Normal
    float4 GBuffer_B;
    // GBuffer_C: R = Roughness, G = Metallic, B = SpecularS
    float4 GBuffer_C;
    float scene_depth;
    float2 uv;
};

EncodedGBuffer EncodeGBuffer(GBufferData data)
{
    EncodedGBuffer encoded;

    encoded.scene_color = float4(data.emissive, 0.0f);
    encoded.GBuffer_A = float4(data.base_color, data.shading_model_id);
    encoded.GBuffer_B = float4(normalize(data.normal_WS) * 0.5f + 0.5f, 0.0f);
    encoded.GBuffer_C = float4(data.roughness, data.metallic, data.specular, 0.0f);

    return encoded;
}

GBufferData DecodeGBuffer(EncodedGBuffer encoded)
{
    GBufferData data;

    data.base_color = encoded.GBuffer_A.rgb;
    data.emissive = float3(0, 0, 0);
    data.normal_WS = normalize(encoded.GBuffer_B.xyz * 2.0f - 1.0f);
    data.uv = encoded.uv;
    data.scene_depth = encoded.scene_depth;
    data.roughness = encoded.GBuffer_C.r;
    data.metallic = encoded.GBuffer_C.g;
    data.specular = encoded.GBuffer_C.b;
    data.shading_model_id = encoded.GBuffer_A.a > 0.5f ? 1 : 0;

    return data;
}

#endif
