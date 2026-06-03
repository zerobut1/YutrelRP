Shader "YutrelRP/DDGIProbeDebug"
{
	SubShader
	{
		Tags { "RenderType" = "Opaque" }
		Cull Back
		ZTest LEqual
		ZWrite Off
		Blend SrcAlpha OneMinusSrcAlpha

		HLSLINCLUDE
		#include "Utils/Common.hlsl"
		#include "DDGI/DDGI.hlsl"

		TEXTURE2D_ARRAY(_DDGIProbeRayData);
		SAMPLER(sampler_DDGIProbeRayData);
		TEXTURE2D_ARRAY(_DDGIProbeIrradiance);
		SAMPLER(sampler_DDGIProbeIrradiance);
		TEXTURE2D_ARRAY(_DDGIProbeDistance);
		SAMPLER(sampler_DDGIProbeDistance);

		int _DDGIProbeDebugMode;
		int _DDGIProbeDebugIssue;
		float4 _DDGIProbeCount;
		float _DDGIProbeDebugRadius;
		float4 _DDGIProbeRayDataDimensions;
		float _DDGIProbeRayDataMaxDistance;
		float4 _DDGIProbeIrradianceDimensions;
		float4 _DDGIProbeDistanceDimensions;
		float3 _DDGIVolumeMinWS;
		float3 _DDGIProbeSpacingWS;

		struct Attributes
		{
			float3 position_OS : POSITION;
			float3 normal_OS : NORMAL;
			uint instance_id : SV_InstanceID;
		};

		struct Varyings
		{
			float4 position_CS : SV_POSITION;
			float3 normal_WS : TEXCOORD0;
			nointerpolation uint3 probe_coord : TEXCOORD1;
		};

		uint3 DDGIProbeDebugCount()
		{
			return uint3((uint)max(_DDGIProbeCount.x, 1.0f), (uint)max(_DDGIProbeCount.y, 1.0f), (uint)max(_DDGIProbeCount.z, 1.0f));
		}

		uint3 DDGIProbeDebugCoord(uint instance_id)
		{
			uint3 probe_count = DDGIProbeDebugCount();
			uint plane_count = max(probe_count.x * probe_count.z, 1u);
			uint probe_y = min(instance_id / plane_count, probe_count.y - 1u);
			uint plane_index = instance_id - probe_y * plane_count;
			return DDGIProbeCoordFromPlaneIndex(plane_index, probe_y, probe_count);
		}

		float3 DDGIProbeDebugToneMapRadiance(float3 radiance)
		{
			float3 exposed_radiance = ApplyPreExposure(max(radiance, 0.0f));
			return saturate(1.0f - exp(-exposed_radiance * 0.5f));
		}

		float3 DDGIProbeDebugInvalidColor()
		{
			return float3(1.0f, 0.1f, 0.0f);
		}

		float DDGIProbeDebugMinProbeSpacing()
		{
			float3 spacing = abs(_DDGIProbeSpacingWS);
			return max(min(spacing.x, min(spacing.y, spacing.z)), 0.001f);
		}

		bool DDGIProbeDebugValidFinite(float3 value)
		{
			return all(value == value) && all(abs(value) < 1.0e20f.xxx);
		}

		bool DDGIProbeDebugValidFinite4(float4 value)
		{
			return all(value == value) && all(abs(value) < 1.0e20f.xxxx);
		}

		float3 DDGIProbeDebugSampleIrradiance(uint3 probe_coord, float3 direction_WS)
		{
			uint interior_texels = (uint)max(_DDGIProbeIrradianceDimensions.w - 2.0f, 1.0f);
			float3 radiance = DDGISampleProbeIrradiance(_DDGIProbeIrradiance, sampler_DDGIProbeIrradiance, probe_coord, direction_WS, interior_texels, _DDGIProbeIrradianceDimensions);
			return DDGIProbeDebugValidFinite(radiance) ? DDGIProbeDebugToneMapRadiance(radiance) : DDGIProbeDebugInvalidColor();
		}

		float3 DDGIProbeDebugSampleDistance(uint3 probe_coord, float3 direction_WS)
		{
			uint interior_texels = (uint)max(_DDGIProbeDistanceDimensions.w - 2.0f, 1.0f);
			float3 moments = DDGISampleProbeDistance(_DDGIProbeDistance, sampler_DDGIProbeDistance, probe_coord, direction_WS, interior_texels, _DDGIProbeDistanceDimensions);
			if (!DDGIProbeDebugValidFinite(moments))
			{
				return DDGIProbeDebugInvalidColor();
			}

			float mean_distance = saturate(moments.r);
			float mean_distance_sq = saturate(moments.g);
			float hit_ratio = saturate(moments.b);
			float max_ray_distance = max(_DDGIProbeRayDataMaxDistance, 0.001f);
			float mean_distance_WS = mean_distance * max_ray_distance;
			float mean_distance_sq_WS = mean_distance_sq * max_ray_distance * max_ray_distance;
			float variance_WS = max(mean_distance_sq_WS - mean_distance_WS * mean_distance_WS, 0.0f);

			float useful_distance = DDGIProbeDebugMinProbeSpacing() * 2.0f;
			float relative_distance = saturate(mean_distance_WS / useful_distance);
			float3 close_color = float3(1.0f, 0.16f, 0.04f);
			float3 useful_color = float3(0.12f, 0.85f, 0.24f);
			float3 far_color = float3(0.18f, 0.42f, 1.0f);
			float3 distance_color = relative_distance < 0.5f
			                            ? lerp(close_color, useful_color, relative_distance * 2.0f)
			                            : lerp(useful_color, far_color, (relative_distance - 0.5f) * 2.0f);

			float variance_ratio = saturate(sqrt(variance_WS) / max(mean_distance_WS, DDGIProbeDebugMinProbeSpacing() * 0.25f));
			distance_color = lerp(distance_color, float3(1.0f, 0.08f, 0.9f), variance_ratio * 0.35f);

			float3 miss_color = float3(0.02f, 0.02f, 0.02f);
			return lerp(miss_color, distance_color, hit_ratio);
		}

		float3 DDGIProbeDebugRayQualityColor(uint3 probe_coord)
		{
			uint rays_per_probe = (uint)max(_DDGIProbeRayDataDimensions.x, 1.0f);
			uint3 probe_count = DDGIProbeDebugCount();
			uint frontface_count = 0u;
			uint backface_count = 0u;
			uint miss_count = 0u;
			uint invalid_count = 0u;

			for (uint ray_index = 0u; ray_index < rays_per_probe; ray_index++)
			{
				uint3 ray_texel = DDGIProbeRayDataTexel(ray_index, probe_coord, probe_count);
				float4 ray_data = LOAD_TEXTURE2D_ARRAY(_DDGIProbeRayData, ray_texel.xy, ray_texel.z);
				if (!DDGIProbeDebugValidFinite4(ray_data))
				{
					invalid_count++;
				}
				else if (DDGIProbeRayDataIsMiss(ray_data.a, _DDGIProbeRayDataMaxDistance))
				{
					miss_count++;
				}
				else if (DDGIProbeRayDataIsBackface(ray_data.a, _DDGIProbeRayDataMaxDistance))
				{
					backface_count++;
				}
				else if (DDGIProbeRayDataIsFrontface(ray_data.a))
				{
					frontface_count++;
				}
				else
				{
					invalid_count++;
				}
			}

			float ray_count = max((float)rays_per_probe, 1.0f);
			float miss_ratio = (float)miss_count / ray_count;
			float backface_ratio = (float)backface_count / ray_count;
			float frontface_ratio = (float)frontface_count / ray_count;
			float invalid_ratio = (float)invalid_count / ray_count;

			if (invalid_ratio > 0.0f || frontface_count + backface_count + miss_count == 0u)
			{
				return float3(1.0f, 0.1f, 0.0f);
			}
			if (backface_ratio >= 0.25f)
			{
				return lerp(float3(0.45f, 0.05f, 0.55f), float3(1.0f, 0.12f, 0.88f), saturate(backface_ratio));
			}
			if (miss_ratio >= 0.35f)
			{
				return lerp(float3(0.03f, 0.03f, 0.03f), float3(0.55f, 0.55f, 0.55f), saturate(1.0f - miss_ratio));
			}

			return lerp(float3(0.05f, 0.25f, 0.08f), float3(0.1f, 1.0f, 0.28f), saturate(frontface_ratio));
		}

		Varyings DDGIProbeDebugVertex(Attributes input)
		{
			Varyings output;
			uint3 probe_coord = DDGIProbeDebugCoord(input.instance_id);
			float3 probe_position_WS = DDGIProbeWorldPosition(_DDGIVolumeMinWS, _DDGIProbeSpacingWS, probe_coord);
			float3 normal_WS = normalize(input.normal_OS);
			float3 position_WS = probe_position_WS + input.position_OS * max(_DDGIProbeDebugRadius, 0.001f);

			output.position_CS = TransformWorldToHClip(position_WS);
			output.normal_WS = normal_WS;
			output.probe_coord = probe_coord;
			return output;
		}

		float4 DDGIProbeDebugFragment(Varyings input) : SV_Target
		{
			if (_DDGIProbeDebugIssue != 0)
			{
				return float4(1.0f, 0.0f, 1.0f, 0.85f);
			}

			float3 direction_WS = DDGISafeNormalize(input.normal_WS, float3(0.0f, 1.0f, 0.0f));
			float3 color = float3(0.0f, 0.0f, 0.0f);
			if (_DDGIProbeDebugMode == 1)
			{
				color = DDGIProbeDebugSampleIrradiance(input.probe_coord, direction_WS);
			}
			else if (_DDGIProbeDebugMode == 2)
			{
				color = DDGIProbeDebugRayQualityColor(input.probe_coord);
			}
			else if (_DDGIProbeDebugMode == 3)
			{
				color = DDGIProbeDebugSampleDistance(input.probe_coord, direction_WS);
			}

			return float4(saturate(color), 0.92f);
		}

		float4 DDGIProbeDebugIssueFragment(FullScreenVaryings input) : SV_Target
		{
			return float4(1.0f, 0.0f, 1.0f, 0.32f);
		}
		ENDHLSL

		Pass
		{
			HLSLPROGRAM
			#pragma target 5.0
			#pragma vertex DDGIProbeDebugVertex
			#pragma fragment DDGIProbeDebugFragment
			ENDHLSL
		}

		Pass
		{
			ZTest Always
			HLSLPROGRAM
			#pragma target 5.0
			#pragma vertex DefaultFullScreenPassVertex
			#pragma fragment DDGIProbeDebugIssueFragment
			ENDHLSL
		}
	}
}
