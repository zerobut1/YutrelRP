using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using Unity.Collections;

namespace YutrelRP
{
    internal class SetupLightPass
    {
        private static readonly ProfilingSampler sampler = new("Light Data Pass");

        // Data
        private int
            directional_light_count_Id,
            directional_light_data_Id,
            brdf_lut_Id;

        private int directional_light_count;

        private LightResources.DirectionalLightData[] directional_light_data;

        private BufferHandle directional_light_data_buffer;

        private TextureHandle BRDF_LUT;

        private void Render(ComputeGraphContext context)
        {
            var cmd = context.cmd;

            cmd.SetGlobalInt(directional_light_count_Id, directional_light_count);
            cmd.SetBufferData(directional_light_data_buffer, directional_light_data, 0, 0, directional_light_count);
            cmd.SetGlobalBuffer(directional_light_data_Id, directional_light_data_buffer);
            cmd.SetGlobalTexture(brdf_lut_Id, BRDF_LUT);
        }

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
            pass.BRDF_LUT = light_resources.BRDF_LUT;

            // -------------- Shadow --------------
            shadow_resources.Setup(render_graph, builder, culling_results, settings.shadowSettings);

            // ------------------------------------
            builder.AllowGlobalStateModification(true);

            builder.SetRenderFunc<SetupLightPass>(static (pass, context) => { pass.Render(context); });
        }
    }
}