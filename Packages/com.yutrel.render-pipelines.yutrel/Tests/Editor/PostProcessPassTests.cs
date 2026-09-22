using NUnit.Framework;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace YutrelRP.Tests
{
    public sealed class PostProcessPassTests
    {
        [Test]
        public void BeforeToneMapping_NullMaterialReturnsHdrSource()
        {
            var renderGraph = new RenderGraph("Before Tone Mapping Test");
            try
            {
                var source = CreateHdrTexture(renderGraph);
                var resources = new YutrelRendererOutput(source);

                var result = new BeforeToneMappingPass().Record(
                    renderGraph, resources, Vector2Int.one, null,
                    GraphicsFormat.R16G16B16A16_SFloat);

                Assert.That(result, Is.EqualTo(source));
            }
            finally
            {
                renderGraph.Cleanup();
            }
        }

        [Test]
        public void ToneMappingNone_ReturnsHdrSource()
        {
            var renderGraph = new RenderGraph("Tone Mapping None Test");
            var pass = new ToneMappingPass();
            try
            {
                var source = CreateHdrTexture(renderGraph);
                var settings = new ResolvedPostProcessSettings(
                    ExposureSettings.Default,
                    new ToneMappingSettings { mode = ToneMappingSettings.Mode.None });

                var result = pass.Record(renderGraph, source, Vector2Int.one, settings,
                    GraphicsFormat.R8G8B8A8_UNorm);

                Assert.That(result, Is.EqualTo(source));
            }
            finally
            {
                pass.Dispose();
                renderGraph.Cleanup();
            }
        }

        private static TextureHandle CreateHdrTexture(RenderGraph renderGraph)
        {
            return renderGraph.CreateTexture(new TextureDesc(1, 1)
            {
                colorFormat = GraphicsFormat.R16G16B16A16_SFloat,
                name = "Test HDR Color"
            });
        }
    }
}
