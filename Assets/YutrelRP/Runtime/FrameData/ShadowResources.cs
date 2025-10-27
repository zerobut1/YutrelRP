using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace YutrelRP
{
    public class ShadowResources : ContextItem
    {
        private const int max_shadowed_directional_light_count = 1;

        public struct ShadowedDirectionalLight
        {
            public int visible_light_index;
        }

        public int shadowed_directional_light_count;
        private ShadowedDirectionalLight[] shadowed_directional_Lights;

        public TextureHandle directional_atlas;

        public struct RenderInfo
        {
            public RendererListHandle renderer_list;

            public Matrix4x4 view, projection;
        }

        public RenderInfo[] directional_render_info;

        private int directional_atlas_tile_size;

        public override void Reset()
        {
            shadowed_directional_light_count = 0;
            shadowed_directional_Lights = new ShadowedDirectionalLight[max_shadowed_directional_light_count];
            directional_atlas = TextureHandle.nullHandle;
            directional_render_info = new RenderInfo[max_shadowed_directional_light_count];
        }

        public void ReserveDirectionalShadows(Light light, int visible_light_index, CullingResults culling_results)
        {
            if (shadowed_directional_light_count < max_shadowed_directional_light_count
                && light.shadows != LightShadows.None
                && light.shadowStrength > 0.0f
                && culling_results.GetShadowCasterBounds(visible_light_index, out var bounds)
               )
            {
                shadowed_directional_Lights[shadowed_directional_light_count++] = new ShadowedDirectionalLight
                {
                    visible_light_index = visible_light_index
                };
            }
        }

        public void Setup(RenderGraph render_graph, IComputeRenderGraphBuilder builder, CullingResults culling_results,
            ShadowSettings settings)
        {
            directional_atlas_tile_size = (int)settings.directional.atlas_size;

            var desc = new TextureDesc(directional_atlas_tile_size, directional_atlas_tile_size)
            {
                depthBufferBits = DepthBits.Depth32,
                isShadowMap = true,
                name = "Directional Shadow Atlas",
            };
            if (shadowed_directional_light_count > 0)
            {
                directional_atlas = render_graph.CreateTexture(desc);
                builder.UseTexture(directional_atlas, AccessFlags.WriteAll);
            }
            else
            {
                directional_atlas = render_graph.defaultResources.defaultShadowTexture;
            }

            BuildRendererList(render_graph, builder, culling_results);
        }

        private void BuildRendererList(RenderGraph render_graph, IComputeRenderGraphBuilder builder,
            CullingResults culling_results)
        {
            if (shadowed_directional_light_count > 0)
            {
                for (int i = 0; i < shadowed_directional_light_count; i++)
                {
                    BuildDirectionalRendererList(i, render_graph, builder, culling_results);
                }
            }
        }

        private void BuildDirectionalRendererList(int index, RenderGraph render_graph,
            IComputeRenderGraphBuilder builder, CullingResults culling_results)
        {
            ShadowedDirectionalLight light = shadowed_directional_Lights[index];
            var shadow_settings = new ShadowDrawingSettings(culling_results, light.visible_light_index)
            {
                useRenderingLayerMaskTest = true,
            };

            ref var render_info = ref directional_render_info[index];
            culling_results.ComputeDirectionalShadowMatricesAndCullingPrimitives(
                light.visible_light_index,
                0,
                1,
                Vector3.zero,
                directional_atlas_tile_size,
                0.0f,
                out render_info.view,
                out render_info.projection,
                out var split_data
            );
            render_info.renderer_list = render_graph.CreateShadowRendererList(ref shadow_settings);
        }
    };
}