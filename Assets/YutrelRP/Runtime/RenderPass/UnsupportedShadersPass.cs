#if UNITY_EDITOR
using System.Diagnostics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RendererUtils;
using UnityEngine.Rendering.RenderGraphModule;

namespace YutrelRP
{
    internal class UnsupportedShadersPass
    {
        private static readonly ProfilingSampler sampler = new("Unsupported Shaders Pass");

        private static readonly ShaderTagId[] shader_tag_ids =
        {
            new("Always"),
            new("ForwardBase"),
            new("PrepassBase"),
            new("Vertex"),
            new("VertexLMRGBM"),
            new("VertexLM")
        };

        private static Material error_material;

        [Conditional("UNITY_EDITOR")]
        internal static void Record(RenderGraph render_graph, Camera camera, CullingResults culling_results,
            in RenderTargets textures)
        {
            using var builder = render_graph.AddRasterRenderPass<UnsupportedShadersPass>(sampler.name, out var pass,
                sampler);

            if (error_material == null)
                error_material = CoreUtils.CreateEngineMaterial(Shader.Find("Hidden/InternalErrorShader"));

            pass.list = render_graph.CreateRendererList(
                new RendererListDesc(shader_tag_ids, culling_results, camera)
                {
                    overrideMaterial = error_material,
                    renderQueueRange = RenderQueueRange.all
                });
            builder.UseRendererList(pass.list);
            builder.SetRenderAttachment(textures.scene_color, 0, AccessFlags.ReadWrite);
            builder.SetRenderAttachmentDepth(textures.scene_depth, AccessFlags.ReadWrite);

            builder.SetRenderFunc<UnsupportedShadersPass>(static (pass, context) => pass.Render(context));
        }

        private RendererListHandle list;

        private void Render(RasterGraphContext context)
        {
            context.cmd.DrawRendererList(list);
        }

        internal static void Cleanup()
        {
            CoreUtils.Destroy(error_material);
            error_material = null;
        }
    }
}
#endif
