Shader "YutrelRP/Skybox/Equirectangular"
{
	Properties
	{
		_Tex("HDR Equirectangular Map", 2D) = "grey" {}
		_Exposure("Exposure", Float) = 1.0
		_Rotation("Rotation", Range(0, 360)) = 0.0
	}

	SubShader
	{
		Tags
		{
			"Queue" = "Background"
			"RenderType" = "Background"
			"PreviewType" = "Skybox"
		}

		Cull Off
		ZWrite Off

		Pass
		{
			HLSLPROGRAM
			#pragma target 5.0
			#pragma vertex SkyboxVertex
			#pragma fragment SkyboxFragment

			#include "Utils/Common.hlsl"

			TEXTURE2D(_Tex);
			SAMPLER(sampler_Tex);

			float _Exposure;
			float _Rotation;

			#define SKYBOX_PI 3.14159265358979323846f
			#define SKYBOX_INV_PI 0.31830988618379067154f
			#define SKYBOX_INV_TWO_PI 0.15915494309189533577f

			struct Attributes
			{
				float3 position_OS : POSITION;
			};

			struct Varyings
			{
				float4 position_CS : SV_POSITION;
				float3 direction_WS : TEXCOORD0;
			};

			float3 RotateDirectionY(float3 direction, float degrees)
			{
				float angle = degrees * (SKYBOX_PI / 180.0f);
				float s;
				float c;
				sincos(angle, s, c);
				return float3(c * direction.x - s * direction.z, direction.y, s * direction.x + c * direction.z);
			}

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
				output.position_CS  = TransformObjectToHClip(input.position_OS);
				output.direction_WS = input.position_OS;
				return output;
			}

			float4 SkyboxFragment(Varyings input) : SV_Target
			{
				float3 direction = RotateDirectionY(input.direction_WS, _Rotation);
				float2 uv        = DirectionToEquirectangularUV(direction);
				float3 color     = SAMPLE_TEXTURE2D(_Tex, sampler_Tex, uv).rgb * _Exposure;
				return float4(color, 1.0f);
			}
			ENDHLSL
		}
	}
}
