using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using Unity.Collections;

namespace YutrelRP
{
    public class ShadowResources : ContextItem
    {
        public static readonly int
            directional_cascade_count_ID = Shader.PropertyToID("_DirectionalShadowCascadeCount"),
            directional_distance_fade_ID = Shader.PropertyToID("_DirectionalShadowDistanceFade"),
            directional_shadow_atlas_ID = Shader.PropertyToID("_DirectionalShadowAtlas"),
            directional_vp_matrices_ID = Shader.PropertyToID("_DirectionalShadowVPMatrices"),
            directional_cascade_data_ID = Shader.PropertyToID("_DirectionalShadowCascadeDatas");

        public const int max_shadowed_directional_light_count = 1;
        public const int max_cascades = 4;

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

        public BufferHandle directional_vp_matrices_buffer;

        public struct DirectionalShadowCascadeData
        {
            public const int stride = 4 * 4 * 1;

            public Vector4 culling_sphere;

            public DirectionalShadowCascadeData(
                Vector4 culling_sphere)
            {
                this.culling_sphere = culling_sphere;
            }
        }

        public DirectionalShadowCascadeData[] directional_cascade_data;

        public BufferHandle directional_cascade_data_buffer;
        private NativeArray<LightShadowCasterCullingInfo> culling_infos_per_light;
        private NativeArray<ShadowSplitData> split_buffer;

        public ShadowResources()
        {
            shadowed_directional_Lights = new ShadowedDirectionalLight[max_shadowed_directional_light_count];
            directional_render_info = new RenderInfo[max_shadowed_directional_light_count * max_cascades];
            directional_cascade_data = new DirectionalShadowCascadeData[max_shadowed_directional_light_count * max_cascades];
        }

        public override void Reset()
        {
            if (culling_infos_per_light.IsCreated)
            {
                culling_infos_per_light.Dispose();
            }
            if (split_buffer.IsCreated)
            {
                split_buffer.Dispose();
            }

            shadowed_directional_light_count = 0;
            Array.Clear(shadowed_directional_Lights, 0, shadowed_directional_Lights.Length);
            directional_atlas = TextureHandle.nullHandle;
            Array.Clear(directional_render_info, 0, directional_render_info.Length);
            Array.Clear(directional_cascade_data, 0, directional_cascade_data.Length);
        }

        public Vector4 ReserveDirectionalShadows(Light light, int visible_light_index, CullingResults culling_results)
        {
            if (shadowed_directional_light_count < max_shadowed_directional_light_count
                && light.shadows != LightShadows.None
                && light.shadowStrength > 0.0f
                && culling_results.GetShadowCasterBounds(visible_light_index, out var bounds)
               )
            {
                shadowed_directional_Lights[shadowed_directional_light_count] = new ShadowedDirectionalLight
                {
                    visible_light_index = visible_light_index
                };

                return new Vector4(shadowed_directional_light_count++, 0, 0, 0);
            }

            return new Vector4(-1, 0, 0, 0);
        }

        public void Setup(RenderGraph render_graph, IComputeRenderGraphBuilder builder, CullingResults culling_results,
            ShadowSettings settings, ScriptableRenderContext context)
        {
            directional_atlas_tile_size = (int)settings.directional.atlas_tile_size;

            // shadow atlas
            var desc = new TextureDesc(directional_atlas_tile_size,
                directional_atlas_tile_size * settings.directional.cascade_count)
            {
                depthBufferBits = DepthBits.Depth16,
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

            // shadow vp matrices
            directional_vp_matrices_buffer = render_graph.CreateBuffer(
                new BufferDesc(settings.directional.cascade_count, 16 * 4)
                {
                    name = "Directional Shadow VP Matrices"
                });
            builder.UseBuffer(directional_vp_matrices_buffer, AccessFlags.WriteAll);

            // shadow cascade data
            directional_cascade_data_buffer = render_graph.CreateBuffer(
                new BufferDesc(settings.directional.cascade_count, DirectionalShadowCascadeData.stride)
                {
                    name = "Directional Shadow Cascade Data"
                });
            builder.UseBuffer(directional_cascade_data_buffer, AccessFlags.WriteAll);

            int visible_light_count = culling_results.visibleLights.Length;
            culling_infos_per_light = new NativeArray<LightShadowCasterCullingInfo>(visible_light_count, Allocator.TempJob);
            split_buffer = new NativeArray<ShadowSplitData>(visible_light_count * max_cascades, Allocator.TempJob);

            // render lists
            BuildRendererList(render_graph, builder, culling_results, settings);

            if (shadowed_directional_light_count > 0)
            {
                context.CullShadowCasters(culling_results, new ShadowCastersCullingInfos
                {
                    perLightInfos = culling_infos_per_light,
                    splitBuffer = split_buffer
                });
            }
        }

        private void BuildRendererList(RenderGraph render_graph, IComputeRenderGraphBuilder builder,
            CullingResults culling_results, ShadowSettings settings)
        {
            if (shadowed_directional_light_count > 0)
            {
                for (int i = 0; i < shadowed_directional_light_count; i++)
                {
                    BuildDirectionalRendererList(i, render_graph, builder, culling_results, settings);
                }
            }
        }

        private void BuildDirectionalRendererList(int index, RenderGraph render_graph,
            IComputeRenderGraphBuilder builder, CullingResults culling_results, ShadowSettings settings)
        {
            ShadowedDirectionalLight light = shadowed_directional_Lights[index];
            var shadow_settings = new ShadowDrawingSettings(
                culling_results,
                light.visible_light_index)
            {
                useRenderingLayerMaskTest = true,
            };

            var cascade_count = settings.directional.cascade_count;
            Vector3 cascade_ratios = settings.directional.cascade_ratios;
            int split_buffer_offset = light.visible_light_index * max_cascades;

            for (int cascade_index = 0; cascade_index < cascade_count; cascade_index++)
            {
                ref var render_info = ref directional_render_info[index * cascade_count + cascade_index];
                culling_results.ComputeDirectionalShadowMatricesAndCullingPrimitives(
                    light.visible_light_index,
                    cascade_index,
                    cascade_count,
                    cascade_ratios,
                    directional_atlas_tile_size,
                    0.0f,
                    out render_info.view,
                    out render_info.projection,
                    out var split_data
                );
                if (index == 0)
                {
                    directional_cascade_data[cascade_index] =
                        new DirectionalShadowCascadeData(split_data.cullingSphere);
                }

                split_buffer[split_buffer_offset + cascade_index] = split_data;
                shadow_settings.splitIndex = cascade_index;
                render_info.renderer_list = render_graph.CreateShadowRendererList(ref shadow_settings);
            }

            culling_infos_per_light[light.visible_light_index] = new LightShadowCasterCullingInfo
            {
                projectionType = BatchCullingProjectionType.Orthographic,
                splitRange = new RangeInt(split_buffer_offset, cascade_count)
            };
        }
    };
}
