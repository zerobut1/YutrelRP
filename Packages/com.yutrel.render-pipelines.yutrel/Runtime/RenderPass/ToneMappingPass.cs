using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace YutrelRP
{
    internal readonly struct ToneMappingPassInputs
    {
        public readonly Shader shader_override;
        public readonly TextureHandle GBuffer_A;
        public readonly bool character_grading_enabled;
        public readonly Texture3D character_grading_lut;

        public ToneMappingPassInputs(
            Shader shader_override,
            TextureHandle GBuffer_A,
            bool character_grading_enabled,
            Texture3D character_grading_lut)
        {
            this.shader_override = shader_override;
            this.GBuffer_A = GBuffer_A;
            this.character_grading_enabled = character_grading_enabled;
            this.character_grading_lut = character_grading_lut;
        }

        public static ToneMappingPassInputs Default => new(null, default, false, null);
    }

    internal class ToneMappingPass
    {
        private static readonly ProfilingSampler sampler = new("Tone Mapping Pass");
        private static readonly int
            source_color_ID = Shader.PropertyToID("_SourceColor"),
            source_scale_bias_ID = Shader.PropertyToID("_SourceScaleBias"),
            GBuffer_A_ID = Shader.PropertyToID("_GBuffer_A"),
            character_grading_enabled_ID = Shader.PropertyToID("_NteCharacterGradingEnabled"),
            character_grading_lut_ID = Shader.PropertyToID("_NteCharacterGradingLut");
        private static Material material;
        private static Shader material_shader;
        private static MaterialPropertyBlock property_block;
        private static readonly Vector4 identity_source_scale_bias = new(1.0f, 1.0f, 0.0f, 0.0f);

        internal static TextureHandle Record(RenderGraph render_graph, TextureHandle source_color,
            Vector2Int target_size, ResolvedPostProcessSettings post_process_settings,
            in ToneMappingPassInputs inputs)
        {
            if (!source_color.IsValid() || !TryEnsureMaterial(inputs.shader_override)) return source_color;
            if (property_block == null) property_block = new MaterialPropertyBlock();

            var final_color = render_graph.CreateTexture(new TextureDesc(target_size.x, target_size.y)
            {
                colorFormat = GraphicsFormatUtility.GetGraphicsFormat(RenderTextureFormat.Default, true),
                clearBuffer = false,
                name = "Final Color"
            });

            using var builder = render_graph.AddRasterRenderPass<ToneMappingPass>(sampler.name, out var pass, sampler);

            pass.source_color = source_color;
            pass.pass_id = (int)post_process_settings.tone_mapping.mode;
            pass.GBuffer_A = inputs.GBuffer_A;
            pass.character_grading_enabled = inputs.character_grading_enabled && inputs.GBuffer_A.IsValid();
            pass.character_grading_lut = inputs.character_grading_lut;
            builder.UseTexture(pass.source_color);
            if (pass.GBuffer_A.IsValid())
            {
                builder.UseTexture(pass.GBuffer_A);
            }
            builder.SetRenderAttachment(final_color, 0);

            builder.SetRenderFunc<ToneMappingPass>(static (pass, context) => { pass.Render(context); });

            return final_color;
        }

        // data
        private TextureHandle source_color;
        private TextureHandle GBuffer_A;

        private int pass_id;
        private bool character_grading_enabled;
        private Texture3D character_grading_lut;

        private void Render(RasterGraphContext context)
        {
            var cmd = context.cmd;

            property_block.Clear();
            property_block.SetTexture(source_color_ID, source_color);
            property_block.SetVector(source_scale_bias_ID, identity_source_scale_bias);
            if (GBuffer_A.IsValid())
            {
                property_block.SetTexture(GBuffer_A_ID, GBuffer_A);
            }
            property_block.SetInteger(character_grading_enabled_ID, character_grading_enabled ? 1 : 0);
            property_block.SetTexture(
                character_grading_lut_ID,
                character_grading_lut != null ? character_grading_lut : CoreUtils.blackVolumeTexture);

            CoreUtils.DrawFullScreen(cmd, material, property_block, pass_id);
        }

        public static void Cleanup()
        {
            CoreUtils.Destroy(material);
            material = null;
            material_shader = null;
            property_block = null;
        }

        private static bool TryEnsureMaterial(Shader shader_override)
        {
            Shader requested_shader;
            string resource_name;
            if (shader_override != null)
            {
                requested_shader = shader_override;
                resource_name = nameof(YutrelDeferredRendererSettings.toneMappingShaderOverride);
            }
            else
            {
                if (!YutrelRPRuntimeShaderUtility.TryGetResources(out var resources))
                {
                    return false;
                }

                requested_shader = resources.tone_mapping;
                resource_name = nameof(YutrelRPRuntimeShaders.tone_mapping);
            }

            if (material != null && material_shader != requested_shader)
            {
                CoreUtils.Destroy(material);
                material = null;
                material_shader = null;
            }

            if (!YutrelRPRuntimeShaderUtility.TryCreateMaterial(
                    requested_shader,
                    resource_name,
                    ref material))
            {
                return false;
            }

            material_shader = requested_shader;
            return true;
        }
    }
}

