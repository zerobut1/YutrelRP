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

        private static readonly int
            ambient_probe_SHAr_ID = Shader.PropertyToID("_AmbientProbeSHAr"),
            ambient_probe_SHAg_ID = Shader.PropertyToID("_AmbientProbeSHAg"),
            ambient_probe_SHAb_ID = Shader.PropertyToID("_AmbientProbeSHAb"),
            ambient_probe_SHBr_ID = Shader.PropertyToID("_AmbientProbeSHBr"),
            ambient_probe_SHBg_ID = Shader.PropertyToID("_AmbientProbeSHBg"),
            ambient_probe_SHBb_ID = Shader.PropertyToID("_AmbientProbeSHBb"),
            ambient_probe_SHC_ID = Shader.PropertyToID("_AmbientProbeSHC");

        public static void Record(RenderGraph graph, RenderTargets textures, LightResources light_resources)
        {
            if (!light_resources.has_BRDF_LUT) return;

            if (material == null) material = CoreUtils.CreateEngineMaterial(Shader.Find("YutrelRP/EnvironmentLightingPass"));
            if (property_block == null) property_block = new MaterialPropertyBlock();

            using var builder =
                graph.AddRasterRenderPass<EnvironmentLightingPass>(sampler.name, out var pass, sampler);

            pass.GBuffer_A_ID = RenderTargets.GBuffer_A_ID;
            pass.GBuffer_B_ID = RenderTargets.GBuffer_B_ID;
            pass.GBuffer_C_ID = RenderTargets.GBuffer_C_ID;
            pass.scene_depth_ID = RenderTargets.scene_depth_ID;
            pass.brdf_lut_ID = LightResources.brdf_lut_ID;
            pass.environment_reflection_cube_ID = LightResources.environment_reflection_cube_ID;
            pass.environment_reflection_cube_hdr_ID = LightResources.environment_reflection_cube_hdr_ID;
            pass.environment_reflection_available_ID = LightResources.environment_reflection_available_ID;

            pass.GBuffer_A = textures.GBuffer_A;
            pass.GBuffer_B = textures.GBuffer_B;
            pass.GBuffer_C = textures.GBuffer_C;
            pass.scene_depth = textures.scene_depth;
            pass.BRDF_LUT = light_resources.BRDF_LUT;
            pass.environment_reflection_cube = light_resources.environment_reflection_cube;
            pass.environment_reflection_cube_hdr = light_resources.environment_reflection_cube_hdr;
            pass.environment_reflection_available = light_resources.has_environment_reflection ? 1.0f : 0.0f;
            pass.ambient_probe = RenderSettings.ambientProbe;

            builder.UseTexture(pass.GBuffer_A);
            builder.UseTexture(pass.GBuffer_B);
            builder.UseTexture(pass.GBuffer_C);
            builder.UseTexture(pass.scene_depth);
            builder.UseTexture(pass.BRDF_LUT);
            builder.UseTexture(pass.environment_reflection_cube);
            builder.SetRenderAttachment(textures.scene_color, 0, AccessFlags.ReadWrite);

            builder.SetRenderFunc<EnvironmentLightingPass>(static (pass, context) => pass.Render(context));
        }

        private int
            GBuffer_A_ID,
            GBuffer_B_ID,
            GBuffer_C_ID,
            scene_depth_ID,
            brdf_lut_ID,
            environment_reflection_cube_ID,
            environment_reflection_cube_hdr_ID,
            environment_reflection_available_ID;

        private TextureHandle
            GBuffer_A,
            GBuffer_B,
            GBuffer_C,
            scene_depth,
            BRDF_LUT,
            environment_reflection_cube;

        private Vector4 environment_reflection_cube_hdr;
        private float environment_reflection_available;
        private SphericalHarmonicsL2 ambient_probe;

        private void Render(RasterGraphContext context)
        {
            property_block.Clear();
            property_block.SetTexture(GBuffer_A_ID, GBuffer_A);
            property_block.SetTexture(GBuffer_B_ID, GBuffer_B);
            property_block.SetTexture(GBuffer_C_ID, GBuffer_C);
            property_block.SetTexture(scene_depth_ID, scene_depth);
            property_block.SetTexture(brdf_lut_ID, BRDF_LUT);
            property_block.SetTexture(environment_reflection_cube_ID, environment_reflection_cube);
            property_block.SetVector(environment_reflection_cube_hdr_ID, environment_reflection_cube_hdr);
            property_block.SetFloat(environment_reflection_available_ID, environment_reflection_available);
            SetAmbientProbeShaderConstants(property_block, ambient_probe);

            CoreUtils.DrawFullScreen(context.cmd, material, property_block);
        }

        public static void Cleanup()
        {
            CoreUtils.Destroy(material);
            material = null;
            property_block = null;
        }

        private static void SetAmbientProbeShaderConstants(MaterialPropertyBlock properties, SphericalHarmonicsL2 sh)
        {
            properties.SetVector(ambient_probe_SHAr_ID, new Vector4(sh[0, 3], sh[0, 1], sh[0, 2], sh[0, 0] - sh[0, 6]));
            properties.SetVector(ambient_probe_SHAg_ID, new Vector4(sh[1, 3], sh[1, 1], sh[1, 2], sh[1, 0] - sh[1, 6]));
            properties.SetVector(ambient_probe_SHAb_ID, new Vector4(sh[2, 3], sh[2, 1], sh[2, 2], sh[2, 0] - sh[2, 6]));
            properties.SetVector(ambient_probe_SHBr_ID, new Vector4(sh[0, 4], sh[0, 5], sh[0, 6] * 3.0f, sh[0, 7]));
            properties.SetVector(ambient_probe_SHBg_ID, new Vector4(sh[1, 4], sh[1, 5], sh[1, 6] * 3.0f, sh[1, 7]));
            properties.SetVector(ambient_probe_SHBb_ID, new Vector4(sh[2, 4], sh[2, 5], sh[2, 6] * 3.0f, sh[2, 7]));
            properties.SetVector(ambient_probe_SHC_ID, new Vector4(sh[0, 8], sh[1, 8], sh[2, 8], 1.0f));
        }
    }
}
