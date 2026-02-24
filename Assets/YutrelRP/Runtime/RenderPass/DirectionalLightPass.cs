using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace YutrelRP
{
    internal class DirectionalLightPass
    {
        private static readonly ProfilingSampler sampler = new ProfilingSampler("Directional Light Pass");

        private static Material material;
        private static MaterialPropertyBlock propertyBlock;
        private static readonly int light_index_ID = Shader.PropertyToID("_LightIndex");

        public static void Record(RenderGraph graph, RenderTargets textures, LightResources light_resources)
        {
            if (light_resources.directional_light_count == 0) return;

            if (material == null)
                material = CoreUtils.CreateEngineMaterial(Shader.Find("YutrelRP/DirectionalLightPass"));

            for (int i = 0; i < light_resources.directional_light_count; i++)
            {
                using var builder =
                    graph.AddRasterRenderPass<DirectionalLightPass>(sampler.name + " " + i, out var pass, sampler);

                pass.GBuffer_A_ID = RenderTargets.GBuffer_A_ID;
                pass.GBuffer_B_ID = RenderTargets.GBuffer_B_ID;
                pass.GBuffer_C_ID = RenderTargets.GBuffer_C_ID;
                pass.scene_depth_ID = RenderTargets.scene_depth_ID;
                pass.BRDF_LUT_ID = LightResources.brdf_lut_ID;
                pass.shadow_mask_ID = RenderTargets.shadow_mask_ID;
                pass.directional_light_data_ID = LightResources.directional_light_data_ID;
                pass.light_index_ID_field = DirectionalLightPass.light_index_ID;
                pass.GBuffer_A = textures.GBuffer_A;
                pass.GBuffer_B = textures.GBuffer_B;
                pass.GBuffer_C = textures.GBuffer_C;
                pass.scene_depth = textures.scene_depth;
                pass.BRDF_LUT = light_resources.BRDF_LUT;
                pass.shadow_mask = textures.shadow_mask;
                pass.directional_light_data_buffer = light_resources.directional_light_data_buffer;
                pass.light_index = i;

                builder.UseTexture(pass.GBuffer_A);
                builder.UseTexture(pass.GBuffer_B);
                builder.UseTexture(pass.GBuffer_C);
                builder.UseTexture(pass.scene_depth);
                builder.UseTexture(pass.BRDF_LUT);
                builder.UseTexture(pass.shadow_mask);
                builder.UseBuffer(pass.directional_light_data_buffer);
                builder.SetRenderAttachment(textures.scene_color, 0, AccessFlags.ReadWrite);

                builder.SetRenderFunc<DirectionalLightPass>(static (pass, context) => pass.Render(context));
            }
        }

        // data
        private int
            GBuffer_A_ID,
            GBuffer_B_ID,
            GBuffer_C_ID,
            scene_depth_ID,
            BRDF_LUT_ID,
            shadow_mask_ID,
            directional_light_data_ID,
            light_index_ID_field;

        private int light_index;

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
            if (propertyBlock == null) propertyBlock = new MaterialPropertyBlock();
            propertyBlock.SetTexture(GBuffer_A_ID, GBuffer_A);
            propertyBlock.SetTexture(GBuffer_B_ID, GBuffer_B);
            propertyBlock.SetTexture(GBuffer_C_ID, GBuffer_C);
            propertyBlock.SetTexture(scene_depth_ID, scene_depth);
            propertyBlock.SetTexture(BRDF_LUT_ID, BRDF_LUT);
            propertyBlock.SetTexture(shadow_mask_ID, shadow_mask);
            propertyBlock.SetBuffer(directional_light_data_ID, directional_light_data_buffer);
            propertyBlock.SetInteger(light_index_ID_field, light_index);

            CoreUtils.DrawFullScreen(cmd, material, propertyBlock);
        }
    }
}