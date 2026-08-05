using System.Runtime.InteropServices;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace YutrelRP
{
    public static class PhotometricColor
    {
        private const float MinLuminance = 1e-6f;

        public static Vector3 NormalizeLinearSrgb(Color linearColor)
        {
            var rgb = new Vector3(
                Mathf.Max(0.0f, linearColor.r),
                Mathf.Max(0.0f, linearColor.g),
                Mathf.Max(0.0f, linearColor.b));
            var luminance = Vector3.Dot(rgb, new Vector3(0.2126f, 0.7152f, 0.0722f));
            return luminance > MinLuminance ? rgb / luminance : Vector3.zero;
        }
    }

    public class LightResources : ContextItem
    {
        private static Texture dfg_lut_texture;
        private static RTHandle dfg_lut_rt_handle;
        private static Texture environment_reflection_texture;
        private static RTHandle environment_reflection_rt_handle;
        private static Texture environment_skybox_texture;
        private static RTHandle environment_skybox_rt_handle;

        public static readonly int
            dfg_lut_ID = Shader.PropertyToID("_DFG_LUT"),
            environment_reflection_cube_ID = Shader.PropertyToID("_EnvironmentReflectionCube"),
            environment_reflection_cube_hdr_ID = Shader.PropertyToID("_EnvironmentReflectionCube_HDR"),
            environment_intensity_ID = Shader.PropertyToID("_EnvironmentIntensity"),
            environment_diffuse_multiplier_ID = Shader.PropertyToID("_EnvironmentDiffuseMultiplier"),
            environment_specular_multiplier_ID = Shader.PropertyToID("_EnvironmentSpecularMultiplier"),
            environment_skybox_ID = Shader.PropertyToID("_EnvironmentSkybox"),
            environment_skybox_multiplier_ID = Shader.PropertyToID("_EnvironmentSkyboxMultiplier"),
            ibl_roughness_one_level_ID = Shader.PropertyToID("_IblRoughnessOneLevel"),
            directional_light_data_ID = Shader.PropertyToID("_DirectionalLightData");

        public const int max_directional_light_count = 4;

        [StructLayout(LayoutKind.Sequential)]
        public struct DirectionalLightData
        {
            public const int stride = 4 * 4 * 3;

            public Vector3 color;
            // Directional Light.intensity is interpreted as illuminance in lux.
            public float illuminance;
            public Vector4 direction;
            public Vector4 shadow_data; // x: shadow index, y: 1 when Unity light uses soft shadows, z: strength, w: normal bias

            public DirectionalLightData(VisibleLight visible_light, Vector4 shadow_data)
            {
                color = PhotometricColor.NormalizeLinearSrgb(visible_light.light.color.linear);
                illuminance = visible_light.light.intensity;
                direction = -visible_light.localToWorldMatrix.GetColumn(2);
                this.shadow_data = shadow_data;
            }
        }

        public int directional_light_count;

        public readonly DirectionalLightData[] directional_light_data =
            new DirectionalLightData[max_directional_light_count];

        public BufferHandle directional_light_data_buffer;
        public TextureHandle DFG_LUT;
        public bool has_DFG_LUT;
        public TextureHandle environment_reflection_cube;
        public Vector4 environment_reflection_cube_hdr;
        public bool has_environment_reflection;
        public TextureHandle environment_skybox;
        public bool has_environment_skybox;
        public float environment_intensity;
        public float environment_diffuse_multiplier;
        public float environment_specular_multiplier;
        public float environment_skybox_multiplier;
        public float ibl_roughness_one_level;
        public SphericalHarmonicsL2 environment_diffuse_sh;
        public string environment_resource_error;
        public string environment_skybox_error;

        public override void Reset()
        {
            directional_light_count = 0;
            directional_light_data_buffer = BufferHandle.nullHandle;
            DFG_LUT = TextureHandle.nullHandle;
            has_DFG_LUT = false;
            environment_reflection_cube = TextureHandle.nullHandle;
            environment_reflection_cube_hdr = Vector4.zero;
            has_environment_reflection = false;
            environment_skybox = TextureHandle.nullHandle;
            has_environment_skybox = false;
            environment_intensity = 0.0f;
            environment_diffuse_multiplier = 1.0f;
            environment_specular_multiplier = 1.0f;
            environment_skybox_multiplier = 1.0f;
            ibl_roughness_one_level = 0.0f;
            environment_diffuse_sh = default;
            environment_resource_error = null;
            environment_skybox_error = null;
        }

        public void Setup(RenderGraph render_graph, IComputeRenderGraphBuilder builder, CullingResults
            culling_results, ref ShadowResources shadow_resources, Camera camera)
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
            ImportDfgLut(render_graph);

            YutrelEnvironmentLight.TryResolve(camera, out var environment_light);
            var environment_asset = environment_light != null ? environment_light.IblAsset : null;
            environment_resource_error = null;
            environment_skybox_error = null;

            var has_complete_environment = environment_asset != null &&
                                           environment_asset.HasLightingData &&
                                           environment_asset.TryGetDiffuseIrradianceSh(out environment_diffuse_sh);

            if (environment_light != null && !has_complete_environment)
            {
                environment_resource_error = environment_asset == null
                    ? "YutrelEnvironmentLight has no IBL asset."
                    : "YutrelEnvironmentLight IBL asset is incomplete: specular cubemap or diffuse SH is missing.";
            }

            if (has_complete_environment)
            {
                ImportEnvironmentReflection(render_graph, environment_asset.specularCubemap);
            }
            else
            {
                environment_diffuse_sh = default;
                ReleaseEnvironmentReflection();
                environment_reflection_cube = TextureHandle.nullHandle;
            }

            var camera_requests_skybox = camera.clearFlags == CameraClearFlags.Skybox;
            var should_render_skybox = camera_requests_skybox &&
                                       environment_light != null &&
                                       environment_light.RenderSkybox;
            var has_complete_skybox = should_render_skybox &&
                                      environment_asset != null &&
                                      environment_asset.HasSkyboxData;

            if (camera_requests_skybox && environment_light == null)
            {
                environment_skybox_error = "The camera scene has no enabled YutrelEnvironmentLight.";
            }
            else if (should_render_skybox && !has_complete_skybox)
            {
                environment_skybox_error = environment_asset == null
                    ? "YutrelEnvironmentLight has no IBL asset."
                    : "YutrelEnvironmentLight IBL asset has no source environment texture.";
            }

            if (has_complete_skybox)
            {
                ImportEnvironmentSkybox(render_graph, environment_asset.SourceEnvironmentTexture);
            }
            else
            {
                ReleaseEnvironmentSkybox();
                environment_skybox = TextureHandle.nullHandle;
            }

            has_environment_reflection = has_complete_environment;
            has_environment_skybox = has_complete_skybox;
            environment_reflection_cube_hdr = has_complete_environment ? new Vector4(1.0f, 1.0f, 0.0f, 0.0f) : Vector4.zero;
            environment_intensity = environment_light != null ? environment_light.Intensity : 1.0f;
            environment_diffuse_multiplier = has_complete_environment ? environment_light.DiffuseMultiplier : 1.0f;
            environment_specular_multiplier = has_complete_environment ? environment_light.SpecularMultiplier : 1.0f;
            environment_skybox_multiplier = has_complete_skybox ? environment_light.SkyboxMultiplier : 1.0f;
            ibl_roughness_one_level = has_complete_environment ? environment_asset.IblRoughnessOneLevel : 0.0f;
        }

        public static void Cleanup()
        {
            ReleaseDfgLut();
            ReleaseEnvironmentReflection();
            ReleaseEnvironmentSkybox();
        }

        private void ImportDfgLut(RenderGraph render_graph)
        {
            if (dfg_lut_rt_handle == null)
            {
                if (YutrelRPRuntimeTextureUtility.TryGetResources(out var resources))
                {
                    dfg_lut_texture = resources.dfg_lut;
                }

                if (dfg_lut_texture != null)
                {
                    dfg_lut_rt_handle = RTHandles.Alloc(dfg_lut_texture);
                }
            }

            if (dfg_lut_rt_handle == null)
            {
                DFG_LUT = TextureHandle.nullHandle;
                has_DFG_LUT = false;
                return;
            }

            DFG_LUT = render_graph.ImportTexture(dfg_lut_rt_handle);
            has_DFG_LUT = true;
        }

        private static void ReleaseDfgLut()
        {
            if (dfg_lut_rt_handle == null)
            {
                dfg_lut_texture = null;
                return;
            }

            RTHandles.Release(dfg_lut_rt_handle);
            dfg_lut_rt_handle = null;
            dfg_lut_texture = null;
        }

        private void ImportEnvironmentReflection(RenderGraph render_graph, Texture reflection_texture)
        {
            if (environment_reflection_texture != reflection_texture)
            {
                ReleaseEnvironmentReflection();
                environment_reflection_texture = reflection_texture;
                environment_reflection_rt_handle = RTHandles.Alloc(environment_reflection_texture);
            }

            environment_reflection_cube = render_graph.ImportTexture(environment_reflection_rt_handle);
        }

        private static void ReleaseEnvironmentReflection()
        {
            if (environment_reflection_rt_handle != null)
            {
                RTHandles.Release(environment_reflection_rt_handle);
                environment_reflection_rt_handle = null;
            }

            environment_reflection_texture = null;
        }

        private void ImportEnvironmentSkybox(RenderGraph render_graph, Texture skybox_texture)
        {
            if (environment_skybox_texture != skybox_texture)
            {
                ReleaseEnvironmentSkybox();
                environment_skybox_texture = skybox_texture;
                environment_skybox_rt_handle = RTHandles.Alloc(environment_skybox_texture);
            }

            environment_skybox = render_graph.ImportTexture(environment_skybox_rt_handle);
        }

        private static void ReleaseEnvironmentSkybox()
        {
            if (environment_skybox_rt_handle != null)
            {
                RTHandles.Release(environment_skybox_rt_handle);
                environment_skybox_rt_handle = null;
            }

            environment_skybox_texture = null;
        }
    };
}
