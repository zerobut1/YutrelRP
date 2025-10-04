using System.Diagnostics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RendererUtils;
using UnityEngine.Rendering.RenderGraphModule;

namespace YutrelRP
{
    internal class DefaultShaderPass
    {
        [Conditional("UNITY_EDITOR")]
        public static void Record(RenderGraph render_graph, Camera camera, CullingResults culling_reuslts,
            in RenderTargets textures)
        {
#if UNITY_EDITOR
            using var builder = render_graph.AddRasterRenderPass<DefaultShaderPass>("Default Shader Pass", out var pass,
                sampler);

            if (error_material == null) error_material = new Material(Shader.Find("Hidden/InternalErrorShader"));

            pass.list = render_graph.CreateRendererList(
                new RendererListDesc(shader_tag_ids, culling_reuslts, camera)
                {
                    overrideMaterial = error_material,
                    renderQueueRange = RenderQueueRange.all
                });
            builder.UseRendererList(pass.list);
            builder.SetRenderAttachment(textures.scene_color, 0);
            builder.SetRenderAttachmentDepth(textures.scene_depth, AccessFlags.ReadWrite);

            builder.SetRenderFunc<DefaultShaderPass>(static (pass, context) => pass.Render(context));
#endif
        }
#if UNITY_EDITOR
        private static readonly ProfilingSampler sampler = new("Default Shader Pass");

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

        private RendererListHandle list;

        private void Render(RasterGraphContext context)
        {
            context.cmd.DrawRendererList(list);
        }
#endif
    }
}