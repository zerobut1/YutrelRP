#ifndef YUTREL_ENDFIELD_CHARACTER_PBR_SURFACE_INCLUDED
#define YUTREL_ENDFIELD_CHARACTER_PBR_SURFACE_INCLUDED

struct EndfieldCharacterSurfaceInput
{
    float2 uv;
    float3 normal_WS;
    float3 tangent_WS;
    float3 bitangent_WS;
};

struct EndfieldCharacterSurfaceData
{
    float4 base_color;
    float3 normal_WS;
};

struct EndfieldCharacterPBRSurfaceData
{
    float3 diffuse_color;
    float3 normal_WS;
    float perceptual_roughness;
    float roughness;
    float3 f0;
    float material_AO;
};

float2 EndfieldCharacterTransformUV(float2 uv, float4 texture_ST)
{
    return uv * texture_ST.xy + texture_ST.zw;
}

float2 EndfieldCharacterGetBaseUV(float2 uv)
{
    float4 texture_ST = UNITY_ACCESS_INSTANCED_PROP(EndfieldCharacterPBRPerMaterial, _EndfieldBaseMap_ST);
    return EndfieldCharacterTransformUV(uv, texture_ST);
}

float4 EndfieldCharacterSampleBaseColor(float2 uv)
{
    float2 base_uv   = EndfieldCharacterGetBaseUV(uv);
    float4 base_map  = SAMPLE_TEXTURE2D(_EndfieldBaseMap, sampler_EndfieldBaseMap, base_uv);
    float4 base_tint = UNITY_ACCESS_INSTANCED_PROP(EndfieldCharacterPBRPerMaterial, _EndfieldBaseColor);
    return base_map * base_tint;
}

void EndfieldCharacterClipAlpha(float alpha)
{
    float alpha_cutoff = UNITY_ACCESS_INSTANCED_PROP(EndfieldCharacterPBRPerMaterial, _EndfieldAlphaCutoff);
    clip(alpha - alpha_cutoff);
}

float3 EndfieldCharacterSampleNormalWS(EndfieldCharacterSurfaceInput input)
{
    float4 texture_ST    = UNITY_ACCESS_INSTANCED_PROP(EndfieldCharacterPBRPerMaterial, _EndfieldNormalMap_ST);
    float2 normal_uv     = EndfieldCharacterTransformUV(input.uv, texture_ST);
    float4 packed_normal = SAMPLE_TEXTURE2D(_EndfieldNormalMap, sampler_EndfieldNormalMap, normal_uv);
    float normal_scale   = UNITY_ACCESS_INSTANCED_PROP(EndfieldCharacterPBRPerMaterial, _EndfieldNormalScale);
    float3 normal_TS     = UnpackNormalScale(packed_normal, normal_scale);

    return normalize(
        normal_TS.x * input.tangent_WS +
        normal_TS.y * input.bitangent_WS +
        normal_TS.z * input.normal_WS);
}

EndfieldCharacterSurfaceData EndfieldCharacterEvaluateSurface(EndfieldCharacterSurfaceInput input)
{
    EndfieldCharacterSurfaceData surface;
    surface.base_color = EndfieldCharacterSampleBaseColor(input.uv);
    surface.normal_WS  = EndfieldCharacterSampleNormalWS(input);
    return surface;
}

EndfieldCharacterPBRSurfaceData EndfieldCharacterEvaluatePBRSurface(EndfieldCharacterSurfaceInput input)
{
    EndfieldCharacterSurfaceData base_surface = EndfieldCharacterEvaluateSurface(input);
    float2 packed_uv                          = EndfieldCharacterGetBaseUV(input.uv);
    float4 packed                             = SAMPLE_TEXTURE2D(_EndfieldPackedMap, sampler_EndfieldPackedMap, packed_uv);
    float metallic                            = saturate(packed.r);
    float specular_level                      = saturate(packed.g);
    float smoothness                          = saturate(packed.a);
    float perceptual_roughness                = clamp(1.0f - smoothness, 0.045f, 1.0f);

    EndfieldCharacterPBRSurfaceData surface;
    surface.diffuse_color        = base_surface.base_color.rgb * (1.0f - 0.96f * metallic);
    surface.normal_WS            = base_surface.normal_WS;
    surface.perceptual_roughness = perceptual_roughness;
    surface.roughness            = perceptual_roughness * perceptual_roughness;
    surface.f0                   = lerp(0.04f * specular_level, base_surface.base_color.rgb, metallic);
    surface.material_AO          = saturate(packed.b);
    return surface;
}

#endif
