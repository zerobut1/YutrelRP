using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace YutrelRP
{
    internal class DirectionalLightPass
    {
        private static readonly ProfilingSampler sampler = new ProfilingSampler("Directional Light Pass");

        private static Material material;

        public static void Record(RenderGraph graph, RenderTargets textures, LightResources light_resources)
        {
            if (material == null)
                material = CoreUtils.CreateEngineMaterial(Shader.Find("YutrelRP/DirectionalLightPass"));

            using var builder =
                graph.AddRasterRenderPass<DirectionalLightPass>(sampler.name, out var pass, sampler);

            pass.GBuffer_A_ID = RenderTargets.GBuffer_A_ID;
            pass.GBuffer_B_ID = RenderTargets.GBuffer_B_ID;
            pass.GBuffer_C_ID = RenderTargets.GBuffer_C_ID;
            pass.scene_depth_ID = RenderTargets.scene_depth_ID;
            pass.BRDF_LUT_ID = LightResources.brdf_lut_ID;
            pass.shadow_mask_ID = RenderTargets.shadow_mask_ID;
            pass.directional_light_data_ID = LightResources.directional_light_data_ID;
            pass.GBuffer_A = textures.GBuffer_A;
            pass.GBuffer_B = textures.GBuffer_B;
            pass.GBuffer_C = textures.GBuffer_C;
            pass.scene_depth = textures.scene_depth;
            pass.BRDF_LUT = light_resources.BRDF_LUT;
            pass.shadow_mask = textures.shadow_mask;
            pass.directional_light_data_buffer = light_resources.directional_light_data_buffer;

            builder.UseTexture(pass.GBuffer_A);
            builder.UseTexture(pass.GBuffer_B);
            builder.UseTexture(pass.GBuffer_C);
            builder.UseTexture(pass.scene_depth);
            builder.UseTexture(pass.BRDF_LUT);
            builder.UseTexture(pass.shadow_mask);
            builder.UseBuffer(pass.directional_light_data_buffer);
            builder.SetRenderAttachment(textures.scene_color, 0, AccessFlags.Write);

            builder.SetRenderFunc<DirectionalLightPass>(static (pass, context) => pass.Render(context));
        }

        // data
        private int
            GBuffer_A_ID,
            GBuffer_B_ID,
            GBuffer_C_ID,
            scene_depth_ID,
            BRDF_LUT_ID,
            shadow_mask_ID,
            directional_light_data_ID;

        private TextureHandle
            GBuffer_A,
            GBuffer_B,
            GBuffer_C,
            scene_depth,
            BRDF_LUT,
            shadow_mask;

        private BufferHandle
            directional_light_data_buffer;

        private void Render(RasterGraphContext context)
        {
            var cmd = context.cmd;
            material.SetTexture(GBuffer_A_ID, GBuffer_A);
            material.SetTexture(GBuffer_B_ID, GBuffer_B);
            material.SetTexture(GBuffer_C_ID, GBuffer_C);
            material.SetTexture(scene_depth_ID, scene_depth);
            material.SetTexture(BRDF_LUT_ID, BRDF_LUT);
            material.SetTexture(shadow_mask_ID, shadow_mask);
            material.SetBuffer(directional_light_data_ID, directional_light_data_buffer);

            CoreUtils.DrawFullScreen(cmd, material);
        }
    }
}