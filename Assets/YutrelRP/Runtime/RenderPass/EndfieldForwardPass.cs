using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RendererUtils;
using UnityEngine.Rendering.RenderGraphModule;

namespace YutrelRP
{
    internal class EndfieldForwardPass
    {
        private const int environment_diffuse_none = 0;
        private const int environment_diffuse_sh = 1;
        private const int environment_diffuse_ddgi = 2;

        private static readonly ProfilingSampler sampler = new("Endfield Forward Pass");
        private static readonly ShaderTagId shader_tag_id = new("EndfieldForward");
        private static readonly int directional_light_count_ID = Shader.PropertyToID("_DirectionalLightCount");
        private static readonly int environment_diffuse_mode_ID = Shader.PropertyToID("_EnvironmentDiffuseMode");
        private static readonly int environment_specular_enabled_ID =
            Shader.PropertyToID("_EnvironmentSpecularEnabled");
        private static readonly int probe_irradiance_ID = Shader.PropertyToID("_DDGIProbeIrradiance");
        private static readonly int probe_distance_ID = Shader.PropertyToID("_DDGIProbeDistance");
        private static readonly int probe_data_ID = Shader.PropertyToID("_DDGIProbeData");
        private static readonly int probe_bounds_min_ID = Shader.PropertyToID("_DDGIProbeBoundsMin");
        private static readonly int probe_spacing_ID = Shader.PropertyToID("_DDGIProbeSpacing");
        private static readonly int probe_count_ID = Shader.PropertyToID("_DDGIProbeCount");
        private static readonly int probe_normal_bias_ID = Shader.PropertyToID("_DDGIProbeNormalBias");
        private static readonly int probe_view_bias_ID = Shader.PropertyToID("_DDGIProbeViewBias");
        private static readonly int lighting_intensity_scale_ID =
            Shader.PropertyToID("_DDGILightingIntensityScale");
        private static readonly int irradiance_encoding_gamma_ID =
            Shader.PropertyToID("_DDGIIrradianceEncodingGamma");
        private static readonly int probe_relocation_enabled_ID =
            Shader.PropertyToID("_DDGIProbeRelocationEnabled");
        private static readonly int probe_classification_enabled_ID =
            Shader.PropertyToID("_DDGIProbeClassificationEnabled");

        private static readonly int[] ibl_sh_IDs =
        {
            Shader.PropertyToID("_IblSH0"),
            Shader.PropertyToID("_IblSH1"),
            Shader.PropertyToID("_IblSH2"),
            Shader.PropertyToID("_IblSH3"),
            Shader.PropertyToID("_IblSH4"),
            Shader.PropertyToID("_IblSH5"),
            Shader.PropertyToID("_IblSH6"),
            Shader.PropertyToID("_IblSH7"),
            Shader.PropertyToID("_IblSH8")
        };

        private static bool warned_missing_dfg_lut;

