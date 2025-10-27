using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace YutrelRP
{
    internal class SkyboxPass
    {
        private static readonly ProfilingSampler sampler = new("Skybox Pass");

        internal static void Record(RenderGraph render_graph, Camera camera, RenderTargets textures)
        {
            if (camera.clearFlags != CameraClearFlags.Skybox || RenderSettings.skybox == null)
            {
                return;
            }

            using var builder = render_graph.AddRasterRenderPass<SkyboxPass>(sampler.name, out var pass, sampler);

            pass.list = render_graph.CreateSkyboxRendererList(camera);

            builder.UseRendererList(pass.list);
            builder.SetRenderAttachment(textures.scene_color, 0);
            builder.SetRenderAttachmentDepth(textures.scene_depth, AccessFlags.Read);

            builder.AllowPassCulling(false);

            builder.SetRenderFunc<SkyboxPass>(static (pass, context) => pass.Render(context));
        }

        // data
        private RendererListHandle list;

        private void Render(RasterGraphContext context)
        {
            context.cmd.DrawRendererList(list);
        }
    }
}