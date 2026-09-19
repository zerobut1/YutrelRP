using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace YutrelRP
{
    internal sealed class ToneMappingPass : IDisposable
    {
        private static readonly ProfilingSampler sampler = new("Tone Mapping Pass");
        private static readonly int sourceColorId = Shader.PropertyToID("_SourceColor");
        private static readonly int sourceScaleBiasId = Shader.PropertyToID("_SourceScaleBias");
        private static readonly Vector4 identityScaleBias = new(1, 1, 0, 0);
        private Material material;
        private readonly MaterialPropertyBlock properties = new();

        internal TextureHandle Record(RenderGraph graph, TextureHandle sourceColor,
            Vector2Int targetSize, in ResolvedPostProcessSettings settings, GraphicsFormat outputFormat)
        {
            if (!sourceColor.IsValid()) throw new ArgumentException("Post-processing requires scene color.");
            if (!YutrelRPRuntimeShaderUtility.TryGetResources(out var resources))
                throw new InvalidOperationException("Default tone-mapping resources are unavailable.");
            if (material != null && material.shader != resources.tone_mapping)
            {
                CoreUtils.Destroy(material);
                material = null;
            }
            if (!YutrelRPRuntimeShaderUtility.TryCreateMaterial(resources.tone_mapping,
                    nameof(YutrelRPRuntimeShaders.tone_mapping), ref material))
                throw new InvalidOperationException("Default tone-mapping shader is unavailable.");

            var destination = graph.CreateTexture(new TextureDesc(targetSize.x, targetSize.y)
            {
                colorFormat = outputFormat, clearBuffer = false, name = "Final Color"
            });
            using var builder = graph.AddRasterRenderPass<PassData>(sampler.name, out var pass, sampler);
            pass.source = sourceColor;
            pass.material = material;
            pass.properties = properties;
            pass.passIndex = (int)settings.tone_mapping.mode;
            builder.UseTexture(sourceColor);
            builder.SetRenderAttachment(destination, 0);
            builder.SetRenderFunc<PassData>(static (data, context) =>
            {
                data.properties.Clear();
                data.properties.SetTexture(sourceColorId, data.source);
                data.properties.SetVector(sourceScaleBiasId, identityScaleBias);
                CoreUtils.DrawFullScreen(context.cmd, data.material, data.properties, data.passIndex);
            });
            return destination;
        }

        private sealed class PassData
        {
            public TextureHandle source;
            public Material material;
            public MaterialPropertyBlock properties;
            public int passIndex;
        }

        public void Dispose()
        {
            CoreUtils.Destroy(material);
            material = null;
        }
    }
}
