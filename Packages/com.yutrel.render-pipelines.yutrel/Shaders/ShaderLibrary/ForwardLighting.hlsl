#ifndef YUTREL_FORWARD_LIGHTING_INCLUDED
#define YUTREL_FORWARD_LIGHTING_INCLUDED

#include "../Utils/ShadingModelStandard.hlsl"
#include "../DDGI/DDGILighting.hlsl"
#include "../EnvironmentLighting.hlsl"

#define YUTREL_ENVIRONMENT_DIFFUSE_NONE 0
#define YUTREL_ENVIRONMENT_DIFFUSE_SH 1
#define YUTREL_ENVIRONMENT_DIFFUSE_DDGI 2

int _DirectionalLightCount;
int _EnvironmentDiffuseMode;
int _EnvironmentSpecularEnabled;

#endif