        internal static void Record(RenderGraph render_graph, Camera camera, CullingResults culling_results,
            RenderTargets textures, LightResources light_resources, DDGIResources ddgi_resources,
            ResolvedDDGISettings ddgi_settings)
        {
            if (!light_resources.has_DFG_LUT)
            {
                if (!warned_missing_dfg_lut)
                {
                    Debug.LogError("YutrelRP: EndfieldForwardPass skipped because the fixed DFG LUT is missing at Resources/Texture/DFG_LUT.");
                    warned_missing_dfg_lut = true;
                }

                return;
            }

            var has_DDGI = HasValidDDGIResources(ddgi_resources, ddgi_settings);
            var environment_diffuse_mode = has_DDGI
                ? environment_diffuse_ddgi
                : light_resources.has_environment_reflection
                    ? environment_diffuse_sh
                    : environment_diffuse_none;
            var environment_specular_enabled = light_resources.has_environment_reflection;
            if (light_resources.directional_light_count == 0 &&
                environment_diffuse_mode == environment_diffuse_none &&
                !environment_specular_enabled)
            {
                return;
            }

            using var builder =
                render_graph.AddRasterRenderPass<EndfieldForwardPass>(sampler.name, out var pass, sampler);

            var renderer_desc = new RendererListDesc(shader_tag_id, culling_results, camera)
            {
                sortingCriteria = SortingCriteria.CommonOpaque,
                renderQueueRange = RenderQueueRange.opaque
            };
            pass.renderer_list = render_graph.CreateRendererList(renderer_desc);
            pass.directional_light_count = light_resources.directional_light_count;
            pass.directional_light_data_buffer = light_resources.directional_light_data_buffer;
            pass.shadow_mask = textures.shadow_mask;
            pass.DFG_LUT = light_resources.DFG_LUT;
            pass.environment_diffuse_mode = environment_diffuse_mode;
            pass.environment_specular_enabled = environment_specular_enabled;
            pass.environment_reflection_cube = light_resources.environment_reflection_cube;
            pass.environment_reflection_cube_hdr = light_resources.environment_reflection_cube_hdr;
            pass.environment_intensity = light_resources.environment_intensity;
            pass.environment_diffuse_multiplier = light_resources.environment_diffuse_multiplier;
            pass.environment_specular_multiplier = light_resources.environment_specular_multiplier;
            pass.ibl_roughness_one_level = light_resources.ibl_roughness_one_level;
            pass.ambient_probe = light_resources.environment_diffuse_sh;

            builder.UseRendererList(pass.renderer_list);
            builder.UseTexture(pass.DFG_LUT);
            if (pass.directional_light_count > 0)
            {
                builder.UseBuffer(pass.directional_light_data_buffer);
                builder.UseTexture(pass.shadow_mask);
            }

            if (pass.environment_specular_enabled)
            {
                builder.UseTexture(pass.environment_reflection_cube);
            }

            if (has_DDGI)
            {
                var volume = ddgi_resources.active_volume;
                var bounds = volume.WorldBounds;
                pass.probe_irradiance = ddgi_resources.probe_irradiance;
                pass.probe_distance = ddgi_resources.probe_distance;
                pass.probe_data = ddgi_resources.probe_data;
                pass.probe_bounds_min = bounds.min;
                pass.probe_spacing = volume.GetWorldProbeSpacing();
                pass.probe_count = volume.ProbeCount;
                pass.probe_normal_bias = ddgi_settings.sampling.probeNormalBias;
                pass.probe_view_bias = ddgi_settings.sampling.probeViewBias;
                pass.lighting_intensity_scale = ddgi_settings.encoding.lightingIntensityScale;
                pass.irradiance_encoding_gamma = ddgi_settings.encoding.irradianceEncodingGamma;
                pass.probe_relocation_enabled = ddgi_settings.relocation.enabled ? 1 : 0;
                pass.probe_classification_enabled =
                    ddgi_settings.classification.enabled &&
                    volume.RaysPerProbe > DDGIResources.FixedRayCount
                        ? 1
                        : 0;

                builder.UseTexture(pass.probe_irradiance);
                builder.UseTexture(pass.probe_distance);
                builder.UseTexture(pass.probe_data);
            }

            builder.SetRenderAttachment(textures.scene_color, 0, AccessFlags.ReadWrite);
            builder.SetRenderAttachmentDepth(textures.scene_depth, AccessFlags.ReadWrite);
            builder.AllowGlobalStateModification(true);
            builder.SetRenderFunc<EndfieldForwardPass>(static (pass, context) => pass.Render(context));
        }

        private RendererListHandle renderer_list;
        private int directional_light_count;
        private BufferHandle directional_light_data_buffer;
        private TextureHandle shadow_mask;
        private TextureHandle DFG_LUT;
        private int environment_diffuse_mode;
        private bool environment_specular_enabled;
        private TextureHandle environment_reflection_cube;
        private Vector4 environment_reflection_cube_hdr;
        private float environment_intensity;
        private float environment_diffuse_multiplier;
        private float environment_specular_multiplier;
        private float ibl_roughness_one_level;
        private SphericalHarmonicsL2 ambient_probe;
        private TextureHandle probe_irradiance;
        private TextureHandle probe_distance;
        private TextureHandle probe_data;
        private Vector3 probe_bounds_min;
        private Vector3 probe_spacing;
        private Vector3Int probe_count;
        private float probe_normal_bias;
        private float probe_view_bias;
        private float lighting_intensity_scale;
        private float irradiance_encoding_gamma;
        private int probe_relocation_enabled;
        private int probe_classification_enabled;

