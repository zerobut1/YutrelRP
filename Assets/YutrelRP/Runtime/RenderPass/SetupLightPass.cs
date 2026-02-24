using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using Unity.Collections;

namespace YutrelRP
{
    internal class SetupLightPass
    {
        private static readonly ProfilingSampler sampler = new("Light Data Pass");

        internal static void Record(RenderGraph render_graph, ScriptableRenderContext context,
            CullingResults culling_results, YutrelRPSettings settings, ref LightResources light_resources,
            ref ShadowResources shadow_resources)
        {
            using var builder = render_graph.AddComputePass<SetupLightPass>(sampler.name, out var pass, sampler);

            // -------------- Light --------------
            light_resources.Setup(render_graph, builder, culling_results, settings.BRDF_LUT, ref shadow_resources);

            pass.directional_light_count = light_resources.directional_light_count;
            pass.directional_light_data = light_resources.directional_light_data;
            pass.directional_light_data_buffer = light_resources.directional_light_data_buffer;

            // -------------- Shadow --------------
            shadow_resources.Setup(render_graph, builder, culling_results, settings.shadowSettings, context);

            var shadow_settings = settings.shadowSettings;

            pass.shadow_cascade_count = shadow_settings.directional.cascade_count;
            pass.shadow_directional_vp_matrices_buffer = shadow_resources.directional_vp_matrices_buffer;
            pass.shadow_directional_vp_matrices =
                new Matrix4x4[shadow_settings.directional.cascade_count];
            for (int cascade_index = 0; cascade_index < pass.shadow_cascade_count; cascade_index++)
            {
                var render_info = shadow_resources.directional_render_info[cascade_index];

                pass.shadow_directional_vp_matrices[cascade_index] =
                    ConvertToAtlasMatrix(render_info.projection * render_info.view,
                        new Vector2(0.0f, cascade_index),
                        new Vector2(1.0f, 1.0f / pass.shadow_cascade_count));
            }

            pass.shadow_directional_cascade_data_buffer = shadow_resources.directional_cascade_data_buffer;
            pass.shadow_directional_cascade_data = shadow_resources.directional_cascade_data;

            // ------------------------------------

            builder.SetRenderFunc<SetupLightPass>(static (pass, context) => { pass.Render(context); });
        }

        // Data
        // ------------- Light -------------
        private int directional_light_count;

        private LightResources.DirectionalLightData[] directional_light_data;

        private BufferHandle directional_light_data_buffer;

        private TextureHandle BRDF_LUT;

        // ------------ Shadow -------------
        private int shadow_cascade_count;

        private Matrix4x4[] shadow_directional_vp_matrices;
        private ShadowResources.DirectionalShadowCascadeData[] shadow_directional_cascade_data;

        private BufferHandle shadow_directional_vp_matrices_buffer;
        private BufferHandle shadow_directional_cascade_data_buffer;

        private void Render(ComputeGraphContext context)
        {
            var cmd = context.cmd;

            // Light
            cmd.SetBufferData(directional_light_data_buffer, directional_light_data, 0, 0, directional_light_count);

            // Shadow
            cmd.SetBufferData(shadow_directional_vp_matrices_buffer, shadow_directional_vp_matrices, 0, 0,
                shadow_cascade_count);
            cmd.SetBufferData(shadow_directional_cascade_data_buffer, shadow_directional_cascade_data, 0, 0,
                shadow_cascade_count);
        }

        private static Matrix4x4 ConvertToAtlasMatrix(Matrix4x4 m, Vector2 offset, Vector2 scale)
        {
            if (SystemInfo.usesReversedZBuffer)
            {
                m.m20 = -m.m20;
                m.m21 = -m.m21;
                m.m22 = -m.m22;
                m.m23 = -m.m23;
            }

            m.m00 = (0.5f * (m.m00 + m.m30) + offset.x * m.m30) * scale.x;
            m.m01 = (0.5f * (m.m01 + m.m31) + offset.x * m.m31) * scale.x;
            m.m02 = (0.5f * (m.m02 + m.m32) + offset.x * m.m32) * scale.x;
            m.m03 = (0.5f * (m.m03 + m.m33) + offset.x * m.m33) * scale.x;
            m.m10 = (0.5f * (m.m10 + m.m30) + offset.y * m.m30) * scale.y;
            m.m11 = (0.5f * (m.m11 + m.m31) + offset.y * m.m31) * scale.y;
            m.m12 = (0.5f * (m.m12 + m.m32) + offset.y * m.m32) * scale.y;
            m.m13 = (0.5f * (m.m13 + m.m33) + offset.y * m.m33) * scale.y;
            m.m20 = 0.5f * (m.m20 + m.m30);
            m.m21 = 0.5f * (m.m21 + m.m31);
            m.m22 = 0.5f * (m.m22 + m.m32);
            m.m23 = 0.5f * (m.m23 + m.m33);

            return m;
        }
    }
}
