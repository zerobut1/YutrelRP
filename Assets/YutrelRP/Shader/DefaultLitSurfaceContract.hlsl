#ifndef YUTREL_DEFAULTLIT_SURFACE_CONTRACT_INCLUDED
#define YUTREL_DEFAULTLIT_SURFACE_CONTRACT_INCLUDED

#include "Assets/YutrelRP/Shader/Utils/GBuffer.hlsl"

struct DefaultLitSurfaceInput
{
    float2 uv;
    float3 normal_WS;
    float3 tangent_WS;
    float3 bitangent_WS;
};

struct DefaultLitSurfaceData
{
    float3 base_color;
    float3 emissive;
    float3 normal_WS;
    float roughness;
    float metallic;
    float specular;
    float material_AO;
    int shading_model_id;
};

struct DefaultLitAlphaClipData
{
    float alpha;
    float cutoff;
    float enabled;
};

struct DefaultLitSurfaceResult
{
    DefaultLitSurfaceData surface;
    DefaultLitAlphaClipData alpha_clip;
};

DefaultLitAlphaClipData DefaultLitAlphaClipOff()
{
    DefaultLitAlphaClipData alpha_clip;
    alpha_clip.alpha   = 1.0f;
    alpha_clip.cutoff  = 0.0f;
    alpha_clip.enabled = 0.0f;
    return alpha_clip;
}

float2 TransformDefaultLitTextureUV(float2 uv, float4 texture_ST)
{
    return uv * texture_ST.xy + texture_ST.zw;
}

void ClipDefaultLitSurface(DefaultLitAlphaClipData alpha_clip)
{
    if (alpha_clip.enabled > 0.5f)
    {
        clip(alpha_clip.alpha - alpha_clip.cutoff);
    }
}

float3 DefaultLitTangentNormalToWorld(float4 packed_normal, DefaultLitSurfaceInput input)
{
    float3 normal_TS = UnpackNormal(packed_normal);
    return normalize(
        normal_TS.x * input.tangent_WS +
        normal_TS.y * input.bitangent_WS +
        normal_TS.z * input.normal_WS);
}

GBufferData DefaultLitSurfaceToGBuffer(DefaultLitSurfaceData surface)
{
    GBufferData gbuffer;
    gbuffer.base_color       = surface.base_color;
    gbuffer.emissive         = surface.emissive;
    gbuffer.normal_WS        = surface.normal_WS;
    gbuffer.uv               = 0.0f;
    gbuffer.scene_depth      = 0.0f;
    gbuffer.roughness        = saturate(surface.roughness);
    gbuffer.metallic         = saturate(surface.metallic);
    gbuffer.specular         = saturate(surface.specular);
    gbuffer.material_AO      = saturate(surface.material_AO);
    gbuffer.shading_model_id = surface.shading_model_id;
    return gbuffer;
}

#endif