        private void Render(RasterGraphContext context)
        {
            context.cmd.SetGlobalInt(directional_light_count_ID, directional_light_count);
            if (directional_light_count > 0)
            {
                context.cmd.SetGlobalBuffer(LightResources.directional_light_data_ID, directional_light_data_buffer);
                context.cmd.SetGlobalTexture(RenderTargets.shadow_mask_ID, shadow_mask);
            }

            context.cmd.SetGlobalTexture(LightResources.dfg_lut_ID, DFG_LUT);
            context.cmd.SetGlobalInt(environment_diffuse_mode_ID, environment_diffuse_mode);
            context.cmd.SetGlobalInt(environment_specular_enabled_ID, environment_specular_enabled ? 1 : 0);
            context.cmd.SetGlobalVector(
                LightResources.environment_reflection_cube_hdr_ID,
                environment_reflection_cube_hdr);
            context.cmd.SetGlobalFloat(LightResources.environment_intensity_ID, environment_intensity);
            context.cmd.SetGlobalFloat(
                LightResources.environment_diffuse_multiplier_ID,
                environment_diffuse_multiplier);
            context.cmd.SetGlobalFloat(
                LightResources.environment_specular_multiplier_ID,
                environment_specular_multiplier);
            context.cmd.SetGlobalFloat(LightResources.ibl_roughness_one_level_ID, ibl_roughness_one_level);

            if (environment_specular_enabled)
            {
                context.cmd.SetGlobalTexture(
                    LightResources.environment_reflection_cube_ID,
                    environment_reflection_cube);
            }

            if (environment_diffuse_mode == environment_diffuse_sh)
            {
                SetIblShShaderConstants(context, ambient_probe);
            }
            else if (environment_diffuse_mode == environment_diffuse_ddgi)
            {
                context.cmd.SetGlobalTexture(probe_irradiance_ID, probe_irradiance);
                context.cmd.SetGlobalTexture(probe_distance_ID, probe_distance);
                context.cmd.SetGlobalTexture(probe_data_ID, probe_data);
                context.cmd.SetGlobalVector(probe_bounds_min_ID, probe_bounds_min);
                context.cmd.SetGlobalVector(probe_spacing_ID, probe_spacing);
                context.cmd.SetGlobalVector(
                    probe_count_ID,
                    new Vector4(probe_count.x, probe_count.y, probe_count.z, 0.0f));
                context.cmd.SetGlobalFloat(probe_normal_bias_ID, probe_normal_bias);
                context.cmd.SetGlobalFloat(probe_view_bias_ID, probe_view_bias);
                context.cmd.SetGlobalFloat(lighting_intensity_scale_ID, lighting_intensity_scale);
                context.cmd.SetGlobalFloat(irradiance_encoding_gamma_ID, irradiance_encoding_gamma);
                context.cmd.SetGlobalInt(probe_relocation_enabled_ID, probe_relocation_enabled);
                context.cmd.SetGlobalInt(probe_classification_enabled_ID, probe_classification_enabled);
            }

            context.cmd.DrawRendererList(renderer_list);
        }

        private static bool HasValidDDGIResources(DDGIResources resources, ResolvedDDGISettings settings)
        {
            return settings.enabled && resources != null && resources.is_valid && resources.active_volume != null &&
                   resources.probe_irradiance.IsValid() && resources.probe_distance.IsValid() &&
                   resources.probe_data.IsValid();
        }

        private static void SetIblShShaderConstants(RasterGraphContext context, SphericalHarmonicsL2 sh)
        {
            for (var coefficient = 0;
                 coefficient < YutrelIBLAsset.diffuseIrradianceShCoefficientCount;
                 coefficient++)
            {
                context.cmd.SetGlobalVector(
                    ibl_sh_IDs[coefficient],
                    new Vector4(sh[0, coefficient], sh[1, coefficient], sh[2, coefficient], 0.0f));
            }
        }
    }
}
