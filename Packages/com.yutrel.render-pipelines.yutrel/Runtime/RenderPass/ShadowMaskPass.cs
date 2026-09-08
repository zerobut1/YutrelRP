using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace YutrelRP
{
    internal class ShadowMaskPass
    {
        private static readonly ProfilingSampler sampler = new("Shadow Mask Pass");
        private static Material material;

        internal static void Record(RenderGraph render_graph, RenderTargets textures, LightResources light_resources,
            DirectionalShadowBindings shadow_bindings, Vector2Int attachment_size)
        {
            if (light_resources.directional_light_count == 0)
            {
                textures.shadow_mask = render_graph.defaultResources.whiteTexture;
                return;
            }

            if (!shadow_bindings.Available)
            {
                textures.shadow_mask = render_graph.defaultResources.whiteTexture;
                return;
            }

            if (!TryEnsureMaterial())
            {
                textures.shadow_mask = render_graph.defaultResources.whiteTexture;
                return;
            }

            using var builder = render_graph.AddRasterRenderPass<ShadowMaskPass>(sampler.name, out var pass, sampler);

            var shadow_mask_desc = new TextureDesc(attachment_size.x, attachment_size.y)
            {
                colorFormat = GraphicsFormatUtility.GetGraphicsFormat(RenderTextureFormat.RFloat, true),
                clearBuffer = true,
                clearColor = Color.white,
                name = "Shadow Mask"
            };
            textures.shadow_mask = render_graph.CreateTexture(shadow_mask_desc);

            pass.shadows = shadow_bindings;
            pass.scene_depth = textures.scene_depth;
            pass.GBuffer_B = textures.GBuffer_B;
            pass.directional_light_data_buffer = light_resources.directional_light_data_buffer;
            shadow_bindings.DeclareResources(builder);
            builder.UseTexture(pass.scene_depth);
            builder.UseTexture(pass.GBuffer_B);
            builder.UseBuffer(pass.directional_light_data_buffer);
            builder.SetRenderAttachment(textures.shadow_mask, 0);

            builder.SetRenderFunc<ShadowMaskPass>(static (pass, context) => { pass.Render(context); });
        }

        private DirectionalShadowBindings shadows;
        private TextureHandle scene_depth, GBuffer_B;
        private BufferHandle directional_light_data_buffer;

        private void Render(RasterGraphContext context)
        {
            shadows.BindMaterial(material);
            material.SetTexture(RenderTargets.scene_depth_ID, scene_depth);
            material.SetTexture(RenderTargets.GBuffer_B_ID, GBuffer_B);
            material.SetBuffer(LightResources.directional_light_data_ID, directional_light_data_buffer);
            CoreUtils.DrawFullScreen(context.cmd, material);
        }

        public static void Cleanup()
        {
            CoreUtils.Destroy(material);
            material = null;
        }

        private static bool TryEnsureMaterial()
        {
            if (!YutrelRPRuntimeShaderUtility.TryGetResources(out var resources))
            {
                return false;
            }

            return YutrelRPRuntimeShaderUtility.TryCreateMaterial(
                resources.shadow_mask_pass,
                nameof(YutrelRPRuntimeShaders.shadow_mask_pass),
                ref material);
        }
    }
}
