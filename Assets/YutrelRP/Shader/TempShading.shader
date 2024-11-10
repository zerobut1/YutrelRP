Shader "YutrelRP/TempShading"
{
    Properties {}
    SubShader
    {

        Pass
        {
            Name "TempShading"

            ZWrite Off
            ZTest Always
            Blend One One
            Cull Off

            HLSLPROGRAM
            #pragma enable_d3d11_debug_symbols
            #pragma vertex vert
            #pragma fragment frag
            #include "Utils/Transformation.hlsl"

            struct a2v
            {
                float4 vertex : POSITION;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };

            CBUFFER_START(_light_buffer)
                float4 _sun_light_direction;
                float4 _sun_light_color;
            CBUFFER_END

            sampler2D _GBuffer_A;
            sampler2D _GBuffer_B;
            sampler2D _GBuffer_C;
            sampler2D _SceneDepth;

            v2f vert(a2v _in)
            {
                v2f _out;
                _out.vertex = _in.vertex;
                _out.uv = _in.vertex * float2(0.5, -0.5) + 0.5;

                return _out;
            }

            float4 frag(v2f _in) : SV_Target
            {
                float4 GBuffer_A = tex2D(_GBuffer_A, _in.uv);
                float4 GBuffer_B = tex2D(_GBuffer_B, _in.uv);
                float4 GBuffer_C = tex2D(_GBuffer_C, _in.uv);
                float device_z = tex2D(_SceneDepth, _in.uv).r;

                // float4 position_ndc = float4(_in.uv * 2.0 - 1.0, device_z * 2.0 - 1.0, 1.0);
                float4 position_ndc = float4(_in.uv, device_z, 1.0);

                float3 albedo = GBuffer_A.rgb;
                float3 normal_ws = GBuffer_B.rgb;

                float3 light_dir_ws = normalize(_sun_light_direction);
                // float3 world_position = ComputeWorldSpacePosition(_in.uv, device_z, UNITY_MATRIX_I_VP);
                // float4 world_position = mul(UNITY_MATRIX_I_VP, position_ndc);
                // world_position /= world_position.w;
                float3 world_position = GBuffer_C.xyz;
                float3 view_dir_ws = normalize(_WorldSpaceCameraPos - world_position);

                float3 h = normalize(light_dir_ws + view_dir_ws);

                float spec = pow(max(dot(normal_ws, h), 0.0), 8.0);
                float3 specular = _sun_light_color * spec;

                float diff = max(dot(normal_ws, light_dir_ws), 0.0);
                float3 diffuse = _sun_light_color * diff;

                float ambient = 0.1f;

                float3 out_color = (specular + diffuse + ambient) * albedo;
                // out_color = normal_ws;
                // out_color = light_dir_ws;
                // out_color = world_position;
                // out_color = view_dir_ws;

                return float4(out_color, 1.0f);
            }
            ENDHLSL
        }
    }
}