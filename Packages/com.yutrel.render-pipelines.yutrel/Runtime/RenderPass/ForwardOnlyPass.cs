using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RendererUtils;
using UnityEngine.Rendering.RenderGraphModule;

namespace YutrelRP
{
    internal class ForwardOnlyPass
    {
        private static readonly ProfilingSampler sampler = new("Forward Only Pass");
        private static readonly ShaderTagId shader_tag_id = new("YutrelForwardOnly");
        private static readonly int directional_light_count_ID = Shader.PropertyToID("_DirectionalLightCount");
        private static readonly int endfield_ambient_color_ID =
            Shader.PropertyToID("_EndfieldAmbientColor");
        private static readonly int endfield_yutrel_input_scale_ID =
            Shader.PropertyToID("_EndfieldYutrelInputScale");
        private static readonly int endfield_ambient_intensity_ID =
            Shader.PropertyToID("_EndfieldAmbientIntensity");
        private static readonly int endfield_scene_pre_exposure_ID =
            Shader.PropertyToID("_EndfieldScenePreExposure");
        private static readonly int endfield_use_screen_space_ao_ID =
            Shader.PropertyToID("_EndfieldUseScreenSpaceAO");

        internal static void Record(RenderGraph render_graph, Camera camera, CullingResults culling_results,
            RenderTargets textures, LightResources light_resources, ResolvedEndfieldSettings endfield_settings,
            float yutrel_pre_exposure, bool use_screen_space_ao)
        {
            // Endfield uses its own BRDF LUT and Volume ambient, including scenes without a main light.
            using var builder =
                render_graph.AddRasterRenderPass<ForwardOnlyPass>(sampler.name, out var pass, sampler);

            var renderer_desc = new RendererListDesc(shader_tag_id, culling_results, camera)
            {
                sortingCriteria = SortingCriteria.CommonOpaque,
                renderQueueRange = RenderQueueRange.opaque
            };
            pass.renderer_list = render_graph.CreateRendererList(renderer_desc);
            pass.directional_light_count = light_resources.directional_light_count;
            pass.directional_light_data_buffer = light_resources.directional_light_data_buffer;
            pass.shadow_mask = textures.shadow_mask;
            pass.screen_space_ao = textures.screen_space_ao.IsValid()
                ? textures.screen_space_ao
                : render_graph.defaultResources.whiteTexture;
            pass.endfield_ambient_color = endfield_settings.ambient_color;
            pass.endfield_yutrel_input_scale =
                endfield_settings.GetYutrelInputScale(yutrel_pre_exposure);
            pass.endfield_ambient_intensity = endfield_settings.ambient_intensity;
            pass.endfield_scene_pre_exposure = endfield_settings.endfield_scene_pre_exposure;
            pass.use_screen_space_ao = use_screen_space_ao;

            builder.UseRendererList(pass.renderer_list);
            builder.UseTexture(pass.screen_space_ao);
            if (pass.directional_light_count > 0)
            {
                builder.UseBuffer(pass.directional_light_data_buffer);
                builder.UseTexture(pass.shadow_mask);
            }

            builder.SetRenderAttachment(textures.scene_color, 0, AccessFlags.ReadWrite);
            builder.SetRenderAttachmentDepth(textures.scene_depth, AccessFlags.ReadWrite);
            builder.AllowGlobalStateModification(true);
            builder.SetRenderFunc<ForwardOnlyPass>(static (pass, context) => pass.Render(context));
        }

        private RendererListHandle renderer_list;
        private int directional_light_count;
        private BufferHandle directional_light_data_buffer;
        private TextureHandle shadow_mask;
        private TextureHandle screen_space_ao;
        private Color endfield_ambient_color;
        private float endfield_yutrel_input_scale;
        private float endfield_ambient_intensity;
        private float endfield_scene_pre_exposure;
        private bool use_screen_space_ao;

        private void Render(RasterGraphContext context)
        {
            context.cmd.SetGlobalVector(endfield_ambient_color_ID, endfield_ambient_color);
            context.cmd.SetGlobalFloat(endfield_yutrel_input_scale_ID, endfield_yutrel_input_scale);
            context.cmd.SetGlobalFloat(endfield_ambient_intensity_ID, endfield_ambient_intensity);
            context.cmd.SetGlobalFloat(endfield_scene_pre_exposure_ID, endfield_scene_pre_exposure);
            context.cmd.SetGlobalFloat(endfield_use_screen_space_ao_ID, use_screen_space_ao ? 1.0f : 0.0f);
            context.cmd.SetGlobalTexture(RenderTargets.screen_space_ao_ID, screen_space_ao);
            context.cmd.SetGlobalInt(directional_light_count_ID, directional_light_count);
            if (directional_light_count > 0)
            {
                context.cmd.SetGlobalBuffer(LightResources.directional_light_data_ID, directional_light_data_buffer);
                context.cmd.SetGlobalTexture(RenderTargets.shadow_mask_ID, shadow_mask);
            }

            context.cmd.DrawRendererList(renderer_list);
        }
    }
}
