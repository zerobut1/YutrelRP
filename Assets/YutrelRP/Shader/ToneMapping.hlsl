#ifndef YUTREL_TONEMAPPING_INCLUDED
#define YUTREL_TONEMAPPING_INCLUDED

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"

TEXTURE2D(_SourceColor);

float4 GetSource(float2 screenUV)
{
    return SAMPLE_TEXTURE2D_LOD(_SourceColor, sampler_linear_clamp, screenUV, 0);
}

float4 CopyPassFragment(FullScreenVaryings input) : SV_Target
{
    return GetSource(input.uv);
}

float4 ToneMappingACESFragment(FullScreenVaryings input) : SV_Target
{
    float4 color = GetSource(input.uv);
    color.rgb = AcesTonemap(unity_to_ACES(color.rgb));

    return color;
}

#endif
