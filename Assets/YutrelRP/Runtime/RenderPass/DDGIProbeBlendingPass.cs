using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace YutrelRP
{
    internal sealed class DDGIProbeBlendingPass
    {
        private const string BlendIrradianceKernelName = "BlendIrradiance";
        private const string BlendDistanceKernelName = "BlendDistance";

        private static readonly ProfilingSampler blend_irradiance_sampler = new("DDGI Probe Blend Irradiance");
        private static readonly ProfilingSampler blend_distance_sampler = new("DDGI Probe Blend Distance");

        private static readonly int probe_ray_data_ID = Shader.PropertyToID("_DDGIProbeRayData");
        private static readonly int probe_irradiance_ID = Shader.PropertyToID("_DDGIProbeIrradiance");
        private static readonly int probe_distance_ID = Shader.PropertyToID("_DDGIProbeDistance");
        private static readonly int probe_count_ID = Shader.PropertyToID("_DDGIProbeCount");
        private static readonly int rays_per_probe_ID = Shader.PropertyToID("_DDGIRaysPerProbe");
        private static readonly int probe_spacing_ID = Shader.PropertyToID("_DDGIProbeSpacing");
        private static readonly int probe_hysteresis_ID = Shader.PropertyToID("_DDGIProbeHysteresis");
        private static readonly int irradiance_encoding_gamma_ID =
            Shader.PropertyToID("_DDGIIrradianceEncodingGamma");
        private static readonly int distance_exponent_ID = Shader.PropertyToID("_DDGIDistanceExponent");
        private static readonly int irradiance_threshold_ID = Shader.PropertyToID("_DDGIIrradianceThreshold");
        private static readonly int brightness_threshold_ID = Shader.PropertyToID("_DDGIBrightnessThreshold");
        private static readonly int probe_random_ray_backface_threshold_ID =
            Shader.PropertyToID("_DDGIProbeRandomRayBackfaceThreshold");
        private static readonly int probe_ray_rotation_row0_ID = Shader.PropertyToID("_DDGIProbeRayRotationRow0");
        private static readonly int probe_ray_rotation_row1_ID = Shader.PropertyToID("_DDGIProbeRayRotationRow1");
        private static readonly int probe_ray_rotation_row2_ID = Shader.PropertyToID("_DDGIProbeRayRotationRow2");

        internal static void Record(RenderGraph render_graph, DDGIResources resources)
        {
            if (resources == null || !resources.is_valid || !resources.probe_ray_data.IsValid() ||
                !resources.probe_irradiance.IsValid() || !resources.probe_distance.IsValid())
            {
                return;
            }

            if (!TryGetBlendingShader(out var shader))
            {
                return;
            }

            var volume = resources.active_volume;
            if (volume == null)
            {
                return;
            }

            RecordKernel(render_graph, resources, volume, shader, BlendIrradianceKernelName,
                blend_irradiance_sampler, KernelMode.BlendIrradiance);
            RecordKernel(render_graph, resources, volume, shader, BlendDistanceKernelName,
                blend_distance_sampler, KernelMode.BlendDistance);
        }

        private static void RecordKernel(RenderGraph render_graph, DDGIResources resources, YutrelDDGIVolume volume,
            ComputeShader shader, string kernel_name, ProfilingSampler sampler, KernelMode mode)
        {
            using var builder = render_graph.AddComputePass<DDGIProbeBlendingPass>(sampler.name, out var pass, sampler);
            pass.shader = shader;
            pass.kernel = shader.FindKernel(kernel_name);
            pass.mode = mode;
            pass.probe_ray_data = resources.probe_ray_data;
            pass.probe_irradiance = resources.probe_irradiance;
            pass.probe_distance = resources.probe_distance;
            pass.probe_count = volume.ProbeCount;
            pass.probe_ray_rotation_row0 = resources.probe_ray_rotation_row0;
            pass.probe_ray_rotation_row1 = resources.probe_ray_rotation_row1;
            pass.probe_ray_rotation_row2 = resources.probe_ray_rotation_row2;
            pass.rays_per_probe = volume.RaysPerProbe;
            pass.probe_spacing = volume.GetWorldProbeSpacing();
            pass.probe_hysteresis = volume.ProbeHysteresis;
            pass.irradiance_encoding_gamma = volume.IrradianceEncodingGamma;
            pass.distance_exponent = volume.DistanceExponent;
            pass.irradiance_threshold = volume.IrradianceThreshold;
            pass.brightness_threshold = volume.BrightnessThreshold;
            pass.probe_random_ray_backface_threshold = volume.ProbeRandomRayBackfaceThreshold;

            builder.UseTexture(pass.probe_ray_data, AccessFlags.Read);

            if (mode == KernelMode.BlendIrradiance)
            {
                builder.UseTexture(pass.probe_irradiance, AccessFlags.ReadWrite);
            }

            if (mode == KernelMode.BlendDistance)
            {
                builder.UseTexture(pass.probe_distance, AccessFlags.ReadWrite);
            }

            builder.AllowPassCulling(false);
            builder.SetRenderFunc<DDGIProbeBlendingPass>(static (pass, context) => pass.Render(context));
        }

        private ComputeShader shader;
        private int kernel;
        private KernelMode mode;
        private TextureHandle probe_ray_data;
        private TextureHandle probe_irradiance;
        private TextureHandle probe_distance;
        private Vector3Int probe_count;
        private Vector4 probe_ray_rotation_row0;
        private Vector4 probe_ray_rotation_row1;
        private Vector4 probe_ray_rotation_row2;
        private int rays_per_probe;
        private Vector3 probe_spacing;
        private float probe_hysteresis;
        private float irradiance_encoding_gamma;
        private float distance_exponent;
        private float irradiance_threshold;
        private float brightness_threshold;
        private float probe_random_ray_backface_threshold;

        private void Render(ComputeGraphContext context)
        {
            var cmd = context.cmd;
            SetCommonParams(cmd);

            switch (mode)
            {
                case KernelMode.BlendIrradiance:
                    cmd.SetComputeTextureParam(shader, kernel, probe_ray_data_ID, probe_ray_data);
                    cmd.SetComputeTextureParam(shader, kernel, probe_irradiance_ID, probe_irradiance);
                    Dispatch(cmd);
                    break;
                case KernelMode.BlendDistance:
                    cmd.SetComputeTextureParam(shader, kernel, probe_ray_data_ID, probe_ray_data);
                    cmd.SetComputeTextureParam(shader, kernel, probe_distance_ID, probe_distance);
                    Dispatch(cmd);
                    break;
            }
        }

        private void SetCommonParams(ComputeCommandBuffer cmd)
        {
            cmd.SetComputeVectorParam(shader, probe_count_ID,
                new Vector4(probe_count.x, probe_count.y, probe_count.z, 0.0f));
            cmd.SetComputeFloatParam(shader, rays_per_probe_ID, rays_per_probe);
            cmd.SetComputeVectorParam(shader, probe_spacing_ID,
                new Vector4(probe_spacing.x, probe_spacing.y, probe_spacing.z, 0.0f));
            cmd.SetComputeVectorParam(shader, probe_ray_rotation_row0_ID, probe_ray_rotation_row0);
            cmd.SetComputeVectorParam(shader, probe_ray_rotation_row1_ID, probe_ray_rotation_row1);
            cmd.SetComputeVectorParam(shader, probe_ray_rotation_row2_ID, probe_ray_rotation_row2);
            cmd.SetComputeFloatParam(shader, probe_hysteresis_ID, probe_hysteresis);
            cmd.SetComputeFloatParam(shader, irradiance_encoding_gamma_ID, irradiance_encoding_gamma);
            cmd.SetComputeFloatParam(shader, distance_exponent_ID, distance_exponent);
            cmd.SetComputeFloatParam(shader, irradiance_threshold_ID, irradiance_threshold);
            cmd.SetComputeFloatParam(shader, brightness_threshold_ID, brightness_threshold);
            cmd.SetComputeFloatParam(shader, probe_random_ray_backface_threshold_ID,
                probe_random_ray_backface_threshold);
        }

        private void Dispatch(ComputeCommandBuffer cmd)
        {
            cmd.DispatchCompute(shader, kernel, probe_count.x, probe_count.z, probe_count.y);
        }

        private static bool TryGetBlendingShader(out ComputeShader shader)
        {
            shader = null;

            if (!GraphicsSettings.TryGetRenderPipelineSettings<YutrelDDGIShaderResources>(out var resources))
            {
                return false;
            }

            shader = resources.probe_blending;
            return shader != null;
        }

        private enum KernelMode
        {
            BlendIrradiance,
            BlendDistance
        }
    }
}
