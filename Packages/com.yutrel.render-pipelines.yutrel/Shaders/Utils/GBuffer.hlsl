#ifndef YUTREL_GBUFFER_INCLUDED
#define YUTREL_GBUFFER_INCLUDED

#include "Common.hlsl"
#include "ShadingModel.hlsl"

TEXTURE2D(_GBuffer_A);
SAMPLER(sampler_GBuffer_A);
TEXTURE2D(_GBuffer_B);
SAMPLER(sampler_GBuffer_B);
TEXTURE2D(_GBuffer_C);
SAMPLER(sampler_GBuffer_C);
TEXTURE2D(_GBuffer_D);
SAMPLER(sampler_GBuffer_D);
TEXTURE2D(_SceneDepth);
SAMPLER(sampler_SceneDepth);

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
    float material_AO;
    int shading_model_id;
    // OpenPBR (SHADING_MODEL_OPENPBR) only:
    //   base_color holds weighted_base_color = base_color * base_weight
    //   specular holds specular_weight
    float3 specular_color;
    float sqrt_f0; // sqrt(weighted_f0): dielectric f0 after specular_weight, encoded as sqrt
    float diffuse_roughness;
};

struct EncodedGBuffer
{
    float4 scene_color;
    // GBuffer_A: RGB = Base Color, A = ShadingModelID
    float4 GBuffer_A;
    // GBuffer_B: RGB = World Normal, A = Diffuse Roughness (OpenPBR)
    float4 GBuffer_B;
    // GBuffer_C: R = Roughness, G = Metallic, B = Specular(Weight), A = Material AO
    float4 GBuffer_C;
    // GBuffer_D: RGB = Specular Color, A = sqrt(weighted_f0) (OpenPBR)
    float4 GBuffer_D;
    float scene_depth;
    float2 uv;
};

EncodedGBuffer EncodeGBuffer(GBufferData data)
{
    EncodedGBuffer encoded;

    encoded.scene_color = float4(ApplyPreExposure(data.emissive), 0.0f);
    encoded.GBuffer_A   = float4(data.base_color, EncodeShadingModelID(data.shading_model_id));
    encoded.GBuffer_B   = float4(normalize(data.normal_WS) * 0.5f + 0.5f, data.diffuse_roughness);
    encoded.GBuffer_C   = float4(data.roughness, data.metallic, data.specular, data.material_AO);
    encoded.GBuffer_D   = float4(data.specular_color, data.sqrt_f0);

    return encoded;
}

GBufferData DecodeGBuffer(EncodedGBuffer encoded)
{
    GBufferData data;

    data.base_color       = encoded.GBuffer_A.rgb;
    data.emissive         = float3(0, 0, 0);
    data.normal_WS        = normalize(encoded.GBuffer_B.xyz * 2.0f - 1.0f);
    data.uv               = encoded.uv;
    data.scene_depth      = encoded.scene_depth;
    data.roughness        = encoded.GBuffer_C.r;
    data.metallic         = encoded.GBuffer_C.g;
    data.specular         = encoded.GBuffer_C.b;
    data.material_AO      = saturate(encoded.GBuffer_C.a);
    data.shading_model_id = DecodeShadingModelID(encoded.GBuffer_A.a);
    data.specular_color   = encoded.GBuffer_D.rgb;
    data.sqrt_f0          = encoded.GBuffer_D.a;
    data.diffuse_roughness = encoded.GBuffer_B.a;

    return data;
}

#endif
