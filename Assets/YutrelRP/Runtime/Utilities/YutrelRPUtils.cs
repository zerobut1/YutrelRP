using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace YutrelRP
{
    public static class YutrelRPUtils
    {
        internal static TextureHandle CreateColorTexture(RenderGraph graph, int width, int height, string name)
        {
            var colorRT_sRGB = (QualitySettings.activeColorSpace == ColorSpace.Linear);

            var desc = new TextureDesc(width, height)
            {
                colorFormat = GraphicsFormatUtility.GetGraphicsFormat(RenderTextureFormat.Default, colorRT_sRGB),
                depthBufferBits = 0,
                msaaSamples = MSAASamples.None,
                enableRandomWrite = false,
                clearBuffer = true,
                clearColor = Color.black,
                name = name,
            };

            return graph.CreateTexture(desc);
        }

        internal static TextureHandle CreateDepthTexture(RenderGraph graph, int width, int height, string name)
        {
            var colorRT_sRGB = (QualitySettings.activeColorSpace == ColorSpace.Linear);

            var desc = new TextureDesc(width, height)
            {
                colorFormat = GraphicsFormatUtility.GetGraphicsFormat(RenderTextureFormat.Depth, colorRT_sRGB),
                depthBufferBits = DepthBits.Depth24,
                msaaSamples = MSAASamples.None,
                enableRandomWrite = false,
                clearBuffer = true,
                clearColor = Color.black,
                name = name,
            };

            return graph.CreateTexture(desc);
        }
    }
}