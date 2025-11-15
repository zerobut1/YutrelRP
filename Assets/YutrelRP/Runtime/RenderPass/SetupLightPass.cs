using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using Unity.Collections;

namespace YutrelRP
{
    internal class SetupLightPass
    {
        private static readonly ProfilingSampler sampler = new("Light Data Pass");

        internal static void Record(RenderGraph render_graph, CullingResults culling_results, YutrelRPSettings settings,
            ref LightResources light_resources, ref ShadowResources shadow_resources)
        {
            using var builder = render_graph.AddComputePass<SetupLightPass>(sampler.name, out var pass, sampler);

            // -------------- Light --------------
            light_resources.Setup(render_graph, builder, culling_results, settings.BRDF_LUT, ref shadow_resources);

            pass.directional_light_count_Id = LightResources.directional_light_count_Id;
            pass.directional_light_data_Id = LightResources.directional_light_data_Id;
            pass.brdf_lut_Id = LightResources.brdf_lut_Id;
            pass.directional_light_count = light_resources.directional_light_count;
            pass.directional_light_data = light_resources.directional_light_data;
            pass.directional_light_data_buffer = light_resources.directional_light_data_buffer;

            // -------------- Shadow --------------
            shadow_resources.Setup(render_graph, builder, culling_results, settings.shadowSettings);

            var shadow_settings = settings.shadowSettings;
            var render_info = shadow_resources.directional_render_info[0];

            pass.shadow_directional_light_count = shadow_resources.shadowed_directional_light_count;
            pass.shadow_directional_vp_matrices_Id = ShadowResources.directional_vp_matrices_Id;
            pass.shadow_directional_vp_matrices_buffer = shadow_resources.directional_vp_matrices_buffer;
            pass.shadow_directional_vp_matrices = new Matrix4x4[shadow_settings.directional.cascade_count];
            pass.shadow_directional_vp_matrices[0] = ConvertToAtlasMatrix(render_info.projection * render_info.view);

            // ------------------------------------

            builder.SetRenderFunc<SetupLightPass>(static (pass, context) => { pass.Render(context); });
        }

        // Data
        // ------------- Light -------------
        private int
            directional_light_count_Id,
            directional_light_data_Id,
            brdf_lut_Id;

        private int directional_light_count;

        private LightResources.DirectionalLightData[] directional_light_data;

        private BufferHandle directional_light_data_buffer;

        private TextureHandle BRDF_LUT;

        // ------------ Shadow -------------
        private int shadow_directional_vp_matrices_Id;

        private int shadow_directional_light_count;

        private Matrix4x4[] shadow_directional_vp_matrices;

        private BufferHandle shadow_directional_vp_matrices_buffer;

        private void Render(ComputeGraphContext context)
        {
            var cmd = context.cmd;

            // Light
            cmd.SetBufferData(directional_light_data_buffer, directional_light_data, 0, 0, directional_light_count);

            // Shadow
            cmd.SetBufferData(shadow_directional_vp_matrices_buffer, shadow_directional_vp_matrices, 0, 0,
                shadow_directional_light_count);
        }

        private static Matrix4x4 ConvertToAtlasMatrix(Matrix4x4 mat)
        {
            if (SystemInfo.usesReversedZBuffer)
            {
                mat.m20 = -mat.m20;
                mat.m21 = -mat.m21;
                mat.m22 = -mat.m22;
                mat.m23 = -mat.m23;
            }

            mat.m00 = 0.5f * (mat.m00 + mat.m30);
            mat.m01 = 0.5f * (mat.m01 + mat.m31);
            mat.m02 = 0.5f * (mat.m02 + mat.m32);
            mat.m03 = 0.5f * (mat.m03 + mat.m33);
            mat.m10 = 0.5f * (mat.m10 + mat.m30);
            mat.m11 = 0.5f * (mat.m11 + mat.m31);
            mat.m12 = 0.5f * (mat.m12 + mat.m32);
            mat.m13 = 0.5f * (mat.m13 + mat.m33);
            mat.m20 = 0.5f * (mat.m20 + mat.m30);
            mat.m21 = 0.5f * (mat.m21 + mat.m31);
            mat.m22 = 0.5f * (mat.m22 + mat.m32);
            mat.m23 = 0.5f * (mat.m23 + mat.m33);

            return mat;
        }
    }
}