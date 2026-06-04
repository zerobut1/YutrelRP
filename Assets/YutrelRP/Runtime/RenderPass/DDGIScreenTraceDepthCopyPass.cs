#if UNITY_EDITOR
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace YutrelRP
{
    internal sealed class DDGIScreenTraceDepthCopyPass
    {
        private const string ShaderName = "YutrelRP/DDGIScreenTraceDepthCopy";
        private static readonly ProfilingSampler sampler = new("DDGI Screen Trace Depth Copy");
        private static readonly int sceneDepthID = RenderTargets.scene_depth_ID;
        private static Material material;
        private static MaterialPropertyBlock propertyBlock;

        internal static TextureHandle Record(RenderGraph renderGraph, TextureHandle sceneDepth, Vector2Int attachmentSize)
        {
            if (material == null)
            {
                material = CoreUtils.CreateEngineMaterial(Shader.Find(ShaderName));
            }

            if (propertyBlock == null)
            {
                propertyBlock = new MaterialPropertyBlock();
            }

            var desc = new TextureDesc(attachmentSize.x, attachmentSize.y)
            {
                colorFormat = GraphicsFormat.R32_SFloat,
                enableRandomWrite = false,
                clearBuffer = true,
                clearColor = Color.clear,
                name = "DDGI Screen Trace Depth"
            };
            var output = renderGraph.CreateTexture(desc);

            using var builder = renderGraph.AddRasterRenderPass<DDGIScreenTraceDepthCopyPass>(sampler.name, out var pass, sampler);
            pass.sceneDepth = sceneDepth;
            pass.output = output;
            builder.UseTexture(sceneDepth);
            builder.SetRenderAttachment(output, 0);
            builder.SetRenderFunc<DDGIScreenTraceDepthCopyPass>(static (pass, context) => pass.Render(context));

            return output;
        }

        private TextureHandle sceneDepth;
        private TextureHandle output;

        private void Render(RasterGraphContext context)
        {
            propertyBlock.Clear();
            propertyBlock.SetTexture(sceneDepthID, sceneDepth);
            CoreUtils.DrawFullScreen(context.cmd, material, propertyBlock);
        }

        internal static void Cleanup()
        {
            CoreUtils.Destroy(material);
            material = null;
            propertyBlock = null;
        }
    }
}
#endif
