#ifndef YUTREL_FORWARD_LIGHTING_INCLUDED
#define YUTREL_FORWARD_LIGHTING_INCLUDED

#include "../Utils/Light.hlsl"
#include "../Utils/ShadowSampling.hlsl"

int _DirectionalLightCount;

// Surface-local receiver query; no screen depth, GBuffer or ShadowMask dependency.
float EvaluateDirectionalShadowVisibility(int lightIndex, float3 positionWS, float3 geometricNormalWS)
{
    if (lightIndex < 0 || lightIndex >= _DirectionalLightCount || _DirectionalShadowCascadeCount <= 0)
    {
        return 1.0f;
    }
    return EvaluateCsmShadowVisibility(GetDirectionalLightShadowData(lightIndex), positionWS, geometricNormalWS);
}

#endif
