using UnityEngine;
using System.Collections.Generic;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace YutrelRP
{
    internal class SkyboxPass
    {
        private static readonly ProfilingSampler sampler = new("Skybox Pass");
        private static readonly HashSet<string> warned_invalid_skybox = new();
        private static Material material;
        private static MaterialPropertyBlock property_block;

        internal static void Record(RenderGraph render_graph, Camera camera, RenderTargets textures,
            LightResources light_resources)
        {
            if (camera.clearFlags != CameraClearFlags.Skybox)
            {
                return;
            }

            if (!light_resources.has_environment_skybox)
            {
                LogInvalidSkyboxOnce(light_resources.environment_skybox_error);
                return;
            }

            if (!TryEnsureMaterial()) return;
            if (property_block == null) property_block = new MaterialPropertyBlock();

            using var builder = render_graph.AddRasterRenderPass<SkyboxPass>(sampler.name, out var pass, sampler);

            pass.environment_skybox_ID = LightResources.environment_skybox_ID;
            pass.environment_intensity_ID = LightResources.environment_intensity_ID;
            pass.environment_skybox_multiplier_ID = LightResources.environment_skybox_multiplier_ID;
            pass.environment_skybox = light_resources.environment_skybox;
            pass.environment_intensity = light_resources.environment_intensity;
            pass.environment_skybox_multiplier = light_resources.environment_skybox_multiplier;

            builder.UseTexture(pass.environment_skybox);
            builder.SetRenderAttachment(textures.scene_color, 0, AccessFlags.ReadWrite);
            builder.SetRenderAttachmentDepth(textures.scene_depth, AccessFlags.Read);

            builder.AllowPassCulling(false);

            builder.SetRenderFunc<SkyboxPass>(static (pass, context) => pass.Render(context));
        }

        // data
        private TextureHandle environment_skybox;
        private int environment_skybox_ID;
        private int environment_intensity_ID;
        private int environment_skybox_multiplier_ID;
        private float environment_intensity;
        private float environment_skybox_multiplier;

        private void Render(RasterGraphContext context)
        {
            property_block.Clear();
            property_block.SetTexture(environment_skybox_ID, environment_skybox);
            property_block.SetFloat(environment_intensity_ID, environment_intensity);
            property_block.SetFloat(environment_skybox_multiplier_ID, environment_skybox_multiplier);
            CoreUtils.DrawFullScreen(context.cmd, material, property_block);
        }

        public static void Cleanup()
        {
            CoreUtils.Destroy(material);
            material = null;
            property_block = null;
            warned_invalid_skybox.Clear();
        }

        private static bool TryEnsureMaterial()
        {
            if (!YutrelRPRuntimeShaderUtility.TryGetResources(out var resources))
            {
                return false;
            }

            return YutrelRPRuntimeShaderUtility.TryCreateMaterial(
                resources.skybox_pass,
                nameof(YutrelRPRuntimeShaders.skybox_pass),
                ref material);
        }

        private static void LogInvalidSkyboxOnce(string error)
        {
            if (string.IsNullOrEmpty(error) || !warned_invalid_skybox.Add(error))
            {
                return;
            }

            Debug.LogWarning($"YutrelRP: SkyboxPass skipped. {error}");
        }
    }
}
