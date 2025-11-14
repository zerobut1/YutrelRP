using System.Runtime.InteropServices;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace YutrelRP
{
    public class LightResources : ContextItem
    {
        public static readonly int
            brdf_lut_Id = Shader.PropertyToID("_BRDF_LUT"),
            directional_light_count_Id = Shader.PropertyToID("_DirectionalLightCount"),
            directional_light_data_Id = Shader.PropertyToID("_DirectionalLightData");

        public const int max_directional_light_count = 1;

        [StructLayout(LayoutKind.Sequential)]
        public struct DirectionalLightData
        {
            public const int stride = 4 * 4 * 3;

            public Vector3 color;
            public float intensity;
            public Vector4 direction;
            public Vector4 shadow_data; // x: shadow index

            public DirectionalLightData(VisibleLight visiable_light, Vector4 shadow_data)
            {
                color.x = visiable_light.light.color.r;
                color.y = visiable_light.light.color.g;
                color.z = visiable_light.light.color.b;
                intensity = visiable_light.light.intensity;
                direction = -visiable_light.localToWorldMatrix.GetColumn(2);
                this.shadow_data = shadow_data;
            }
        }

        public int directional_light_count;

        public readonly DirectionalLightData[] directional_light_data =
            new DirectionalLightData[max_directional_light_count];

        public BufferHandle directional_light_data_buffer;
        public TextureHandle BRDF_LUT;

        public override void Reset()
        {
            directional_light_count = 0;
            directional_light_data_buffer = BufferHandle.nullHandle;
            BRDF_LUT = TextureHandle.nullHandle;
        }

        public void Setup(RenderGraph render_graph, IComputeRenderGraphBuilder builder, CullingResults
            culling_results, Texture2D _BRDF_LUT, ref ShadowResources shadow_resources)
        {
            NativeArray<VisibleLight> visible_lights = culling_results.visibleLights;

            directional_light_count = 0;
            for (int i = 0; i < visible_lights.Length; i++)
            {
                VisibleLight visible_light = visible_lights[i];
                switch (visible_light.lightType)
                {
                    case LightType.Directional:
                        if (directional_light_count < LightResources.max_directional_light_count)
                        {
                            directional_light_data[directional_light_count++] =
                                new DirectionalLightData(visible_light,
                                    shadow_resources.ReserveDirectionalShadows(visible_light.light, i,
                                        culling_results));
                        }

                        break;
                }
            }

            directional_light_data_buffer = render_graph.CreateBuffer(
                new BufferDesc(max_directional_light_count, DirectionalLightData.stride)
                {
                    name = "Directional Light Data"
                });
            builder.UseBuffer(directional_light_data_buffer, AccessFlags.WriteAll);

            BRDF_LUT = render_graph.ImportTexture(RTHandles.Alloc(_BRDF_LUT));
            builder.UseTexture(BRDF_LUT);
        }

        public void Render(ComputeCommandBuffer cmd)
        {
            cmd.SetGlobalInt(directional_light_count_Id, directional_light_count);
            cmd.SetBufferData(directional_light_data_buffer, directional_light_data, 0, 0, directional_light_count);
            cmd.SetGlobalBuffer(directional_light_data_Id, directional_light_data_buffer);
            cmd.SetGlobalTexture(brdf_lut_Id, BRDF_LUT);
        }
    };
}