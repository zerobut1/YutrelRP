using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using System.Runtime.InteropServices;
using Unity.Collections;

namespace YutrelRP
{
    internal class SetupLightPass
    {
        private static readonly ProfilingSampler sampler = new("Light Data Pass");

        static readonly int
            brdf_lut_Id = Shader.PropertyToID("_BRDF_LUT"),
            directional_light_count_Id = Shader.PropertyToID("_DirectionalLightCount"),
            directional_light_data_Id = Shader.PropertyToID("_DirectionalLightData");

        // Directional Light
        [StructLayout(LayoutKind.Sequential)]
        struct DirectionalLightData
        {
            public const int stride = 4 * 4 * 2;

            public Vector3 color;
            public float intensity;
            public Vector4 direction;

            public DirectionalLightData(ref VisibleLight visiable_light)
            {
                color.x = visiable_light.light.color.r;
                color.y = visiable_light.light.color.g;
                color.z = visiable_light.light.color.b;
                intensity = visiable_light.light.intensity;
                direction = -visiable_light.localToWorldMatrix.GetColumn(2);
            }
        }

        private const int max_directional_light_count = 1;

        static readonly DirectionalLightData[] directional_light_data =
            new DirectionalLightData[max_directional_light_count];

        int directional_light_count = 0;

        // Data
        BufferHandle directional_light_data_buffer;

        TextureHandle BRDF_LUT;

        void Setup(CullingResults culling_results)
        {
            NativeArray<VisibleLight> visible_lights = culling_results.visibleLights;

            directional_light_count = 0;
            for (int i = 0; i < visible_lights.Length; i++)
            {
                VisibleLight visible_light = visible_lights[i];
                switch (visible_light.lightType)
                {
                    case LightType.Directional:
                        if (directional_light_count < max_directional_light_count)
                        {
                            directional_light_data[directional_light_count++] =
                                new DirectionalLightData(ref visible_light);
                        }

                        break;
                }
            }
        }

        private void Render(ComputeGraphContext context)
        {
            var cmd = context.cmd;

            cmd.SetGlobalInt(directional_light_count_Id, directional_light_count);
            cmd.SetBufferData(directional_light_data_buffer, directional_light_data, 0, 0, directional_light_count);
            cmd.SetGlobalBuffer(directional_light_data_Id, directional_light_data_buffer);
            cmd.SetGlobalTexture(brdf_lut_Id, BRDF_LUT);
        }

        internal static void Record(RenderGraph render_graph, CullingResults culling_results, YutrelRPSettings settings,
            ref LightResources light_resources)
        {
            using var builder = render_graph.AddComputePass<SetupLightPass>(sampler.name, out var pass, sampler);

            pass.Setup(culling_results);

            pass.directional_light_data_buffer = render_graph.CreateBuffer(
                new BufferDesc(max_directional_light_count, DirectionalLightData.stride)
                {
                    name = "Directional Light Data"
                });
            builder.UseBuffer(pass.directional_light_data_buffer, AccessFlags.WriteAll);

            pass.BRDF_LUT = render_graph.ImportTexture(RTHandles.Alloc(settings.BRDF_LUT));
            builder.UseTexture(pass.BRDF_LUT);

            builder.AllowPassCulling(false);
            builder.AllowGlobalStateModification(true);

            builder.SetRenderFunc<SetupLightPass>(static (pass, context) => { pass.Render(context); });

            light_resources.directional_light_data_buffer = pass.directional_light_data_buffer;
            light_resources.brdf_lut_Id = brdf_lut_Id;
            light_resources.BRDF_LUT = pass.BRDF_LUT;
        }
    }
}