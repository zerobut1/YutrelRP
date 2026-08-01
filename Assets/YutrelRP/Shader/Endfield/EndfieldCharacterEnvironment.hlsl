#ifndef YUTREL_ENDFIELD_CHARACTER_ENVIRONMENT_INCLUDED
#define YUTREL_ENDFIELD_CHARACTER_ENVIRONMENT_INCLUDED

#include "../DDGI/DDGILighting.hlsl"
#include "../EnvironmentLighting.hlsl"

#define ENDFIELD_ENVIRONMENT_NONE 0
#define ENDFIELD_ENVIRONMENT_SH 1
#define ENDFIELD_ENVIRONMENT_DDGI 2

int _DirectionalLightCount;
int _EnvironmentDiffuseMode;
int _EnvironmentSpecularEnabled;

EnvironmentLightingResult EndfieldCharacterEvaluateEnvironment(StandardSurface surface)
{
    float3 diffuse_lighting = 0.0f;
    if (_EnvironmentDiffuseMode == ENDFIELD_ENVIRONMENT_DDGI)
    {
        diffuse_lighting = EvaluateDDGIDiffuseLighting(surface);
    }
    else if (_EnvironmentDiffuseMode == ENDFIELD_ENVIRONMENT_SH)
    {
        diffuse_lighting = EvaluateEnvironmentDiffuseSH(surface.normal_WS) * _EnvironmentIntensity *
                           _EnvironmentDiffuseMultiplier;
    }

    EnvironmentLightingResult environment = EvaluateEnvironmentLighting(
        surface,
        diffuse_lighting,
        surface.material_AO,
        _EnvironmentSpecularEnabled != 0);

    // Environment resources use physical luminance units. Endfield direct lighting is already
    // normalized by _EndfieldReferenceIlluminance, so only the environment terms need pre-exposure.
    environment.diffuse  = ApplyPreExposure(environment.diffuse);
    environment.specular = ApplyPreExposure(environment.specular);
    return environment;
}

#endif
