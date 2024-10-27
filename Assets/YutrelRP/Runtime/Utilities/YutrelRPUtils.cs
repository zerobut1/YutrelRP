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

        internal static Mesh CreateFullscreenMesh()
        {
            // Simple full-screen triangle.
            Vector3[] positions =
            {
                new Vector3(-1.0f, 1.0f, 0.0f),
                new Vector3(-1.0f, -3.0f, 0.0f),
                new Vector3(3.0f, 1.0f, 0.0f)
            };

            int[] indices = { 0, 1, 2 };

            var mesh = new Mesh
            {
                indexFormat = IndexFormat.UInt16,
                vertices = positions,
                triangles = indices
            };

            return mesh;
        }
    }
}