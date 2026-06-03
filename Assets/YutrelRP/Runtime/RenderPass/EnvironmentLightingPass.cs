using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace YutrelRP
{
    internal class EnvironmentLightingPass
    {
        private static readonly ProfilingSampler sampler = new("Environment Lighting Pass");
        private static Material material;
        private static MaterialPropertyBlock property_block;

        private static readonly int[] ibl_sh_IDs =
        {
            Shader.PropertyToID("_IblSH0"),
            Shader.PropertyToID("_IblSH1"),
            Shader.PropertyToID("_IblSH2"),
            Shader.PropertyToID("_IblSH3"),
            Shader.PropertyToID("_IblSH4"),
            Shader.PropertyToID("_IblSH5"),
            Shader.PropertyToID("_IblSH6"),
            Shader.PropertyToID("_IblSH7"),
            Shader.PropertyToID("_IblSH8")
        };

        public static void Record(RenderGraph graph, RenderTargets textures, LightResources light_resources,
            DDGIResources ddgi_resources, YutrelRPSettings.DDGISettings ddgi_settings)
        {
            if (material == null) material = CoreUtils.CreateEngineMaterial(Shader.Find("YutrelRP/EnvironmentLightingPass"));
            if (property_block == null) property_block = new MaterialPropertyBlock();

            using var builder =
                graph.AddRasterRenderPass<EnvironmentLightingPass>(sampler.name, out var pass, sampler);

            pass.GBuffer_A_ID = RenderTargets.GBuffer_A_ID;
            pass.GBuffer_B_ID = RenderTargets.GBuffer_B_ID;
            pass.GBuffer_C_ID = RenderTargets.GBuffer_C_ID;
            pass.scene_depth_ID = RenderTargets.scene_depth_ID;
            pass.screen_space_ao_ID = RenderTargets.screen_space_ao_ID;
            pass.dfg_lut_ID = LightResources.dfg_lut_ID;
            pass.environment_reflection_cube_ID = LightResources.environment_reflection_cube_ID;
            pass.environment_reflection_cube_hdr_ID = LightResources.environment_reflection_cube_hdr_ID;
            pass.environment_intensity_ID = LightResources.environment_intensity_ID;
            pass.environment_diffuse_multiplier_ID = LightResources.environment_diffuse_multiplier_ID;
            pass.environment_specular_multiplier_ID = LightResources.environment_specular_multiplier_ID;
            pass.ibl_roughness_one_level_ID = LightResources.ibl_roughness_one_level_ID;
            pass.ddgi_probe_irradiance_ID = DDGIResources.probe_irradiance_ID;
            pass.ddgi_probe_irradiance_dimensions_ID = DDGIResources.probe_irradiance_dimensions_ID;
            pass.ddgi_probe_distance_ID = DDGIResources.probe_distance_ID;
            pass.ddgi_probe_distance_dimensions_ID = DDGIResources.probe_distance_dimensions_ID;
            pass.ddgi_probe_ray_data_max_distance_ID = DDGIResources.probe_ray_data_max_distance_ID;
            pass.ddgi_probe_count_ID = Shader.PropertyToID("_DDGIProbeCount");
            pass.ddgi_volume_min_ws_ID = DDGIResources.volume_min_ws_ID;
            pass.ddgi_volume_max_ws_ID = DDGIResources.volume_max_ws_ID;
            pass.ddgi_probe_spacing_ws_ID = DDGIResources.probe_spacing_ws_ID;
            pass.ddgi_probe_normal_bias_ID = DDGIResources.probe_normal_bias_ID;
            pass.ddgi_probe_view_bias_ID = DDGIResources.probe_view_bias_ID;
            pass.ddgi_gather_valid_ID = DDGIResources.gather_valid_ID;
            pass.ddgi_diffuse_intensity_ID = DDGIResources.diffuse_intensity_ID;

            pass.GBuffer_A = textures.GBuffer_A;
            pass.GBuffer_B = textures.GBuffer_B;
            pass.GBuffer_C = textures.GBuffer_C;
            pass.scene_depth = textures.scene_depth;
            pass.screen_space_ao = textures.screen_space_ao.IsValid()
                ? textures.screen_space_ao
                : graph.defaultResources.whiteTexture;
            pass.DFG_LUT = light_resources.has_DFG_LUT ? light_resources.DFG_LUT : graph.defaultResources.whiteTexture;
            pass.environment_reflection_cube = light_resources.has_environment_reflection
                ? light_resources.environment_reflection_cube
                : TextureHandle.nullHandle;
            pass.environment_reflection_cube_hdr = light_resources.environment_reflection_cube_hdr;
            pass.environment_intensity = light_resources.environment_intensity;
            pass.environment_diffuse_multiplier = light_resources.environment_diffuse_multiplier;
            pass.environment_specular_multiplier = light_resources.environment_specular_multiplier;
            pass.ibl_roughness_one_level = light_resources.ibl_roughness_one_level;
            pass.ambient_probe = light_resources.environment_diffuse_sh;

            pass.has_DDGI_gather = ddgi_resources != null && ddgi_resources.has_gather_data &&
                                   ddgi_resources.probe_irradiance.IsValid() &&
                                   ddgi_resources.probe_distance.IsValid() &&
                                   ddgi_resources.probe_count.x > 1 &&
                                   ddgi_resources.probe_count.y > 1 &&
                                   ddgi_resources.probe_count.z > 1 &&
                                   ddgi_resources.probe_irradiance_interior_texels > 0 &&
                                   ddgi_resources.probe_distance_interior_texels > 0;
            pass.ddgi_probe_irradiance = pass.has_DDGI_gather
                ? ddgi_resources.probe_irradiance
                : TextureHandle.nullHandle;
            pass.ddgi_probe_distance = pass.has_DDGI_gather
                ? ddgi_resources.probe_distance
                : TextureHandle.nullHandle;
            pass.ddgi_probe_irradiance_dimensions = pass.has_DDGI_gather
                ? ddgi_resources.ProbeIrradianceDimensions
                : Vector4.zero;
            pass.ddgi_probe_distance_dimensions = pass.has_DDGI_gather
                ? ddgi_resources.ProbeDistanceDimensions
                : Vector4.zero;
            pass.ddgi_probe_count = pass.has_DDGI_gather ? ddgi_resources.probe_count : Vector3Int.zero;
            pass.ddgi_probe_ray_data_max_distance = pass.has_DDGI_gather
                ? Mathf.Max(0.001f, ddgi_resources.probe_max_ray_distance)
                : 0.001f;
            pass.ddgi_volume_min_ws = pass.has_DDGI_gather ? ddgi_resources.volume_min_ws : Vector3.zero;
            pass.ddgi_volume_max_ws = pass.has_DDGI_gather ? ddgi_resources.volume_max_ws : Vector3.zero;
            pass.ddgi_probe_spacing_ws = pass.has_DDGI_gather ? ddgi_resources.probe_spacing_ws : Vector3.zero;
            pass.ddgi_probe_normal_bias = pass.has_DDGI_gather ? ddgi_resources.probe_normal_bias : 0.0f;
            pass.ddgi_probe_view_bias = pass.has_DDGI_gather ? ddgi_resources.probe_view_bias : 0.0f;
            pass.ddgi_diffuse_intensity = Mathf.Max(0.0f, ddgi_settings != null ? ddgi_settings.diffuseIntensity : 1.0f);

            builder.UseTexture(pass.GBuffer_A);
            builder.UseTexture(pass.GBuffer_B);
            builder.UseTexture(pass.GBuffer_C);
            builder.UseTexture(pass.scene_depth);
            builder.UseTexture(pass.screen_space_ao);
            builder.UseTexture(pass.DFG_LUT);
            if (pass.environment_reflection_cube.IsValid())
            {
                builder.UseTexture(pass.environment_reflection_cube);
            }
            if (pass.has_DDGI_gather)
            {
                builder.UseTexture(pass.ddgi_probe_irradiance);
                builder.UseTexture(pass.ddgi_probe_distance);
            }
            builder.SetRenderAttachment(textures.scene_color, 0, AccessFlags.ReadWrite);

            builder.SetRenderFunc<EnvironmentLightingPass>(static (pass, context) => pass.Render(context));
        }

        private int
            GBuffer_A_ID,
            GBuffer_B_ID,
            GBuffer_C_ID,
            scene_depth_ID,
            screen_space_ao_ID,
            dfg_lut_ID,
            environment_reflection_cube_ID,
            environment_reflection_cube_hdr_ID,
            environment_intensity_ID,
            environment_diffuse_multiplier_ID,
            environment_specular_multiplier_ID,
            ibl_roughness_one_level_ID,
            ddgi_probe_irradiance_ID,
            ddgi_probe_irradiance_dimensions_ID,
            ddgi_probe_distance_ID,
            ddgi_probe_distance_dimensions_ID,
            ddgi_probe_ray_data_max_distance_ID,
            ddgi_probe_count_ID,
            ddgi_volume_min_ws_ID,
            ddgi_volume_max_ws_ID,
            ddgi_probe_spacing_ws_ID,
            ddgi_probe_normal_bias_ID,
            ddgi_probe_view_bias_ID,
            ddgi_gather_valid_ID,
            ddgi_diffuse_intensity_ID;

        private TextureHandle
            GBuffer_A,
            GBuffer_B,
            GBuffer_C,
            scene_depth,
            screen_space_ao,
            DFG_LUT,
            environment_reflection_cube,
            ddgi_probe_irradiance,
            ddgi_probe_distance;

        private Vector4 environment_reflection_cube_hdr;
        private float environment_intensity;
        private float environment_diffuse_multiplier;
        private float environment_specular_multiplier;
        private float ibl_roughness_one_level;
        private SphericalHarmonicsL2 ambient_probe;
        private bool has_DDGI_gather;
        private Vector4 ddgi_probe_irradiance_dimensions;
        private Vector4 ddgi_probe_distance_dimensions;
        private Vector3Int ddgi_probe_count;
        private float ddgi_probe_ray_data_max_distance;
        private Vector3 ddgi_volume_min_ws;
        private Vector3 ddgi_volume_max_ws;
        private Vector3 ddgi_probe_spacing_ws;
        private float ddgi_probe_normal_bias;
        private float ddgi_probe_view_bias;
        private float ddgi_diffuse_intensity;

        private void Render(RasterGraphContext context)
        {
            property_block.Clear();
            property_block.SetTexture(GBuffer_A_ID, GBuffer_A);
            property_block.SetTexture(GBuffer_B_ID, GBuffer_B);
            property_block.SetTexture(GBuffer_C_ID, GBuffer_C);
            property_block.SetTexture(scene_depth_ID, scene_depth);
            property_block.SetTexture(screen_space_ao_ID, screen_space_ao);
            property_block.SetTexture(dfg_lut_ID, DFG_LUT);
            if (environment_reflection_cube.IsValid())
            {
                property_block.SetTexture(environment_reflection_cube_ID, environment_reflection_cube);
            }
            if (has_DDGI_gather)
            {
                property_block.SetTexture(ddgi_probe_irradiance_ID, ddgi_probe_irradiance);
                property_block.SetTexture(ddgi_probe_distance_ID, ddgi_probe_distance);
            }
            property_block.SetVector(environment_reflection_cube_hdr_ID, environment_reflection_cube_hdr);
            property_block.SetFloat(environment_intensity_ID, environment_intensity);
            property_block.SetFloat(environment_diffuse_multiplier_ID, environment_diffuse_multiplier);
            property_block.SetFloat(environment_specular_multiplier_ID, environment_specular_multiplier);
            property_block.SetFloat(ibl_roughness_one_level_ID, ibl_roughness_one_level);
            property_block.SetVector(ddgi_probe_irradiance_dimensions_ID, ddgi_probe_irradiance_dimensions);
            property_block.SetVector(ddgi_probe_distance_dimensions_ID, ddgi_probe_distance_dimensions);
            property_block.SetFloat(ddgi_probe_ray_data_max_distance_ID, ddgi_probe_ray_data_max_distance);
            property_block.SetVector(ddgi_probe_count_ID,
                new Vector4(ddgi_probe_count.x, ddgi_probe_count.y, ddgi_probe_count.z, 0.0f));
            property_block.SetVector(ddgi_volume_min_ws_ID, ddgi_volume_min_ws);
            property_block.SetVector(ddgi_volume_max_ws_ID, ddgi_volume_max_ws);
            property_block.SetVector(ddgi_probe_spacing_ws_ID, ddgi_probe_spacing_ws);
            property_block.SetFloat(ddgi_probe_normal_bias_ID, ddgi_probe_normal_bias);
            property_block.SetFloat(ddgi_probe_view_bias_ID, ddgi_probe_view_bias);
            property_block.SetFloat(ddgi_gather_valid_ID, has_DDGI_gather ? 1.0f : 0.0f);
            property_block.SetFloat(ddgi_diffuse_intensity_ID, ddgi_diffuse_intensity);
            SetIblShShaderConstants(property_block, ambient_probe);

            CoreUtils.DrawFullScreen(context.cmd, material, property_block);
        }

        public static void Cleanup()
        {
            CoreUtils.Destroy(material);
            material = null;
            property_block = null;
        }

        private static void SetIblShShaderConstants(MaterialPropertyBlock properties, SphericalHarmonicsL2 sh)
        {
            for (var coefficient = 0; coefficient < YutrelIBLAsset.diffuseIrradianceShCoefficientCount; coefficient++)
            {
                properties.SetVector(ibl_sh_IDs[coefficient], new Vector4(sh[0, coefficient], sh[1, coefficient], sh[2, coefficient], 0.0f));
            }
        }
    }
}
