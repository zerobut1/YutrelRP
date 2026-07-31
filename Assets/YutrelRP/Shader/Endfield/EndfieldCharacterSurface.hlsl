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

float2 EndfieldCharacterTransformUV(float2 uv, float4 texture_ST)
{
    return uv * texture_ST.xy + texture_ST.zw;
}

float4 EndfieldCharacterSampleBaseColor(float2 uv)
{
    float4 texture_ST = UNITY_ACCESS_INSTANCED_PROP(EndfieldCharacterPerMaterial, _EndfieldBaseMap_ST);
    float2 base_uv    = EndfieldCharacterTransformUV(uv, texture_ST);
    float4 base_map   = SAMPLE_TEXTURE2D(_EndfieldBaseMap, sampler_EndfieldBaseMap, base_uv);
    float4 base_tint  = UNITY_ACCESS_INSTANCED_PROP(EndfieldCharacterPerMaterial, _EndfieldBaseColor);
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

#endif
