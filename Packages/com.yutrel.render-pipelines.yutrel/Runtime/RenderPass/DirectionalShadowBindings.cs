using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace YutrelRP
{
    // Immutable camera snapshot shared by screen-space and surface-local receivers.
    internal readonly struct DirectionalShadowBindings
    {
        private static readonly string[] filter_keywords =
        {
            "_DIRECTIONAL_SHADOW_FILTER_NONE", "_DIRECTIONAL_SHADOW_FILTER_LOW",
            "_DIRECTIONAL_SHADOW_FILTER_MEDIUM", "_DIRECTIONAL_SHADOW_FILTER_HIGH"
        };

        private readonly TextureHandle atlas;
        private readonly BufferHandle matrices, cascades;
        private readonly int cascade_count, filter_index;
        private readonly Vector4 texel_size, distance_fade;
        internal bool Available => cascade_count > 0;

        internal DirectionalShadowBindings(RenderGraph graph, ShadowResources resources,
            ResolvedShadowSettings settings)
        {
            bool available = resources.shadowed_directional_light_count > 0 && resources.directional_atlas.IsValid();
            cascade_count = available ? settings.directional.cascade_count : 0;
            atlas = available ? resources.directional_atlas : graph.defaultResources.defaultShadowTexture;
            // SetupLightPass allocates and initializes these for every camera, even without casters.
            matrices = resources.directional_vp_matrices_buffer;
            cascades = resources.directional_cascade_data_buffer;
            int width = (int)settings.directional.atlas_tile_size;
            int height = width * settings.directional.cascade_count;
            texel_size = available ? new Vector4(1.0f / width, 1.0f / height, width, height) : Vector4.one;
            distance_fade = GetDistanceFade(settings);
            var quality = available && resources.directional_soft_shadow
                ? settings.directional.soft_shadow_quality : ShadowSettings.Directional.SoftShadowQuality.None;
            filter_index = quality switch
            {
                ShadowSettings.Directional.SoftShadowQuality.Low => 1,
                ShadowSettings.Directional.SoftShadowQuality.Medium => 2,
                ShadowSettings.Directional.SoftShadowQuality.High => 3,
                _ => 0
            };
        }

        internal void DeclareResources(IBaseRenderGraphBuilder builder)
        {
            builder.UseTexture(atlas);
            builder.UseBuffer(matrices);
            builder.UseBuffer(cascades);
        }

        internal void BindGlobals(IBaseCommandBuffer cmd)
        {
            cmd.SetGlobalInt(ShadowResources.directional_cascade_count_ID, cascade_count);
            cmd.SetGlobalVector(ShadowResources.directional_distance_fade_ID, distance_fade);
            cmd.SetGlobalVector(ShadowResources.directional_atlas_texel_size_ID, texel_size);
            cmd.SetGlobalTexture(ShadowResources.directional_shadow_atlas_ID, atlas);
            cmd.SetGlobalBuffer(ShadowResources.directional_vp_matrices_ID, matrices);
            cmd.SetGlobalBuffer(ShadowResources.directional_cascade_data_ID, cascades);
            for (int i = 0; i < filter_keywords.Length; i++)
            {
                if (i == filter_index) cmd.EnableShaderKeyword(filter_keywords[i]);
                else cmd.DisableShaderKeyword(filter_keywords[i]);
            }
        }

        internal void BindMaterial(Material material)
        {
            material.SetInteger(ShadowResources.directional_cascade_count_ID, cascade_count);
            material.SetVector(ShadowResources.directional_distance_fade_ID, distance_fade);
            material.SetVector(ShadowResources.directional_atlas_texel_size_ID, texel_size);
            material.SetTexture(ShadowResources.directional_shadow_atlas_ID, atlas);
            material.SetBuffer(ShadowResources.directional_vp_matrices_ID, matrices);
            material.SetBuffer(ShadowResources.directional_cascade_data_ID, cascades);
            for (int i = 0; i < filter_keywords.Length; i++)
            {
                if (i == filter_index) material.EnableKeyword(filter_keywords[i]);
                else material.DisableKeyword(filter_keywords[i]);
            }
        }

        internal static Vector4 GetDistanceFade(ResolvedShadowSettings settings)
        {
            float fade = Mathf.Clamp(settings.directional.cascade_fade, ShadowSettings.MinCascadeFade, 1.0f);
            float one_minus_fade = 1.0f - fade;
            return new Vector4(1.0f / settings.max_distance, 1.0f / settings.distance_fade,
                1.0f / Mathf.Max(ShadowSettings.MinCascadeFade, 1.0f - one_minus_fade * one_minus_fade), 0.0f);
        }
    }
}
