using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace YutrelRP
{
    internal class ShadowMaskPass
    {
        private static readonly ProfilingSampler sampler = new("Shadow Mask Pass");
        private const string none_filter_keyword = "_DIRECTIONAL_SHADOW_FILTER_NONE";
        private const string low_filter_keyword = "_DIRECTIONAL_SHADOW_FILTER_LOW";
        private const string medium_filter_keyword = "_DIRECTIONAL_SHADOW_FILTER_MEDIUM";
        private const string high_filter_keyword = "_DIRECTIONAL_SHADOW_FILTER_HIGH";
        private static Material material;

        internal static void Record(RenderGraph render_graph, RenderTargets textures, LightResources light_resources,
            ShadowResources shadow_resources, ResolvedShadowSettings shadow_settings, Vector2Int attachment_size)
        {
            if (light_resources.directional_light_count == 0) return;
            if (material == null) material = CoreUtils.CreateEngineMaterial(Shader.Find("YutrelRP/ShadowMask"));

            if (shadow_resources.shadowed_directional_light_count == 0)
            {
                textures.shadow_mask = render_graph.defaultResources.whiteTexture;
                return;
            }

            using var builder = render_graph.AddRasterRenderPass<ShadowMaskPass>(sampler.name, out var pass, sampler);

            var shadow_mask_desc = new TextureDesc(attachment_size.x, attachment_size.y)
            {
                colorFormat = GraphicsFormatUtility.GetGraphicsFormat(RenderTextureFormat.RFloat, true),
                clearBuffer = true,
                clearColor = Color.white,
                name = "Shadow Mask"
            };
            textures.shadow_mask = render_graph.CreateTexture(shadow_mask_desc);

            pass.directional_shadow_cascade_count_ID = ShadowResources.directional_cascade_count_ID;
            pass.directional_shadow_distance_fade_ID = ShadowResources.directional_distance_fade_ID;
            pass.directional_shadow_atlas_texel_size_ID = ShadowResources.directional_atlas_texel_size_ID;
            pass.directional_shadow_atlas_ID = ShadowResources.directional_shadow_atlas_ID;
            pass.scene_depth_ID = RenderTargets.scene_depth_ID;
            pass.GBuffer_B_ID = RenderTargets.GBuffer_B_ID;
            pass.directional_light_data_ID = LightResources.directional_light_data_ID;
            pass.directional_shadow_vp_matrices_ID = ShadowResources.directional_vp_matrices_ID;
            pass.directional_shadow_cascade_data_ID = ShadowResources.directional_cascade_data_ID;

            pass.directional_shadow_cascade_count = shadow_settings.directional.cascade_count;
            int directional_atlas_width = (int)shadow_settings.directional.atlas_tile_size;
            int directional_atlas_height = directional_atlas_width * pass.directional_shadow_cascade_count;
            pass.directional_shadow_atlas_texel_size =
                new Vector4(
                    1.0f / directional_atlas_width,
                    1.0f / directional_atlas_height,
                    directional_atlas_width,
                    directional_atlas_height);
            pass.directional_shadow_distance_fade =
                GetDirectionalShadowDistanceFade(shadow_settings);
            pass.soft_shadow_quality = GetEffectiveSoftShadowQuality(
                shadow_settings.directional.soft_shadow_quality,
                shadow_resources.directional_soft_shadow);

            pass.directional_shadow_atlas = shadow_resources.directional_atlas;
            pass.scene_depth = textures.scene_depth;
            pass.GBuffer_B = textures.GBuffer_B;
            pass.directional_light_data_buffer = light_resources.directional_light_data_buffer;
            pass.directional_shadow_vp_matrices_buffer = shadow_resources.directional_vp_matrices_buffer;
            pass.directional_shadow_cascade_data_buffer = shadow_resources.directional_cascade_data_buffer;

            builder.UseTexture(pass.directional_shadow_atlas);
            builder.UseTexture(pass.scene_depth);
            builder.UseTexture(pass.GBuffer_B);
            builder.UseBuffer(pass.directional_light_data_buffer);
            builder.UseBuffer(pass.directional_shadow_vp_matrices_buffer);
            builder.UseBuffer(pass.directional_shadow_cascade_data_buffer);
            builder.SetRenderAttachment(textures.shadow_mask, 0);

            builder.SetRenderFunc<ShadowMaskPass>(static (pass, context) => { pass.Render(context); });
        }

        // data
        private int
            directional_shadow_cascade_count_ID,
            directional_shadow_distance_fade_ID,
            directional_shadow_atlas_texel_size_ID,
            directional_shadow_atlas_ID,
            scene_depth_ID,
            GBuffer_B_ID,
            directional_light_data_ID,
            directional_shadow_vp_matrices_ID,
            directional_shadow_cascade_data_ID;

        private int directional_shadow_cascade_count;
        private Vector4 directional_shadow_distance_fade;
        private Vector4 directional_shadow_atlas_texel_size;
        private ShadowSettings.Directional.SoftShadowQuality soft_shadow_quality;

        private TextureHandle
            directional_shadow_atlas,
            scene_depth,
            GBuffer_B;

        private BufferHandle
            directional_light_data_buffer,
            directional_shadow_vp_matrices_buffer,
            directional_shadow_cascade_data_buffer;

        private void Render(RasterGraphContext context)
        {
            var cmd = context.cmd;

            material.SetInteger(directional_shadow_cascade_count_ID, directional_shadow_cascade_count);
            material.SetVector(directional_shadow_distance_fade_ID, directional_shadow_distance_fade);
            material.SetVector(directional_shadow_atlas_texel_size_ID, directional_shadow_atlas_texel_size);
            material.SetTexture(directional_shadow_atlas_ID, directional_shadow_atlas);
            material.SetTexture(scene_depth_ID, scene_depth);
            material.SetTexture(GBuffer_B_ID, GBuffer_B);
            material.SetBuffer(directional_light_data_ID, directional_light_data_buffer);
            material.SetBuffer(directional_shadow_vp_matrices_ID, directional_shadow_vp_matrices_buffer);
            material.SetBuffer(directional_shadow_cascade_data_ID, directional_shadow_cascade_data_buffer);
            SetSoftShadowQualityKeywords(soft_shadow_quality);

            CoreUtils.DrawFullScreen(cmd, material);
        }

        private static ShadowSettings.Directional.SoftShadowQuality GetEffectiveSoftShadowQuality(
            ShadowSettings.Directional.SoftShadowQuality quality, bool light_uses_soft_shadows)
        {
            return light_uses_soft_shadows ? quality : ShadowSettings.Directional.SoftShadowQuality.None;
        }

        internal static Vector4 GetDirectionalShadowDistanceFade(ResolvedShadowSettings shadow_settings)
        {
            float cascade_fade = Mathf.Clamp(shadow_settings.directional.cascade_fade, ShadowSettings.MinCascadeFade, 1.0f);
            float one_minus_fade = 1.0f - cascade_fade;
            return new Vector4(
                1.0f / shadow_settings.max_distance,
                1.0f / shadow_settings.distance_fade,
                1.0f / Mathf.Max(ShadowSettings.MinCascadeFade, 1.0f - one_minus_fade * one_minus_fade),
                0.0f);
        }

        private static void SetSoftShadowQualityKeywords(ShadowSettings.Directional.SoftShadowQuality quality)
        {
            material.DisableKeyword(none_filter_keyword);
            material.DisableKeyword(low_filter_keyword);
            material.DisableKeyword(medium_filter_keyword);
            material.DisableKeyword(high_filter_keyword);

            if (quality == ShadowSettings.Directional.SoftShadowQuality.None)
            {
                material.EnableKeyword(none_filter_keyword);
            }
            else if (quality == ShadowSettings.Directional.SoftShadowQuality.Low)
            {
                material.EnableKeyword(low_filter_keyword);
            }
            else if (quality == ShadowSettings.Directional.SoftShadowQuality.Medium)
            {
                material.EnableKeyword(medium_filter_keyword);
            }
            else if (quality == ShadowSettings.Directional.SoftShadowQuality.High)
            {
                material.EnableKeyword(high_filter_keyword);
            }
        }

        public static void Cleanup()
        {
            CoreUtils.Destroy(material);
            material = null;
        }
    }
}
