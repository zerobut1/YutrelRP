using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace YutrelRP
{
    internal sealed class DirectionalLightPass
    {
        private const string ExtensionPassName = "DirectionalLightExtension";
        private static readonly ProfilingSampler sampler = new("Directional Light Pass");
        private static readonly ProfilingSampler extensionSampler = new("Directional Light Extension Pass");
        private static readonly MaterialPropertyBlock properties = new();
        private static readonly int lightIndexId = Shader.PropertyToID("_LightIndex");
        private static Material material;
        private static bool warnedMissingDfgLut;

        public static void Record(RenderGraph graph, RenderTargets textures, LightResources lightResources,
            Material extensionMaterial = null)
        {
            if (lightResources.directional_light_count == 0 ||
                !ValidateLightingResources(lightResources) ||
                !TryEnsureMaterial())
            {
                return;
            }

            var extensionPassIndex = FindExtensionPass(extensionMaterial);
            for (var lightIndex = 0; lightIndex < lightResources.directional_light_count; ++lightIndex)
            {
                RecordLight(graph, textures, lightResources, material, 0, lightIndex, sampler);
                if (extensionPassIndex >= 0)
                {
                    RecordLight(graph, textures, lightResources, extensionMaterial,
                        extensionPassIndex, lightIndex, extensionSampler);
                }
            }
        }

        public static void Cleanup()
        {
            CoreUtils.Destroy(material);
            material = null;
        }

        private static void RecordLight(RenderGraph graph, RenderTargets textures,
            LightResources lightResources, Material passMaterial, int materialPassIndex,
            int lightIndex, ProfilingSampler profilingSampler)
        {
            using var builder = graph.AddRasterRenderPass<PassData>(
                $"{profilingSampler.name} {lightIndex}", out var pass, profilingSampler);

            pass.gbufferA = textures.GBuffer_A;
            pass.gbufferB = textures.GBuffer_B;
            pass.gbufferC = textures.GBuffer_C;
            pass.gbufferD = textures.GBuffer_D;
            pass.sceneDepth = textures.scene_depth;
            pass.shadowMask = textures.shadow_mask;
            pass.dfgLut = lightResources.DFG_LUT;
            pass.directionalLightData = lightResources.directional_light_data_buffer;
            pass.material = passMaterial;
            pass.materialPassIndex = materialPassIndex;
            pass.lightIndex = lightIndex;

            builder.UseTexture(pass.gbufferA);
            builder.UseTexture(pass.gbufferB);
            builder.UseTexture(pass.gbufferC);
            builder.UseTexture(pass.gbufferD);
            builder.UseTexture(pass.sceneDepth);
            builder.UseTexture(pass.shadowMask);
            builder.UseTexture(pass.dfgLut);
            builder.UseBuffer(pass.directionalLightData);
            builder.SetRenderAttachment(textures.scene_color, 0, AccessFlags.ReadWrite);
            builder.SetRenderFunc<PassData>(static (data, context) => Render(data, context));
        }

        private static void Render(PassData pass, RasterGraphContext context)
        {
            properties.Clear();
            properties.SetTexture(RenderTargets.GBuffer_A_ID, pass.gbufferA);
            properties.SetTexture(RenderTargets.GBuffer_B_ID, pass.gbufferB);
            properties.SetTexture(RenderTargets.GBuffer_C_ID, pass.gbufferC);
            properties.SetTexture(RenderTargets.GBuffer_D_ID, pass.gbufferD);
            properties.SetTexture(RenderTargets.scene_depth_ID, pass.sceneDepth);
            properties.SetTexture(RenderTargets.shadow_mask_ID, pass.shadowMask);
            properties.SetTexture(LightResources.dfg_lut_ID, pass.dfgLut);
            properties.SetBuffer(LightResources.directional_light_data_ID, pass.directionalLightData);
            properties.SetInteger(lightIndexId, pass.lightIndex);

            CoreUtils.DrawFullScreen(context.cmd, pass.material, properties, pass.materialPassIndex);
        }

        private static int FindExtensionPass(Material extensionMaterial)
        {
            if (extensionMaterial == null)
            {
                return -1;
            }

            var passIndex = extensionMaterial.FindPass(ExtensionPassName);
            if (passIndex < 0)
            {
                throw new InvalidOperationException(
                    $"Material '{extensionMaterial.name}' does not contain a pass named '{ExtensionPassName}'.");
            }

            return passIndex;
        }

        private static bool TryEnsureMaterial()
        {
            if (!YutrelRPRuntimeShaderUtility.TryGetResources(out var resources))
            {
                return false;
            }

            return YutrelRPRuntimeShaderUtility.TryCreateMaterial(
                resources.directional_light_pass,
                nameof(YutrelRPRuntimeShaders.directional_light_pass),
                ref material);
        }

        private static bool ValidateLightingResources(LightResources lightResources)
        {
            if (lightResources.has_DFG_LUT)
            {
                return true;
            }

            if (!warnedMissingDfgLut)
            {
                Debug.LogError(
                    "YutrelRP: DirectionalLightPass skipped because the fixed DFG LUT is missing at Resources/Texture/DFG_LUT.");
                warnedMissingDfgLut = true;
            }

            return false;
        }

        private sealed class PassData
        {
            public TextureHandle gbufferA;
            public TextureHandle gbufferB;
            public TextureHandle gbufferC;
            public TextureHandle gbufferD;
            public TextureHandle sceneDepth;
            public TextureHandle shadowMask;
            public TextureHandle dfgLut;
            public BufferHandle directionalLightData;
            public Material material;
            public int materialPassIndex;
            public int lightIndex;
        }
    }
}
