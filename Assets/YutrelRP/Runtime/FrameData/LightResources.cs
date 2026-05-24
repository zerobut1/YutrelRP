using System.Runtime.InteropServices;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.SceneManagement;

namespace YutrelRP
{
    public class LightResources : ContextItem
    {
        private static Texture brdf_lut_texture;
        private static RTHandle brdf_lut_rt_handle;
        private static Texture environment_reflection_texture;
        private static RTHandle environment_reflection_rt_handle;

        public static readonly int
            brdf_lut_ID = Shader.PropertyToID("_BRDF_LUT"),
            environment_reflection_cube_ID = Shader.PropertyToID("_EnvironmentReflectionCube"),
            environment_reflection_cube_hdr_ID = Shader.PropertyToID("_EnvironmentReflectionCube_HDR"),
            environment_intensity_ID = Shader.PropertyToID("_EnvironmentIntensity"),
            environment_diffuse_multiplier_ID = Shader.PropertyToID("_EnvironmentDiffuseMultiplier"),
            environment_specular_multiplier_ID = Shader.PropertyToID("_EnvironmentSpecularMultiplier"),
            ibl_roughness_one_level_ID = Shader.PropertyToID("_IblRoughnessOneLevel"),
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
        public float environment_intensity;
        public float environment_diffuse_multiplier;
        public float environment_specular_multiplier;
        public float ibl_roughness_one_level;
        public SphericalHarmonicsL2 environment_diffuse_sh;
        public string environment_resource_error;

        public override void Reset()
        {
            directional_light_count = 0;
            directional_light_data_buffer = BufferHandle.nullHandle;
            BRDF_LUT = TextureHandle.nullHandle;
            has_BRDF_LUT = false;
            environment_reflection_cube = TextureHandle.nullHandle;
            environment_reflection_cube_hdr = Vector4.zero;
            has_environment_reflection = false;
            environment_intensity = 0.0f;
            environment_diffuse_multiplier = 1.0f;
            environment_specular_multiplier = 1.0f;
            ibl_roughness_one_level = 0.0f;
            environment_diffuse_sh = default;
            environment_resource_error = null;
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

            var environment_light = ResolveEnvironmentLight(camera);
            var environment_asset = environment_light != null ? environment_light.IblAsset : null;
            environment_resource_error = null;

            var has_complete_environment = environment_asset != null &&
                                           environment_asset.HasCompleteData &&
                                           environment_asset.TryGetDiffuseIrradianceSh(out environment_diffuse_sh);

            if (environment_light != null && !has_complete_environment)
            {
                environment_resource_error = environment_asset == null
                    ? "YutrelEnvironmentLight has no IBL asset."
                    : "YutrelEnvironmentLight IBL asset is incomplete: specular cubemap, DFG LUT, or diffuse SH is missing.";
            }

            if (has_complete_environment)
            {
                ImportBrdfLut(render_graph, environment_asset.dfgLut);
                ImportEnvironmentReflection(render_graph, environment_asset.specularCubemap);
            }
            else
            {
                environment_diffuse_sh = default;
                ReleaseBrdfLut();
                BRDF_LUT = TextureHandle.nullHandle;
                has_BRDF_LUT = false;
                ReleaseEnvironmentReflection();
                environment_reflection_cube = TextureHandle.nullHandle;
            }

            has_environment_reflection = has_complete_environment;
            environment_reflection_cube_hdr = has_complete_environment ? new Vector4(1.0f, 1.0f, 0.0f, 0.0f) : Vector4.zero;
            environment_intensity = has_complete_environment ? environment_light.Intensity : 0.0f;
            environment_diffuse_multiplier = has_complete_environment ? environment_light.DiffuseMultiplier : 1.0f;
            environment_specular_multiplier = has_complete_environment ? environment_light.SpecularMultiplier : 1.0f;
            ibl_roughness_one_level = has_complete_environment ? environment_asset.IblRoughnessOneLevel : 0.0f;
        }

        public static void Cleanup()
        {
            ReleaseBrdfLut();
            ReleaseEnvironmentReflection();
        }

        private static YutrelEnvironmentLight ResolveEnvironmentLight(Camera camera)
        {
            if (camera == null)
            {
                return null;
            }

            var camera_scene = camera.gameObject.scene;
            if (YutrelEnvironmentLight.TryResolve(camera_scene, out var environment_light))
            {
                return environment_light;
            }

            // Scene-view and preview cameras are often not owned by the scene they render.
            // For those Unity editor cameras only, fall back to the active scene binding.
            if (camera.cameraType == CameraType.SceneView || camera.cameraType == CameraType.Preview)
            {
                var active_scene = SceneManager.GetActiveScene();
                if (active_scene != camera_scene && YutrelEnvironmentLight.TryResolve(active_scene, out environment_light))
                {
                    return environment_light;
                }
            }

            return null;
        }

        private void ImportBrdfLut(RenderGraph render_graph, Texture dfg_lut)
        {
            if (brdf_lut_texture != dfg_lut)
            {
                ReleaseBrdfLut();
                brdf_lut_texture = dfg_lut;
                brdf_lut_rt_handle = RTHandles.Alloc(brdf_lut_texture);
            }

            BRDF_LUT = render_graph.ImportTexture(brdf_lut_rt_handle);
            has_BRDF_LUT = true;
        }

        private static void ReleaseBrdfLut()
        {
            if (brdf_lut_rt_handle == null)
            {
                brdf_lut_texture = null;
                return;
            }

            RTHandles.Release(brdf_lut_rt_handle);
            brdf_lut_rt_handle = null;
            brdf_lut_texture = null;
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
    };
}
