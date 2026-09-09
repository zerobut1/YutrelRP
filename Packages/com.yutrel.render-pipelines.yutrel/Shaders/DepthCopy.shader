Shader "Hidden/YutrelRP/Depth Copy"
{
    SubShader
    {
        Tags { "RenderPipeline" = "YutrelPipeline" }
        Pass
        {
            Cull Off
            ZTest Always
            ZWrite Off
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex DepthCopyVertex
            #pragma fragment DepthCopyFragment
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"

            Texture2D<float> _DepthCopySource;
            float4 DepthCopyVertex(uint vertexID : SV_VertexID) : SV_Position
            {
                return GetFullScreenTriangleVertexPosition(vertexID);
            }
            float DepthCopyFragment(float4 positionCS : SV_Position) : SV_Target0
            {
                // 1:1 device depth copy; no UV flip, filtering, linearization or stencil.
                return _DepthCopySource.Load(int3(int2(positionCS.xy), 0));
            }
            ENDHLSL
        }
    }
}
