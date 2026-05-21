using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace YutrelRP
{
    internal class ScreenSpaceAmbientOcclusionPass
    {
        private const string SSAOShaderName = "YutrelRP/SSAO";
        private const string HBAOShaderName = "YutrelRP/HBAO";
        private const string GTAOShaderName = "YutrelRP/GTAO";

        private static readonly ProfilingSampler ssao_sampler = new("SSAO Pass");
        private static readonly ProfilingSampler hbao_sampler = new("HBAO Pass");
        private static readonly ProfilingSampler gtao_sampler = new("GTAO Pass");

        private static readonly int ao_radius_ID = Shader.PropertyToID("_AORadius");
        private static readonly int ao_intensity_ID = Shader.PropertyToID("_AOIntensity");
        private static readonly int ao_bias_ID = Shader.PropertyToID("_AOBias");
        private static readonly int ao_sample_count_ID = Shader.PropertyToID("_AOSampleCount");
        private static readonly int ao_direction_count_ID = Shader.PropertyToID("_AODirectionCount");
        private static readonly int ao_step_count_ID = Shader.PropertyToID("_AOStepCount");
        private static readonly int ao_thickness_ID = Shader.PropertyToID("_AOThickness");
        private static readonly int ao_slice_count_ID = Shader.PropertyToID("_AOSliceCount");
        private static readonly int ao_samples_per_slice_ID = Shader.PropertyToID("_AOSamplesPerSlice");
        private static readonly int ao_denoise_radius_ID = Shader.PropertyToID("_AODenoiseRadius");

        private static Material ssao_material;
        private static Material hbao_material;
        private static Material gtao_material;
        private static MaterialPropertyBlock property_block;
        private static readonly HashSet<string> warned_missing_shader_names = new();

        internal static void Record(RenderGraph render_graph, RenderTargets textures,
            AmbientOcclusionSettings settings, Vector2Int attachment_size)
        {
            textures.screen_space_ao = render_graph.defaultResources.whiteTexture;

            if (settings == null || settings.mode == AmbientOcclusionSettings.Mode.Disabled) return;
            if (!textures.GBuffer_A.IsValid() || !textures.GBuffer_B.IsValid() ||
                !textures.GBuffer_C.IsValid() || !textures.scene_depth.IsValid())
            {
                return;
            }

            var shader_name = GetShaderName(settings.mode);
            var material = GetMaterial(settings.mode, shader_name);
            if (material == null) return;

            if (property_block == null) property_block = new MaterialPropertyBlock();

            var sampler = GetSampler(settings.mode);
            using var builder =
                render_graph.AddRasterRenderPass<ScreenSpaceAmbientOcclusionPass>(sampler.name, out var pass, sampler);

            var ao_desc = new TextureDesc(attachment_size.x, attachment_size.y)
            {
                colorFormat = GraphicsFormat.R8_UNorm,
                clearBuffer = true,
                clearColor = Color.white,
                name = $"{settings.mode} Screen Space AO"
            };
            textures.screen_space_ao = render_graph.CreateTexture(ao_desc);

            pass.material = material;
            pass.GBuffer_A = textures.GBuffer_A;
            pass.GBuffer_B = textures.GBuffer_B;
            pass.GBuffer_C = textures.GBuffer_C;
            pass.scene_depth = textures.scene_depth;
            pass.constants = BuildConstants(settings);

            builder.UseTexture(pass.GBuffer_A);
            builder.UseTexture(pass.GBuffer_B);
            builder.UseTexture(pass.GBuffer_C);
            builder.UseTexture(pass.scene_depth);
            builder.SetRenderAttachment(textures.screen_space_ao, 0);

            builder.SetRenderFunc<ScreenSpaceAmbientOcclusionPass>(static (pass, context) => pass.Render(context));
        }

        private static string GetShaderName(AmbientOcclusionSettings.Mode mode)
        {
            switch (mode)
            {
                case AmbientOcclusionSettings.Mode.SSAO:
                    return SSAOShaderName;
                case AmbientOcclusionSettings.Mode.HBAO:
                    return HBAOShaderName;
                case AmbientOcclusionSettings.Mode.GTAO:
                    return GTAOShaderName;
                default:
                    return null;
            }
        }

        private static ProfilingSampler GetSampler(AmbientOcclusionSettings.Mode mode)
        {
            switch (mode)
            {
                case AmbientOcclusionSettings.Mode.SSAO:
                    return ssao_sampler;
                case AmbientOcclusionSettings.Mode.HBAO:
                    return hbao_sampler;
                case AmbientOcclusionSettings.Mode.GTAO:
                    return gtao_sampler;
                default:
                    return ssao_sampler;
            }
        }

        private static Material GetMaterial(AmbientOcclusionSettings.Mode mode, string shader_name)
        {
            if (string.IsNullOrEmpty(shader_name)) return null;

            switch (mode)
            {
                case AmbientOcclusionSettings.Mode.SSAO:
                    if (ssao_material == null) ssao_material = CreateMaterial(shader_name);
                    return ssao_material;
                case AmbientOcclusionSettings.Mode.HBAO:
                    if (hbao_material == null) hbao_material = CreateMaterial(shader_name);
                    return hbao_material;
                case AmbientOcclusionSettings.Mode.GTAO:
                    if (gtao_material == null) gtao_material = CreateMaterial(shader_name);
                    return gtao_material;
                default:
                    return null;
            }
        }

        private static Material CreateMaterial(string shader_name)
        {
            var shader = Shader.Find(shader_name);
            if (shader == null)
            {
                WarnMissingShaderOnce(shader_name);
                return null;
            }

            return CoreUtils.CreateEngineMaterial(shader);
        }

        private static void WarnMissingShaderOnce(string shader_name)
        {
            if (!warned_missing_shader_names.Add(shader_name)) return;

            Debug.LogWarning($"YutrelRP screen-space AO shader '{shader_name}' was not found. Using neutral AO.");
        }

        private static Constants BuildConstants(AmbientOcclusionSettings settings)
        {
            var constants = new Constants
            {
                radius = 1.0f,
                intensity = 1.0f,
                bias = 0.0f,
                sample_count = 1,
                direction_count = 1,
                step_count = 1,
                thickness = 1.0f,
                slice_count = 1,
                samples_per_slice = 1,
                denoise_radius = 0,
            };

            switch (settings.mode)
            {
                case AmbientOcclusionSettings.Mode.SSAO:
                    var ssao = settings.ssao ?? new AmbientOcclusionSettings.SSAOSettings();
                    constants.radius = Mathf.Max(0.001f, ssao.radius);
                    constants.intensity = Mathf.Max(0.0f, ssao.intensity);
                    constants.bias = Mathf.Clamp(ssao.bias, 0.0f, 0.1f);
                    constants.sample_count = Mathf.Clamp(ssao.sampleCount, 1, 64);
                    constants.denoise_radius = Mathf.Clamp(ssao.denoiseRadius, 0, 4);
                    break;
                case AmbientOcclusionSettings.Mode.HBAO:
                    var hbao = settings.hbao ?? new AmbientOcclusionSettings.HBAOSettings();
                    constants.radius = Mathf.Max(0.001f, hbao.radius);
                    constants.intensity = Mathf.Max(0.0f, hbao.intensity);
                    constants.bias = Mathf.Clamp(hbao.bias, 0.0f, 0.3f);
                    constants.direction_count = Mathf.Clamp(hbao.directionCount, 1, 16);
                    constants.step_count = Mathf.Clamp(hbao.stepCount, 1, 16);
                    constants.thickness = Mathf.Max(0.001f, hbao.thickness);
                    constants.denoise_radius = Mathf.Clamp(hbao.denoiseRadius, 0, 4);
                    break;
                case AmbientOcclusionSettings.Mode.GTAO:
                    var gtao = settings.gtao ?? new AmbientOcclusionSettings.GTAOSettings();
                    constants.radius = Mathf.Max(0.001f, gtao.radius);
                    constants.intensity = Mathf.Max(0.0f, gtao.intensity);
                    constants.slice_count = Mathf.Clamp(gtao.sliceCount, 1, 16);
                    constants.samples_per_slice = Mathf.Clamp(gtao.samplesPerSlice, 1, 16);
                    constants.thickness = Mathf.Max(0.001f, gtao.thickness);
                    constants.denoise_radius = Mathf.Clamp(gtao.denoiseRadius, 0, 4);
                    break;
            }

            return constants;
        }

        private Material material;
        private TextureHandle GBuffer_A;
        private TextureHandle GBuffer_B;
        private TextureHandle GBuffer_C;
        private TextureHandle scene_depth;
        private Constants constants;

        private void Render(RasterGraphContext context)
        {
            property_block.Clear();
            property_block.SetTexture(RenderTargets.GBuffer_A_ID, GBuffer_A);
            property_block.SetTexture(RenderTargets.GBuffer_B_ID, GBuffer_B);
            property_block.SetTexture(RenderTargets.GBuffer_C_ID, GBuffer_C);
            property_block.SetTexture(RenderTargets.scene_depth_ID, scene_depth);
            property_block.SetFloat(ao_radius_ID, constants.radius);
            property_block.SetFloat(ao_intensity_ID, constants.intensity);
            property_block.SetFloat(ao_bias_ID, constants.bias);
            property_block.SetInteger(ao_sample_count_ID, constants.sample_count);
            property_block.SetInteger(ao_direction_count_ID, constants.direction_count);
            property_block.SetInteger(ao_step_count_ID, constants.step_count);
            property_block.SetFloat(ao_thickness_ID, constants.thickness);
            property_block.SetInteger(ao_slice_count_ID, constants.slice_count);
            property_block.SetInteger(ao_samples_per_slice_ID, constants.samples_per_slice);
            property_block.SetInteger(ao_denoise_radius_ID, constants.denoise_radius);

            CoreUtils.DrawFullScreen(context.cmd, material, property_block);
        }

        internal static void Cleanup()
        {
            CoreUtils.Destroy(ssao_material);
            CoreUtils.Destroy(hbao_material);
            CoreUtils.Destroy(gtao_material);
            ssao_material = null;
            hbao_material = null;
            gtao_material = null;
            property_block = null;
            warned_missing_shader_names.Clear();
        }

        private struct Constants
        {
            public float radius;
            public float intensity;
            public float bias;
            public int sample_count;
            public int direction_count;
            public int step_count;
            public float thickness;
            public int slice_count;
            public int samples_per_slice;
            public int denoise_radius;
        }
    }
}
