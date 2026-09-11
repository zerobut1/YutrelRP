#ifndef CUSTOM_UNITY_INPUT_INCLUDED
#define CUSTOM_UNITY_INPUT_INCLUDED

CBUFFER_START(UnityPerDraw)
float4x4 unity_ObjectToWorld;
float4x4 unity_WorldToObject;
float4 unity_LODFade;
real4 unity_WorldTransformParams;

float4 unity_RenderingLayer;

float4 unity_ProbesOcclusion;

float4 unity_SpecCube0_HDR;

float4 unity_LightmapST;
float4 unity_DynamicLightmapST;

float4 unity_SHAr;
float4 unity_SHAg;
float4 unity_SHAb;
float4 unity_SHBr;
float4 unity_SHBg;
float4 unity_SHBb;
float4 unity_SHC;

float4 unity_ProbeVolumeParams;
float4x4 unity_ProbeVolumeWorldToObject;
float4 unity_ProbeVolumeSizeInv;
float4 unity_ProbeVolumeMin;
CBUFFER_END

float4x4 unity_MatrixVP;
float4x4 unity_MatrixV;
float4x4 unity_MatrixInvV;
float4x4 unity_MatrixInvVP;
float4x4 unity_prev_MatrixM;
float4x4 unity_prev_MatrixIM;
float4x4 glstate_matrix_projection;

float3 _WorldSpaceCameraPos;

float4 unity_OrthoParams;
float4 _ProjectionParams;
float4 _ScreenParams;
float4 _CameraBufferSize;
float4 _ZBufferParams;

// 每帧时间。SRP 不会像内置管线那样自动填 _Time，这里由 Runtime/RenderPass/SetupPass.cs
// 每帧写一次，数值约定与 Unity 内置保持一致：
//   _Time = (t/20, t, t*2, t*3)，_SinTime/_CosTime 取 t/8、t/4、t/2、t 三个频率，
//   其中 t = Time.timeSinceLevelLoad。
float4 _Time;
float4 _SinTime;
float4 _CosTime;

#endif
