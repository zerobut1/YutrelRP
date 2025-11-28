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

        internal static void Record(RenderGraph render_graph, RenderTargets textures, LightResources light_resources,
            ShadowResources shadow_resources, ShadowSettings shadow_settings, Vector2Int attachment_size)
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

            pass.directional_shadow_cascade_count_ID = ShadowResources.directional_cascade_count_ID;
            pass.directional_shadow_distance_ID = ShadowResources.directional_distance_ID;
            pass.directional_shadow_atlas_ID = ShadowResources.directional_shadow_atlas_ID;
            pass.scene_depth_ID = RenderTargets.scene_depth_ID;
            pass.directional_light_data_ID = LightResources.directional_light_data_ID;
            pass.directional_shadow_vp_matrices_ID = ShadowResources.directional_vp_matrices_ID;
            pass.directional_shadow_cascade_data_ID = ShadowResources.directional_cascade_data_ID;

            pass.directional_shadow_cascade_count = shadow_settings.directional.cascade_count;
            pass.directional_shadow_distance = shadow_settings.max_distance;
            pass.directional_shadow_atlas = shadow_resources.directional_atlas;
            pass.scene_depth = textures.scene_depth;
            pass.directional_light_data_buffer = light_resources.directional_light_data_buffer;
            pass.directional_shadow_vp_matrices_buffer = shadow_resources.directional_vp_matrices_buffer;
            pass.directional_shadow_cascade_data_buffer = shadow_resources.directional_cascade_data_buffer;

            builder.UseTexture(pass.directional_shadow_atlas);
            builder.UseTexture(pass.scene_depth);
            builder.UseBuffer(pass.directional_light_data_buffer);
            builder.UseBuffer(pass.directional_shadow_vp_matrices_buffer);
            builder.UseBuffer(pass.directional_shadow_cascade_data_buffer);
            builder.SetRenderAttachment(textures.shadow_mask, 0);

            builder.SetRenderFunc<ShadowMaskPass>(static (pass, context) => { pass.Render(context); });
        }

        // data
        private int
            directional_shadow_cascade_count_ID,
            directional_shadow_distance_ID,
            directional_shadow_atlas_ID,
            scene_depth_ID,
            directional_light_data_ID,
            directional_shadow_vp_matrices_ID,
            directional_shadow_cascade_data_ID;

        private int directional_shadow_cascade_count;
        private float directional_shadow_distance;

        private TextureHandle
            directional_shadow_atlas,
            scene_depth;

        private BufferHandle
            directional_light_data_buffer,
            directional_shadow_vp_matrices_buffer,
            directional_shadow_cascade_data_buffer;

        private void Render(RasterGraphContext context)
        {
            var cmd = context.cmd;

            material.SetInteger(directional_shadow_cascade_count_ID, directional_shadow_cascade_count);
            material.SetFloat(directional_shadow_distance_ID, directional_shadow_distance);
            material.SetTexture(directional_shadow_atlas_ID, directional_shadow_atlas);
            material.SetTexture(scene_depth_ID, scene_depth);
            material.SetBuffer(directional_light_data_ID, directional_light_data_buffer);
            material.SetBuffer(directional_shadow_vp_matrices_ID, directional_shadow_vp_matrices_buffer);
            material.SetBuffer(directional_shadow_cascade_data_ID, directional_shadow_cascade_data_buffer);

            CoreUtils.DrawFullScreen(cmd, material);
        }
    }
}