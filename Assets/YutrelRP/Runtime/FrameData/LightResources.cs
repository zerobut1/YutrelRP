using System.Runtime.InteropServices;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace YutrelRP
{
    public class LightResources : ContextItem
    {
        private const string brdf_lut_resource_path = "Texture/brdf_lut";
        private static Texture2D brdf_lut_texture;
        private static RTHandle brdf_lut_rt_handle;

        public static readonly int
            brdf_lut_ID = Shader.PropertyToID("_BRDF_LUT"),
            directional_light_count_ID = Shader.PropertyToID("_DirectionalLightCount"),
            directional_light_data_ID = Shader.PropertyToID("_DirectionalLightData");

        public const int max_directional_light_count = 4;

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
            culling_results, ref ShadowResources shadow_resources)
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

            brdf_lut_texture ??= Resources.Load<Texture2D>(brdf_lut_resource_path);
            if (brdf_lut_texture == null)
            {
                Debug.LogError($"YutrelRP: Missing BRDF LUT resource at Resources/{brdf_lut_resource_path}.png");
                brdf_lut_texture = Texture2D.blackTexture;
            }

            brdf_lut_rt_handle ??= RTHandles.Alloc(brdf_lut_texture);
            BRDF_LUT = render_graph.ImportTexture(brdf_lut_rt_handle);
        }
    };
}
