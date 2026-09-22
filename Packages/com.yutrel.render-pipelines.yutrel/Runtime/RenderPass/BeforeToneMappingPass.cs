using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace YutrelRP
{
    internal sealed class BeforeToneMappingPass
    {
        private const string PassName = "BeforeToneMapping";
        private static readonly ProfilingSampler sampler = new("Before Tone Mapping Pass");
        private static readonly int sourceColorId = Shader.PropertyToID("_SourceColor");
        private static readonly int sourceScaleBiasId = Shader.PropertyToID("_SourceScaleBias");
        private static readonly int gbufferAId = Shader.PropertyToID("_GBuffer_A");
        private static readonly int gbufferBId = Shader.PropertyToID("_GBuffer_B");
        private static readonly int gbufferCId = Shader.PropertyToID("_GBuffer_C");
        private static readonly int gbufferDId = Shader.PropertyToID("_GBuffer_D");
        private static readonly Vector4 identityScaleBias = new(1, 1, 0, 0);
        private readonly MaterialPropertyBlock properties = new();

        internal TextureHandle Record(RenderGraph graph, in YutrelRendererOutput resources,
            Vector2Int targetSize, Material material, GraphicsFormat outputFormat)
        {
            if (material == null)
            {
                return resources.sceneColor;
            }

            if (!resources.sceneColor.IsValid())
            {
                throw new ArgumentException("Before-tone-mapping processing requires scene color.");
            }

            var passIndex = material.FindPass(PassName);
            if (passIndex < 0)
            {
                throw new InvalidOperationException(
                    $"Material '{material.name}' does not contain a pass named '{PassName}'.");
            }

            var destination = graph.CreateTexture(new TextureDesc(targetSize.x, targetSize.y)
            {
                colorFormat = outputFormat,
                clearBuffer = false,
                name = "Before Tone Mapping Color"
            });

            using var builder = graph.AddRasterRenderPass<PassData>(sampler.name, out var pass, sampler);
            pass.sourceColor = resources.sceneColor;
            pass.gbufferA = resources.gbufferA;
            pass.gbufferB = resources.gbufferB;
            pass.gbufferC = resources.gbufferC;
            pass.gbufferD = resources.gbufferD;
            pass.material = material;
            pass.properties = properties;
            pass.passIndex = passIndex;

            builder.UseTexture(pass.sourceColor);
            UseOptionalTexture(builder, pass.gbufferA);
            UseOptionalTexture(builder, pass.gbufferB);
            UseOptionalTexture(builder, pass.gbufferC);
            UseOptionalTexture(builder, pass.gbufferD);
            builder.SetRenderAttachment(destination, 0);
            builder.SetRenderFunc<PassData>(static (data, context) =>
            {
                data.properties.Clear();
                data.properties.SetTexture(sourceColorId, data.sourceColor);
                data.properties.SetVector(sourceScaleBiasId, identityScaleBias);
                SetOptionalTexture(data.properties, gbufferAId, data.gbufferA);
                SetOptionalTexture(data.properties, gbufferBId, data.gbufferB);
                SetOptionalTexture(data.properties, gbufferCId, data.gbufferC);
                SetOptionalTexture(data.properties, gbufferDId, data.gbufferD);
                CoreUtils.DrawFullScreen(context.cmd, data.material, data.properties, data.passIndex);
            });

            return destination;
        }

        private static void UseOptionalTexture(IRasterRenderGraphBuilder builder, TextureHandle texture)
        {
            if (texture.IsValid())
            {
                builder.UseTexture(texture);
            }
        }

        private static void SetOptionalTexture(MaterialPropertyBlock properties, int propertyId,
            TextureHandle texture)
        {
            if (texture.IsValid())
            {
                properties.SetTexture(propertyId, texture);
            }
            else
            {
                properties.SetTexture(propertyId, Texture2D.blackTexture);
            }
        }

        private sealed class PassData
        {
            public TextureHandle sourceColor;
            public TextureHandle gbufferA;
            public TextureHandle gbufferB;
            public TextureHandle gbufferC;
            public TextureHandle gbufferD;
            public Material material;
            public MaterialPropertyBlock properties;
            public int passIndex;
        }
    }
}
