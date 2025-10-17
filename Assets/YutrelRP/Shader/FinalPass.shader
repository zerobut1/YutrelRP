Shader "YutrelRP/FinalPass"
{
	SubShader
	{
		Cull Off
		ZTest Always
		ZWrite Off

		HLSLINCLUDE
		#include "Utils/Common.hlsl"

		TEXTURE2D(_BlitTexture);

		struct Varyings
		{
			float4 positionCS_SS : SV_POSITION;
			float2 screenUV : VAR_SCREEN_UV;
		};

		Varyings DefaultPassVertex(uint vertexID : SV_VertexID)
		{
			Varyings output;
			output.positionCS_SS = float4(
				vertexID <= 1 ? -1.0 : 3.0,
				vertexID == 1 ? 3.0 : -1.0,
				0.0,
				1.0
			);
			output.screenUV = float2(
				vertexID <= 1 ? 0.0 : 2.0,
				vertexID == 1 ? 2.0 : 0.0
			);
			if (_ProjectionParams.x < 0.0)
			{
				output.screenUV.y = 1.0 - output.screenUV.y;
			}
			return output;
		}

		float4 CopyPassFragment(Varyings input) : SV_TARGET
		{
			return SAMPLE_TEXTURE2D_LOD(
				_BlitTexture,
				sampler_linear_clamp,
				input.screenUV,
				0);
		}

		float CopyDepthPassFragment(Varyings input) : SV_DEPTH
		{
			return SAMPLE_DEPTH_TEXTURE_LOD(
				_BlitTexture,
				sampler_point_clamp,
				input.screenUV,
				0);
		}
		ENDHLSL

		Pass
		{
			Name "Copy"

			HLSLPROGRAM
			#pragma vertex DefaultPassVertex
			#pragma fragment CopyPassFragment
			ENDHLSL
		}

		Pass
		{
			Name "Copy Depth"

			ColorMask 0
			ZWrite On

			HLSLPROGRAM
			#pragma vertex DefaultPassVertex
			#pragma fragment CopyDepthPassFragment
			ENDHLSL
		}
	}
}