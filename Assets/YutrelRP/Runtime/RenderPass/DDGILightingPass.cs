using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace YutrelRP
{
    internal sealed class DDGILightingPass
    {
        private static readonly ProfilingSampler sampler = new("DDGI Lighting Pass");
        private static readonly int probe_irradiance_ID = Shader.PropertyToID("_DDGIProbeIrradiance");
        private static readonly int probe_distance_ID = Shader.PropertyToID("_DDGIProbeDistance");
        private static readonly int probe_data_ID = Shader.PropertyToID("_DDGIProbeData");
        private static readonly int probe_bounds_min_ID = Shader.PropertyToID("_DDGIProbeBoundsMin");
        private static readonly int probe_spacing_ID = Shader.PropertyToID("_DDGIProbeSpacing");
        private static readonly int probe_count_ID = Shader.PropertyToID("_DDGIProbeCount");
        private static readonly int probe_normal_bias_ID = Shader.PropertyToID("_DDGIProbeNormalBias");
        private static readonly int probe_view_bias_ID = Shader.PropertyToID("_DDGIProbeViewBias");
        private static readonly int probe_ray_radiance_max_ID = Shader.PropertyToID("_DDGIProbeRayRadianceMax");
        private static readonly int irradiance_encoding_gamma_ID =
            Shader.PropertyToID("_DDGIIrradianceEncodingGamma");
        private static readonly int probe_relocation_enabled_ID = Shader.PropertyToID("_DDGIProbeRelocationEnabled");

        private static Material material;
        private static MaterialPropertyBlock property_block;

        internal static void Record(RenderGraph render_graph, RenderTargets textures, DDGIResources resources)
        {
            if (render_graph == null || textures == null || resources == null || !resources.is_valid ||
                resources.active_volume == null || !textures.scene_color.IsValid() ||
                !textures.GBuffer_A.IsValid() || !textures.GBuffer_B.IsValid() ||
                !textures.GBuffer_C.IsValid() || !textures.scene_depth.IsValid() ||
                !resources.probe_irradiance.IsValid() || !resources.probe_distance.IsValid())
            {
                return;
            }

            if (!TryEnsureMaterial())
            {
                return;
            }

            property_block ??= new MaterialPropertyBlock();

            using var builder = render_graph.AddRasterRenderPass<DDGILightingPass>(
                sampler.name, out var pass, sampler);

            var volume = resources.active_volume;
            var bounds = volume.WorldBounds;
            var probe_count = volume.ProbeCount;
            var probe_spacing = volume.GetWorldProbeSpacing();

            pass.GBuffer_A_ID = RenderTargets.GBuffer_A_ID;
            pass.GBuffer_B_ID = RenderTargets.GBuffer_B_ID;
            pass.GBuffer_C_ID = RenderTargets.GBuffer_C_ID;
            pass.scene_depth_ID = RenderTargets.scene_depth_ID;
            pass.GBuffer_A = textures.GBuffer_A;
            pass.GBuffer_B = textures.GBuffer_B;
            pass.GBuffer_C = textures.GBuffer_C;
            pass.scene_depth = textures.scene_depth;
            pass.probe_irradiance = resources.probe_irradiance;
            pass.probe_distance = resources.probe_distance;
            pass.probe_data = resources.probe_data;
            pass.probe_bounds_min = bounds.min;
            pass.probe_spacing = probe_spacing;
            pass.probe_count = probe_count;
            pass.probe_normal_bias = volume.ProbeNormalBias;
            pass.probe_view_bias = volume.ProbeViewBias;
            pass.probe_ray_radiance_max = volume.ProbeRayRadianceMax;
            pass.irradiance_encoding_gamma = volume.IrradianceEncodingGamma;
            pass.probe_relocation_enabled = volume.ProbeRelocationEnabled ? 1 : 0;

            builder.UseTexture(pass.GBuffer_A);
            builder.UseTexture(pass.GBuffer_B);
            builder.UseTexture(pass.GBuffer_C);
            builder.UseTexture(pass.scene_depth);
            builder.UseTexture(pass.probe_irradiance);
            builder.UseTexture(pass.probe_distance);
            builder.UseTexture(pass.probe_data);
            builder.SetRenderAttachment(textures.scene_color, 0, AccessFlags.ReadWrite);
            builder.SetRenderFunc<DDGILightingPass>(static (pass, context) => pass.Render(context));
        }

        internal static void Cleanup()
        {
            CoreUtils.Destroy(material);
            material = null;
            property_block = null;
        }

        private int GBuffer_A_ID;
        private int GBuffer_B_ID;
        private int GBuffer_C_ID;
        private int scene_depth_ID;

        private TextureHandle GBuffer_A;
        private TextureHandle GBuffer_B;
        private TextureHandle GBuffer_C;
        private TextureHandle scene_depth;
        private TextureHandle probe_irradiance;
        private TextureHandle probe_distance;
        private TextureHandle probe_data;
        private Vector3 probe_bounds_min;
        private Vector3 probe_spacing;
        private Vector3Int probe_count;
        private float probe_normal_bias;
        private float probe_view_bias;
        private float probe_ray_radiance_max;
        private float irradiance_encoding_gamma;
        private int probe_relocation_enabled;

        private void Render(RasterGraphContext context)
        {
            property_block.Clear();
            property_block.SetTexture(GBuffer_A_ID, GBuffer_A);
            property_block.SetTexture(GBuffer_B_ID, GBuffer_B);
            property_block.SetTexture(GBuffer_C_ID, GBuffer_C);
            property_block.SetTexture(scene_depth_ID, scene_depth);
            property_block.SetTexture(probe_irradiance_ID, probe_irradiance);
            property_block.SetTexture(probe_distance_ID, probe_distance);
            property_block.SetTexture(probe_data_ID, probe_data);
            property_block.SetVector(probe_bounds_min_ID, probe_bounds_min);
            property_block.SetVector(probe_spacing_ID, probe_spacing);
            property_block.SetVector(probe_count_ID,
                new Vector4(probe_count.x, probe_count.y, probe_count.z, 0.0f));
            property_block.SetFloat(probe_normal_bias_ID, probe_normal_bias);
            property_block.SetFloat(probe_view_bias_ID, probe_view_bias);
            property_block.SetFloat(probe_ray_radiance_max_ID, probe_ray_radiance_max);
            property_block.SetFloat(irradiance_encoding_gamma_ID, irradiance_encoding_gamma);
            property_block.SetInt(probe_relocation_enabled_ID, probe_relocation_enabled);

            CoreUtils.DrawFullScreen(context.cmd, material, property_block);
        }

        private static bool TryEnsureMaterial()
        {
            if (!GraphicsSettings.TryGetRenderPipelineSettings<YutrelDDGIShaderResources>(out var resources) ||
                resources == null)
            {
                YutrelRPRuntimeShaderUtility.WarnMissingResourceOnce(nameof(YutrelDDGIShaderResources));
                return false;
            }

            return YutrelRPRuntimeShaderUtility.TryCreateMaterial(
                resources.lighting,
                nameof(YutrelDDGIShaderResources.lighting),
                ref material);
        }
    }
}
