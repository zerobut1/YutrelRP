using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using YutrelRP.FrameData;

namespace YutrelRP
{
    internal class SetupLightPass : YutrelRenderPass
    {
        private static class ShaderLightData
        {
            internal static readonly int sun_light_direction = Shader.PropertyToID("_sun_light_direction");
            internal static readonly int sun_light_color = Shader.PropertyToID("_sun_light_color");
        }

        private class PassData
        {
            internal LightData light_data;
        }

        internal void Setup(RenderGraph render_graph, LightData light_data)
        {
            using var builder = render_graph.AddRasterRenderPass<PassData>(
                "Setup Light Pass",
                out var pass_data,
                new ProfilingSampler("Setup Light Pass"));

            pass_data.light_data = light_data;

            builder.AllowPassCulling(false);
            builder.AllowGlobalStateModification(true);

            builder.SetRenderFunc((PassData data, RasterGraphContext context) =>
            {
                VisibleLight sun_light = data.light_data.sun_light;
                Vector4 sun_light_direction = sun_light.localToWorldMatrix.MultiplyVector(Vector3.back);
                Vector4 sun_light_color = sun_light.finalColor;

                context.cmd.SetGlobalVector(ShaderLightData.sun_light_direction,
                    sun_light_direction);
                context.cmd.SetGlobalVector(ShaderLightData.sun_light_color, sun_light_color);
            });
        }
    }
}