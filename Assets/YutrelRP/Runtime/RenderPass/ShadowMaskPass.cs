using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace YutrelRP
{
    internal class ShadowMaskPass
    {
        private static readonly ProfilingSampler sampler = new("Shadow Mask Pass");
        private static Material material;

        // data
        private TextureHandle
            directional_shadow_atlas,
            scene_depth;

        private BufferHandle
            directional_light_data_buffer,
            directional_shadow_vp_matrices_buffer;

        internal static void Record(RenderGraph render_graph, RenderTargets textures, LightResources light_resources,
            ShadowResources shadow_resources, Vector2Int attachment_size)
        {
            using var builder = render_graph.AddRasterRenderPass<ShadowMaskPass>(sampler.name, out var pass, sampler);

            if (material == null) material = CoreUtils.CreateEngineMaterial(Shader.Find("YutrelRP/ShadowMask"));

            var shadow_mask_desc = new TextureDesc(attachment_size.x, attachment_size.y)
            {
                colorFormat = GraphicsFormatUtility.GetGraphicsFormat(RenderTextureFormat.RFloat, true),
                clearBuffer = true,
                clearColor = Color.white,
                name = "Shadow Mask"
            };
            textures.shadow_mask = render_graph.CreateTexture(shadow_mask_desc);

            pass.directional_shadow_atlas = shadow_resources.directional_atlas;
            pass.scene_depth = textures.scene_depth;
            pass.directional_light_data_buffer = light_resources.directional_light_data_buffer;
            pass.directional_shadow_vp_matrices_buffer = shadow_resources.directional_vp_matrices_buffer;

            builder.UseTexture(pass.directional_shadow_atlas);
            builder.UseTexture(pass.scene_depth);
            builder.UseBuffer(pass.directional_light_data_buffer);
            builder.UseBuffer(pass.directional_shadow_vp_matrices_buffer);
            builder.SetRenderAttachment(textures.shadow_mask, 0);

            builder.SetRenderFunc<ShadowMaskPass>(static (pass, context) => { pass.Render(context); });
        }

        private void Render(RasterGraphContext context)
        {
            var cmd = context.cmd;

            material.SetTexture(Shader.PropertyToID("_DirectionalShadowAtlas"), directional_shadow_atlas);
            material.SetTexture(Shader.PropertyToID("_SceneDepth"), scene_depth);
            material.SetBuffer(Shader.PropertyToID("_DirectionalLightData"), directional_light_data_buffer);
            material.SetBuffer(Shader.PropertyToID("_DirectionalShadowVPMatrices"),
                directional_shadow_vp_matrices_buffer);

            CoreUtils.DrawFullScreen(cmd, material);
        }
    }
}