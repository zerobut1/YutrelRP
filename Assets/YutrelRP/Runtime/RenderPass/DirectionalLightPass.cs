using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace YutrelRP
{
    internal class DirectionalLightPass
    {
        private static readonly ProfilingSampler sampler = new ProfilingSampler("Directional Light Pass");

        private static Material material;

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
            material.SetTexture(Shader.PropertyToID("_GBuffer_A"), GBuffer_A);
            material.SetTexture(Shader.PropertyToID("_GBuffer_B"), GBuffer_B);
            material.SetTexture(Shader.PropertyToID("_GBuffer_C"), GBuffer_C);
            material.SetTexture(Shader.PropertyToID("_SceneDepth"), scene_depth);
            material.SetTexture(Shader.PropertyToID("_ShadowMask"), shadow_mask);
            material.SetBuffer(Shader.PropertyToID("_DirectionalLightData"),
                directional_light_data_buffer);

            CoreUtils.DrawFullScreen(cmd, material);
        }

        public static void Record(RenderGraph graph, RenderTargets textures, LightResources light_resources)
        {
            if (material == null)
                material = CoreUtils.CreateEngineMaterial(Shader.Find("YutrelRP/DirectionalLightPass"));

            using var builder =
                graph.AddRasterRenderPass<DirectionalLightPass>("Directional Light Pass", out var pass, sampler);

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
    }
}