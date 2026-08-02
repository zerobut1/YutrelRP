Shader "Hidden/YutrelRP/NoDraw"
{
	SubShader
	{
		Tags
		{
			"RenderPipeline" = "YutrelPipeline"
		}

		Pass
		{
			Name "NoDraw"
			Tags
			{
				"LightMode" = "YutrelRPNoDraw"
			}

			Cull Off
			ZTest Always
			ZWrite Off
			ColorMask 0

			HLSLPROGRAM
			#pragma target 5.0
			#pragma vertex NoDrawVertex
			#pragma fragment NoDrawFragment

			float4 NoDrawVertex(float3 position_OS : POSITION) : SV_POSITION
			{
				return float4(position_OS, 1.0);
			}

			float4 NoDrawFragment() : SV_Target
			{
				return 0.0;
			}
			ENDHLSL
		}
	}

	FallBack Off
}
