using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace YutrelRP
{
    internal class SkyboxPass
    {
        private static readonly ProfilingSampler sampler = new("Skybox Pass");

        internal static void Record(RenderGraph render_graph, Camera camera, RenderTargets textures,
            LightResources light_resources)
        {
            if (camera.clearFlags != CameraClearFlags.Skybox || RenderSettings.skybox == null)
            {
                return;
            }

            using var builder = render_graph.AddRasterRenderPass<SkyboxPass>(sampler.name, out var pass, sampler);

            pass.list = render_graph.CreateSkyboxRendererList(camera);
            pass.environment_intensity = light_resources.environment_intensity;
            pass.environment_intensity_ID = LightResources.environment_intensity_ID;

            builder.UseRendererList(pass.list);
            builder.SetRenderAttachment(textures.scene_color, 0, AccessFlags.ReadWrite);
            builder.SetRenderAttachmentDepth(textures.scene_depth, AccessFlags.Read);

            builder.AllowPassCulling(false);
            builder.AllowGlobalStateModification(true);

            builder.SetRenderFunc<SkyboxPass>(static (pass, context) => pass.Render(context));
        }

        // data
        private RendererListHandle list;
        private int environment_intensity_ID;
        private float environment_intensity;

        private void Render(RasterGraphContext context)
        {
            context.cmd.SetGlobalFloat(environment_intensity_ID, environment_intensity);
            context.cmd.DrawRendererList(list);
        }
    }
}
