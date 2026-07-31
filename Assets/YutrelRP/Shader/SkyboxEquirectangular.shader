Shader "Hidden/YutrelRP/Skybox/Equirectangular"
{
	SubShader
	{
		Cull Off
		ZWrite Off
		ZTest Equal
		Blend Off

		Pass
		{
			HLSLPROGRAM
			#pragma target 5.0
			#pragma vertex SkyboxVertex
			#pragma fragment SkyboxFragment

			#include "Utils/Common.hlsl"

			TEXTURE2D(_EnvironmentSkybox);
			SAMPLER(sampler_EnvironmentSkybox);

			float _EnvironmentIntensity;
			float _EnvironmentSkyboxMultiplier;

			#define SKYBOX_INV_PI 0.31830988618379067154f
			#define SKYBOX_INV_TWO_PI 0.15915494309189533577f

			struct Attributes
			{
				uint vertex_ID : SV_VertexID;
			};

			struct Varyings
			{
				float4 position_CS : SV_POSITION;
				float2 uv : TEXCOORD0;
			};

			float2 DirectionToEquirectangularUV(float3 direction)
			{
				direction = normalize(direction);
				float u   = atan2(direction.x, direction.z) * SKYBOX_INV_TWO_PI + 0.5f;
				float v   = asin(clamp(direction.y, -1.0f, 1.0f)) * SKYBOX_INV_PI + 0.5f;
				return float2(u, v);
			}

			Varyings SkyboxVertex(Attributes input)
			{
				Varyings output;
				output.position_CS = GetFullScreenTriangleVertexPosition(input.vertex_ID, UNITY_RAW_FAR_CLIP_VALUE);
				output.uv = GetFullScreenTriangleTexCoord(input.vertex_ID);
				return output;
			}

			float4 SkyboxFragment(Varyings input) : SV_Target
			{
				float3 position_WS = ComputeWorldSpacePositionFromFullScreenUV(input.uv, UNITY_RAW_FAR_CLIP_VALUE);
				float3 direction_WS = -GetWorldSpaceViewDirectionForSurface(position_WS);
				float2 environment_uv = DirectionToEquirectangularUV(direction_WS);
				float3 color = SAMPLE_TEXTURE2D_LOD(
					_EnvironmentSkybox, sampler_EnvironmentSkybox, environment_uv, 0.0f).rgb;
				color *= _EnvironmentIntensity * _EnvironmentSkyboxMultiplier;
				return float4(ApplyPreExposure(color), 1.0f);
			}
			ENDHLSL
		}
	}
}
