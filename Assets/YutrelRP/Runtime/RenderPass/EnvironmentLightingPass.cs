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

        public static void Record(RenderGraph graph, RenderTargets textures, LightResources light_resources)
        {
            if (material == null) material = CoreUtils.CreateEngineMaterial(Shader.Find("YutrelRP/EnvironmentLightingPass"));
            if (property_block == null) property_block = new MaterialPropertyBlock();

            using var builder =
                graph.AddRasterRenderPass<EnvironmentLightingPass>(sampler.name, out var pass, sampler);

            pass.GBuffer_A_ID = RenderTargets.GBuffer_A_ID;
            pass.GBuffer_B_ID = RenderTargets.GBuffer_B_ID;
            pass.GBuffer_C_ID = RenderTargets.GBuffer_C_ID;
            pass.scene_depth_ID = RenderTargets.scene_depth_ID;
            pass.dfg_lut_ID = LightResources.dfg_lut_ID;
            pass.environment_reflection_cube_ID = LightResources.environment_reflection_cube_ID;
            pass.environment_reflection_cube_hdr_ID = LightResources.environment_reflection_cube_hdr_ID;
            pass.environment_intensity_ID = LightResources.environment_intensity_ID;
            pass.environment_diffuse_multiplier_ID = LightResources.environment_diffuse_multiplier_ID;
            pass.environment_specular_multiplier_ID = LightResources.environment_specular_multiplier_ID;
            pass.ibl_roughness_one_level_ID = LightResources.ibl_roughness_one_level_ID;

            pass.GBuffer_A = textures.GBuffer_A;
            pass.GBuffer_B = textures.GBuffer_B;
            pass.GBuffer_C = textures.GBuffer_C;
            pass.scene_depth = textures.scene_depth;
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

            builder.UseTexture(pass.GBuffer_A);
            builder.UseTexture(pass.GBuffer_B);
            builder.UseTexture(pass.GBuffer_C);
            builder.UseTexture(pass.scene_depth);
            builder.UseTexture(pass.DFG_LUT);
            if (pass.environment_reflection_cube.IsValid())
            {
                builder.UseTexture(pass.environment_reflection_cube);
            }
            builder.SetRenderAttachment(textures.scene_color, 0, AccessFlags.ReadWrite);

            builder.SetRenderFunc<EnvironmentLightingPass>(static (pass, context) => pass.Render(context));
        }

        private int
            GBuffer_A_ID,
            GBuffer_B_ID,
            GBuffer_C_ID,
            scene_depth_ID,
            dfg_lut_ID,
            environment_reflection_cube_ID,
            environment_reflection_cube_hdr_ID,
            environment_intensity_ID,
            environment_diffuse_multiplier_ID,
            environment_specular_multiplier_ID,
            ibl_roughness_one_level_ID;

        private TextureHandle
            GBuffer_A,
            GBuffer_B,
            GBuffer_C,
            scene_depth,
            DFG_LUT,
            environment_reflection_cube;

        private Vector4 environment_reflection_cube_hdr;
        private float environment_intensity;
        private float environment_diffuse_multiplier;
        private float environment_specular_multiplier;
        private float ibl_roughness_one_level;
        private SphericalHarmonicsL2 ambient_probe;

        private void Render(RasterGraphContext context)
        {
            property_block.Clear();
            property_block.SetTexture(GBuffer_A_ID, GBuffer_A);
            property_block.SetTexture(GBuffer_B_ID, GBuffer_B);
            property_block.SetTexture(GBuffer_C_ID, GBuffer_C);
            property_block.SetTexture(scene_depth_ID, scene_depth);
            property_block.SetTexture(dfg_lut_ID, DFG_LUT);
            if (environment_reflection_cube.IsValid())
            {
                property_block.SetTexture(environment_reflection_cube_ID, environment_reflection_cube);
            }
            property_block.SetVector(environment_reflection_cube_hdr_ID, environment_reflection_cube_hdr);
            property_block.SetFloat(environment_intensity_ID, environment_intensity);
            property_block.SetFloat(environment_diffuse_multiplier_ID, environment_diffuse_multiplier);
            property_block.SetFloat(environment_specular_multiplier_ID, environment_specular_multiplier);
            property_block.SetFloat(ibl_roughness_one_level_ID, ibl_roughness_one_level);
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
