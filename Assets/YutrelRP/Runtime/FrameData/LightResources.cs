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
        private static bool brdf_lut_missing_reported;
        private static Texture environment_reflection_texture;
        private static Cubemap black_environment_reflection;
        private static RTHandle environment_reflection_rt_handle;

        public static readonly int
            brdf_lut_ID = Shader.PropertyToID("_BRDF_LUT"),
            environment_reflection_cube_ID = Shader.PropertyToID("_EnvironmentReflectionCube"),
            environment_reflection_cube_hdr_ID = Shader.PropertyToID("_EnvironmentReflectionCube_HDR"),
            environment_reflection_available_ID = Shader.PropertyToID("_EnvironmentReflectionAvailable"),
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
            public Vector4 shadow_data; // x: shadow index, y: 1 when Unity light uses soft shadows

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
        public bool has_BRDF_LUT;
        public TextureHandle environment_reflection_cube;
        public Vector4 environment_reflection_cube_hdr;
        public bool has_environment_reflection;

        public override void Reset()
        {
            directional_light_count = 0;
            directional_light_data_buffer = BufferHandle.nullHandle;
            BRDF_LUT = TextureHandle.nullHandle;
            has_BRDF_LUT = false;
            environment_reflection_cube = TextureHandle.nullHandle;
            environment_reflection_cube_hdr = Vector4.zero;
            has_environment_reflection = false;
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
                if (!brdf_lut_missing_reported)
                {
                    Debug.LogError($"YutrelRP: Missing BRDF LUT resource at Resources/{brdf_lut_resource_path}.png");
                    brdf_lut_missing_reported = true;
                }

                BRDF_LUT = TextureHandle.nullHandle;
                has_BRDF_LUT = false;
            }
            else
            {
                brdf_lut_rt_handle ??= RTHandles.Alloc(brdf_lut_texture);
                BRDF_LUT = render_graph.ImportTexture(brdf_lut_rt_handle);
                has_BRDF_LUT = true;
            }

            var reflection_texture = ReflectionProbe.defaultTexture;
            has_environment_reflection = reflection_texture != null;
            environment_reflection_cube_hdr = has_environment_reflection
                ? ReflectionProbe.defaultTextureHDRDecodeValues
                : Vector4.zero;
            reflection_texture ??= GetBlackEnvironmentReflection();

            if (environment_reflection_texture != reflection_texture)
            {
                if (environment_reflection_rt_handle != null)
                {
                    RTHandles.Release(environment_reflection_rt_handle);
                }

                environment_reflection_texture = reflection_texture;
                environment_reflection_rt_handle = RTHandles.Alloc(environment_reflection_texture);
            }

            environment_reflection_cube = render_graph.ImportTexture(environment_reflection_rt_handle);
        }

        private static Cubemap GetBlackEnvironmentReflection()
        {
            if (black_environment_reflection != null) return black_environment_reflection;

            black_environment_reflection = new Cubemap(1, TextureFormat.RGBA32, false)
            {
                name = "YutrelRP Black Environment Reflection"
            };

            var black = new[] { Color.black };
            foreach (var face in new[]
                     {
                         CubemapFace.PositiveX,
                         CubemapFace.NegativeX,
                         CubemapFace.PositiveY,
                         CubemapFace.NegativeY,
                         CubemapFace.PositiveZ,
                         CubemapFace.NegativeZ
                     })
            {
                black_environment_reflection.SetPixels(black, face);
            }

            black_environment_reflection.Apply(false, true);
            return black_environment_reflection;
        }
    };
}
