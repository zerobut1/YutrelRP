using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace YutrelRP
{
    // Copies device depth at the caller's recording point. No material-family policy.
    internal class DepthCopyPass
    {
        private static readonly ProfilingSampler sampler = new("Copy Camera Depth");
        private static readonly int source_ID = Shader.PropertyToID("_DepthCopySource");
        private static Material material;
        private static readonly MaterialPropertyBlock properties = new();
        private TextureHandle source;
        private Material pass_material;

        internal static TextureHandle Record(RenderGraph graph, TextureHandle source)
        {
            if (!YutrelRPRuntimeShaderUtility.TryGetResources(out var resources) ||
                !YutrelRPRuntimeShaderUtility.TryCreateMaterial(resources.depth_copy,
                    nameof(YutrelRPRuntimeShaders.depth_copy), ref material))
                return TextureHandle.nullHandle;

            var desc = graph.GetTextureDesc(source);
            desc.name = "Camera Depth Snapshot";
            desc.format = GraphicsFormat.R32_SFloat;
            desc.msaaSamples = MSAASamples.None;
            desc.bindTextureMS = false;
            desc.clearBuffer = false;
            var destination = graph.CreateTexture(desc);
            using var builder = graph.AddRasterRenderPass<DepthCopyPass>(sampler.name, out var pass, sampler);
            pass.source = source;
            pass.pass_material = material;
            builder.UseTexture(source, AccessFlags.Read);
            builder.SetRenderAttachment(destination, 0, AccessFlags.Write);
            builder.SetRenderFunc<DepthCopyPass>(static (data, context) =>
            {
                properties.Clear();
                properties.SetTexture(source_ID, data.source);
                CoreUtils.DrawFullScreen(context.cmd, data.pass_material, properties);
            });
            return destination;
        }

        internal static void Cleanup()
        {
            CoreUtils.Destroy(material);
            material = null;
            properties.Clear();
        }
    }
}
