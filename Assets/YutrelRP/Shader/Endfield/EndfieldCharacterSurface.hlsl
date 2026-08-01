#ifndef YUTREL_ENDFIELD_CHARACTER_SURFACE_INCLUDED
#define YUTREL_ENDFIELD_CHARACTER_SURFACE_INCLUDED

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
    float4 texture_ST = UNITY_ACCESS_INSTANCED_PROP(EndfieldCharacterPerMaterial, _EndfieldBaseMap_ST);
    return EndfieldCharacterTransformUV(uv, texture_ST);
}

float4 EndfieldCharacterSampleBaseColor(float2 uv)
{
    float2 base_uv   = EndfieldCharacterGetBaseUV(uv);
    float4 base_map  = SAMPLE_TEXTURE2D(_EndfieldBaseMap, sampler_EndfieldBaseMap, base_uv);
    float4 base_tint = UNITY_ACCESS_INSTANCED_PROP(EndfieldCharacterPerMaterial, _EndfieldBaseColor);
    return base_map * base_tint;
}

void EndfieldCharacterClipAlpha(float alpha)
{
    float alpha_cutoff = UNITY_ACCESS_INSTANCED_PROP(EndfieldCharacterPerMaterial, _EndfieldAlphaCutoff);
    clip(alpha - alpha_cutoff);
}

float3 EndfieldCharacterSampleNormalWS(EndfieldCharacterSurfaceInput input)
{
    float4 texture_ST    = UNITY_ACCESS_INSTANCED_PROP(EndfieldCharacterPerMaterial, _EndfieldNormalMap_ST);
    float2 normal_uv     = EndfieldCharacterTransformUV(input.uv, texture_ST);
    float4 packed_normal = SAMPLE_TEXTURE2D(_EndfieldNormalMap, sampler_EndfieldNormalMap, normal_uv);
    float normal_scale   = UNITY_ACCESS_INSTANCED_PROP(EndfieldCharacterPerMaterial, _EndfieldNormalScale);
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

float3 EndfieldCharacterApplyColorLUT(float3 linear_color)
{
#if !defined(_ENDFIELD_COLOR_LUT)
    return linear_color;
#else
    const float size      = 32.0f;
    const float max_index = size - 1.0f;

    float3 coord  = saturate(LinearToSRGB(max(linear_color, 0.0f)));
    float blue    = coord.b * max_index;
    float slice_0 = floor(blue);
    float slice_1 = min(slice_0 + 1.0f, max_index);
    float2 uv     = (coord.rg * max_index + 0.5f) / float2(size * size, size);

    float3 color_0 = SAMPLE_TEXTURE2D_LOD(
                         _EndfieldColorLUT,
                         sampler_EndfieldColorLUT,
                         uv + float2(slice_0 / size, 0.0f),
                         0.0f)
                         .rgb;
    float3 color_1 = SAMPLE_TEXTURE2D_LOD(
                         _EndfieldColorLUT,
                         sampler_EndfieldColorLUT,
                         uv + float2(slice_1 / size, 0.0f),
                         0.0f)
                         .rgb;

    return lerp(color_0, color_1, frac(blue));
#endif
}

EndfieldCharacterPBRSurfaceData EndfieldCharacterEvaluatePBRSurface(EndfieldCharacterSurfaceInput input)
{
    EndfieldCharacterSurfaceData base_surface = EndfieldCharacterEvaluateSurface(input);
    float3 albedo                             = EndfieldCharacterApplyColorLUT(base_surface.base_color.rgb);
    float2 packed_uv                          = EndfieldCharacterGetBaseUV(input.uv);
    float4 packed                             = SAMPLE_TEXTURE2D(_EndfieldPackedMap, sampler_EndfieldPackedMap, packed_uv);
    float metallic                            = saturate(packed.r);
    float specular_level                      = saturate(packed.g);
    float smoothness                          = saturate(packed.a);
    float perceptual_roughness                = clamp(1.0f - smoothness, 0.045f, 1.0f);

    EndfieldCharacterPBRSurfaceData surface;
    surface.diffuse_color        = albedo * (1.0f - 0.96f * metallic);
    surface.normal_WS            = base_surface.normal_WS;
    surface.perceptual_roughness = perceptual_roughness;
    surface.roughness            = perceptual_roughness * perceptual_roughness;
    surface.f0                   = lerp(0.04f * specular_level, albedo, metallic);
    surface.material_AO          = saturate(packed.b);
    return surface;
}

#endif
