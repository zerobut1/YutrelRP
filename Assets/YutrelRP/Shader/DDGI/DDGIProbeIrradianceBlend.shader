Shader "YutrelRP/DDGIProbeIrradianceBlend"
{
	SubShader
	{
		HLSLINCLUDE
		#include "Assets/YutrelRP/Shader/Utils/Common.hlsl"
		#include "Assets/YutrelRP/Shader/DDGI/DDGIProbeBlendCommon.hlsl"

		Texture2DArray<float4> _DDGIProbeIrradianceHistory;
		int _DDGIProbeBlendSlice;

		float4 Fragment(FullScreenVaryings input) : SV_Target
		{
			uint3 dispatch_id = uint3((uint2)max(input.position_CS.xy, 0.0f.xx), (uint)max(_DDGIProbeBlendSlice, 0));
			uint3 probe_coord;
			uint2 local_texel;
			uint interior_texels;
			if (!DDGIProbeBlendAtlasTexel(dispatch_id, _DDGIProbeIrradianceDimensions, probe_coord, local_texel, interior_texels))
			{
				return float4(0.0f, 0.0f, 0.0f, 1.0f);
			}

			uint3 history_texel = DDGIAtlasLocalTexelIsBorder(local_texel, interior_texels)
									  ? DDGIProbeBlendWrappedTexel(probe_coord, local_texel, interior_texels)
									  : dispatch_id;
			float3 irradiance         = DDGIProbeBlendIrradiance(probe_coord, local_texel, interior_texels);
			float3 encoded_irradiance = DDGIEncodeProbeIrradiance(irradiance);
			float4 current            = DDGIProbeIrradianceStoreValue(encoded_irradiance);
			float4 history            = _DDGIProbeIrradianceHistory.Load(uint4(history_texel, 0u));
			return DDGIProbeBlendIrradianceHistory(history, current);
		}
		ENDHLSL

		Pass
		{
			ZWrite Off
			ZTest Always
			Blend Off
			Cull Off

			HLSLPROGRAM
			#pragma target 5.0
			#pragma vertex DefaultFullScreenPassVertex
			#pragma fragment Fragment
			ENDHLSL
		}
	}
}
