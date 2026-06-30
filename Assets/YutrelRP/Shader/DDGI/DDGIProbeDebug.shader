Shader "YutrelRP/DDGI/Probe Debug"
{
	SubShader
	{
		HLSLINCLUDE
		#include "../Utils/Common.hlsl"
		#include "DDGICommon.hlsl"

		Texture2DArray<float4> _DDGIProbeIrradiance;
		Texture2DArray<float2> _DDGIProbeDistance;
		Texture2DArray<float2> _DDGIProbeRayData;
		Texture2D<float> _SceneDepth;

		int _DDGIProbeDebugMode;
		float _DDGIProbeDebugRadius;
		float _DDGIProbeDebugDistanceScale;
		float3 _DDGIProbeBoundsMin;
		float3 _DDGIProbeSpacing;
		float3 _DDGIProbeCount;
		float4 _DDGIProbeIrradianceDimensions;
		float4 _DDGIProbeDistanceDimensions;
		float4 _DDGIProbeRayDataDimensions;
		float _DDGIIrradianceEncodingGamma;

		struct ProbeDebugAttributes
		{
			float3 position_OS : POSITION;
			uint instance_id : SV_InstanceID;
		};

		struct ProbeDebugVaryings
		{
			float4 position_CS : SV_POSITION;
			float3 position_WS : TEXCOORD0;
			float3 probe_center_WS : TEXCOORD1;
			nointerpolation int probe_index : TEXCOORD2;
		};

		int3 DDGIProbeDebugGetProbeCoords(int probeIndex, int3 probeCount)
		{
			return int3(
				probeIndex % probeCount.x,
				probeIndex / (probeCount.x * probeCount.z),
				(probeIndex / probeCount.x) % probeCount.z);
		}

		float3 DDGIProbeDebugGetProbePosition(int3 probeCoords)
		{
			return _DDGIProbeBoundsMin + _DDGIProbeSpacing * (float3)probeCoords;
		}

		float3 DDGIProbeDebugGetProbeUV(int probeIndex, float2 octantCoordinates, int interiorTexels, int3 probeCount)
		{
			int3 probeCoords = DDGIProbeDebugGetProbeCoords(probeIndex, probeCount);
			float tile       = (float)interiorTexels + 2.0f;
			float2 size      = float2(probeCount.x, probeCount.z) * tile;
			float2 uv        = float2(probeCoords.x, probeCoords.z) * tile + tile * 0.5f;
			uv += octantCoordinates * ((float)interiorTexels * 0.5f);
			return float3(uv / size, probeCoords.y);
		}

		float3 DDGIProbeDebugDecodeIrradiance(float3 value)
		{
			float3 color = pow(max(value, 0.0f), _DDGIIrradianceEncodingGamma * 0.5f);
			return color * color * 2.0f * 1.0989f;
		}

		float DDGIProbeDebugDecodeDistance(float2 value)
		{
			return 2.0f * value.r;
		}

		float3 DDGIProbeDebugUnpackRadiance01(uint packedRadiance)
		{
			return DDGIUnpackRadiance01(packedRadiance);
		}

		float4 DDGIProbeDebugOutput(float3 color, float alpha)
		{
			return float4(color, alpha);
		}

		bool DDGIProbeDebugPassesSceneDepth(float4 positionCS)
		{
			int2 pixel        = (int2)positionCS.xy;
			float sceneDepth = _SceneDepth.Load(int3(pixel, 0));
			float probeDepth = positionCS.z;
#if UNITY_REVERSED_Z
			return probeDepth >= sceneDepth - 1.0e-6f;
#else
			return probeDepth <= sceneDepth + 1.0e-6f;
#endif
		}

		ProbeDebugVaryings ProbeDebugSphereVertex(ProbeDebugAttributes input)
		{
			ProbeDebugVaryings output;
			int3 probeCount      = (int3)_DDGIProbeCount;
			int probeIndex       = (int)input.instance_id;
			int3 probeCoords     = DDGIProbeDebugGetProbeCoords(probeIndex, probeCount);
			float3 probeCenterWS = DDGIProbeDebugGetProbePosition(probeCoords);
			float3 positionWS    = probeCenterWS + input.position_OS * _DDGIProbeDebugRadius;

			output.position_CS     = mul(UNITY_MATRIX_VP, float4(positionWS, 1.0f));
			output.position_WS     = positionWS;
			output.probe_center_WS = probeCenterWS;
			output.probe_index     = probeIndex;
			return output;
		}

		float4 ProbeDebugSphereFragment(ProbeDebugVaryings input) : SV_Target
		{
			if (!DDGIProbeDebugPassesSceneDepth(input.position_CS))
			{
				discard;
			}

			int3 probeCount   = (int3)_DDGIProbeCount;
			float3 direction = normalize(input.position_WS - input.probe_center_WS);
			float2 octantUV  = DDGIOctEncode(direction);

			if (_DDGIProbeDebugMode == 1)
			{
				float3 uv    = DDGIProbeDebugGetProbeUV(input.probe_index, octantUV, 6, probeCount);
				float3 value = _DDGIProbeIrradiance.SampleLevel(sampler_linear_clamp, uv, 0).rgb;
				return DDGIProbeDebugOutput(saturate(DDGIProbeDebugDecodeIrradiance(value)), 1.0f);
			}

			if (_DDGIProbeDebugMode == 2)
			{
				float3 uv      = DDGIProbeDebugGetProbeUV(input.probe_index, octantUV, 14, probeCount);
				float distance = DDGIProbeDebugDecodeDistance(_DDGIProbeDistance.SampleLevel(sampler_linear_clamp, uv, 0));
				float value    = saturate(distance / max(_DDGIProbeDebugDistanceScale, 1.0e-6f));
				return DDGIProbeDebugOutput(value.xxx, 1.0f);
			}

			return DDGIProbeDebugOutput(float3(1.0f, 0.0f, 1.0f), 1.0f);
		}

		bool DDGIProbeDebugGetOverlayCoords(float2 uv, float4 dimensions, out int3 coords)
		{
			float2 screenSize = _ScreenParams.xy;
			float2 pixel     = float2(uv.x, 1.0f - uv.y) * screenSize;
			float2 local     = pixel - float2(8.0f, 8.0f);
			float width      = dimensions.x;
			float height     = dimensions.y;
			float slices     = dimensions.z;

			coords = 0;
			if (local.x < 0.0f || local.y < 0.0f || local.x >= width * slices || local.y >= height)
			{
				return false;
			}

			int slice = (int)(local.x / width);
			coords = int3(
				(int)(local.x - (float)slice * width),
				(int)local.y,
				slice);
			return true;
		}

		float4 ProbeDebugOverlayFragment(FullScreenVaryings input) : SV_Target
		{
			int3 coords;
			if (_DDGIProbeDebugMode == 3)
			{
				if (!DDGIProbeDebugGetOverlayCoords(input.uv, _DDGIProbeIrradianceDimensions, coords))
				{
					return float4(0.0f, 0.0f, 0.0f, 0.0f);
				}

				float3 value = _DDGIProbeIrradiance.Load(int4(coords, 0)).rgb;
				return DDGIProbeDebugOutput(saturate(DDGIProbeDebugDecodeIrradiance(value)), 1.0f);
			}

			if (_DDGIProbeDebugMode == 4)
			{
				if (!DDGIProbeDebugGetOverlayCoords(input.uv, _DDGIProbeDistanceDimensions, coords))
				{
					return float4(0.0f, 0.0f, 0.0f, 0.0f);
				}

				float distance = DDGIProbeDebugDecodeDistance(_DDGIProbeDistance.Load(int4(coords, 0)));
				float value    = saturate(distance / max(_DDGIProbeDebugDistanceScale, 1.0e-6f));
				return DDGIProbeDebugOutput(value.xxx, 1.0f);
			}

			if (_DDGIProbeDebugMode == 5)
			{
				if (!DDGIProbeDebugGetOverlayCoords(input.uv, _DDGIProbeRayDataDimensions, coords))
				{
					return float4(0.0f, 0.0f, 0.0f, 0.0f);
				}

				float2 rayData = _DDGIProbeRayData.Load(int4(coords, 0));
				float3 color   = DDGIProbeDebugUnpackRadiance01(asuint(rayData.x));
				return DDGIProbeDebugOutput(color, 1.0f);
			}

			return float4(0.0f, 0.0f, 0.0f, 0.0f);
		}
		ENDHLSL

		Pass
		{
			Name "Probe Sphere"
			Cull Back
			ZTest LEqual
			ZWrite On

			HLSLPROGRAM
			#pragma target 5.0
			#pragma vertex ProbeDebugSphereVertex
			#pragma fragment ProbeDebugSphereFragment
			ENDHLSL
		}

		Pass
		{
			Name "Atlas Overlay"
			Cull Off
			ZTest Always
			ZWrite Off
			Blend SrcAlpha OneMinusSrcAlpha

			HLSLPROGRAM
			#pragma target 5.0
			#pragma vertex DefaultFullScreenPassVertex
			#pragma fragment ProbeDebugOverlayFragment
			ENDHLSL
		}
	}
}
