using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace YutrelRP
{
    internal sealed class DDGIProbeTracePass
    {
        private const string ShaderPassName = "DDGIProbeTrace";
        private const string RayGenName = "RayGenDDGIProbeTrace";

        private static readonly ProfilingSampler sampler = new("DDGI Probe Trace");
        private static readonly int acceleration_structure_ID = Shader.PropertyToID("_DDGIAccelerationStructure");
        private static readonly int probe_ray_data_ID = Shader.PropertyToID("_DDGIProbeRayData");
        private static readonly int directional_light_count_ID = Shader.PropertyToID("_DirectionalLightCount");
        private static readonly int probe_bounds_min_ID = Shader.PropertyToID("_DDGIProbeBoundsMin");
        private static readonly int probe_spacing_ID = Shader.PropertyToID("_DDGIProbeSpacing");
        private static readonly int probe_count_ID = Shader.PropertyToID("_DDGIProbeCount");
        private static readonly int probe_max_ray_distance_ID = Shader.PropertyToID("_DDGIProbeMaxRayDistance");
        private static readonly int probe_ray_radiance_max_ID = Shader.PropertyToID("_DDGIProbeRayRadianceMax");
        private static readonly int probe_normal_bias_ID = Shader.PropertyToID("_DDGIProbeNormalBias");
        private static readonly int probe_view_bias_ID = Shader.PropertyToID("_DDGIProbeViewBias");
        private static readonly int irradiance_encoding_gamma_ID =
            Shader.PropertyToID("_DDGIIrradianceEncodingGamma");
        private static readonly int probe_irradiance_ID = Shader.PropertyToID("_DDGIProbeIrradiance");
        private static readonly int probe_distance_ID = Shader.PropertyToID("_DDGIProbeDistance");
        private static readonly int probe_ray_rotation_row0_ID = Shader.PropertyToID("_DDGIProbeRayRotationRow0");
        private static readonly int probe_ray_rotation_row1_ID = Shader.PropertyToID("_DDGIProbeRayRotationRow1");
        private static readonly int probe_ray_rotation_row2_ID = Shader.PropertyToID("_DDGIProbeRayRotationRow2");
        private static RayTracingShader probe_trace_shader;

        internal static void Record(RenderGraph render_graph, DDGIResources resources,
            LightResources light_resources, YutrelRayTracingWorld ray_tracing_world)
        {
            if (resources == null || !resources.is_valid || light_resources == null ||
                ray_tracing_world == null || !SystemInfo.supportsRayTracing)
            {
                return;
            }

            var volume = resources.active_volume;
            if (volume == null)
            {
                return;
            }

            var probe_count = volume.ProbeCount;
            var dispatch_width = (uint)volume.RaysPerProbe;
            var dispatch_height = (uint)(probe_count.x * probe_count.z);
            var dispatch_depth = (uint)probe_count.y;
            if (!ray_tracing_world.EnsureInitialized())
            {
                return;
            }

            if (!GraphicsSettings.TryGetRenderPipelineSettings<YutrelDDGIShaderResources>(out var shader_resources))
            {
                return;
            }

            probe_trace_shader ??= shader_resources.probe_trace_ray_tracing;
            if (probe_trace_shader == null)
            {
                return;
            }

            ray_tracing_world.SyncSceneIfNeeded(0xFFu);

            var ray_data_desc = new TextureDesc(volume.RaysPerProbe, probe_count.x * probe_count.z)
            {
                colorFormat = GraphicsFormat.R32G32_SFloat,
                dimension = TextureDimension.Tex2DArray,
                slices = probe_count.y,
                enableRandomWrite = true,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                clearBuffer = true,
                clearColor = new Color(0.0f, 1.0e27f, 0.0f, 0.0f),
                name = "DDGI Probe Ray Data"
            };
            resources.probe_ray_data = render_graph.CreateTexture(ray_data_desc);

            using var builder = render_graph.AddComputePass<DDGIProbeTracePass>(sampler.name, out var pass, sampler);
            var bounds = volume.WorldBounds;
            var probe_ray_rotation = ComputeProbeRayRotation((uint)Mathf.Max(Time.frameCount, 0));
            pass.shader = probe_trace_shader;
            pass.scene_accel_struct = ray_tracing_world.SceneAccelStruct;
            pass.probe_ray_data = resources.probe_ray_data;
            pass.probe_irradiance = resources.probe_irradiance;
            pass.probe_distance = resources.probe_distance;
            pass.directional_light_data_buffer = light_resources.directional_light_data_buffer;
            pass.directional_light_count = light_resources.directional_light_count;
            pass.probe_bounds_min = bounds.min;
            pass.probe_spacing = volume.GetWorldProbeSpacing();
            pass.probe_count = probe_count;
            pass.probe_max_ray_distance = volume.ProbeMaxRayDistance;
            pass.probe_ray_radiance_max = volume.ProbeRayRadianceMax;
            pass.probe_normal_bias = volume.ProbeNormalBias;
            pass.probe_view_bias = volume.ProbeViewBias;
            pass.irradiance_encoding_gamma = volume.IrradianceEncodingGamma;
            pass.probe_ray_rotation_row0 = new Vector4(probe_ray_rotation.m00, probe_ray_rotation.m01,
                probe_ray_rotation.m02, 0.0f);
            pass.probe_ray_rotation_row1 = new Vector4(probe_ray_rotation.m10, probe_ray_rotation.m11,
                probe_ray_rotation.m12, 0.0f);
            pass.probe_ray_rotation_row2 = new Vector4(probe_ray_rotation.m20, probe_ray_rotation.m21,
                probe_ray_rotation.m22, 0.0f);
            pass.dispatch_width = dispatch_width;
            pass.dispatch_height = dispatch_height;
            pass.dispatch_depth = dispatch_depth;
            builder.UseTexture(resources.probe_ray_data, AccessFlags.Write);
            builder.UseTexture(resources.probe_irradiance, AccessFlags.Read);
            builder.UseTexture(resources.probe_distance, AccessFlags.Read);
            builder.UseBuffer(light_resources.directional_light_data_buffer, AccessFlags.Read);
            builder.AllowPassCulling(false);
            builder.SetRenderFunc<DDGIProbeTracePass>(static (pass, context) => pass.Render(context));
        }

        internal static void Cleanup()
        {
            probe_trace_shader = null;
        }

        private RayTracingShader shader;
        private YutrelRayTracingAccelStruct scene_accel_struct;
        private TextureHandle probe_ray_data;
        private TextureHandle probe_irradiance;
        private TextureHandle probe_distance;
        private BufferHandle directional_light_data_buffer;
        private int directional_light_count;
        private Vector3 probe_bounds_min;
        private Vector3 probe_spacing;
        private Vector3Int probe_count;
        private float probe_max_ray_distance;
        private float probe_ray_radiance_max;
        private float probe_normal_bias;
        private float probe_view_bias;
        private float irradiance_encoding_gamma;
        private Vector4 probe_ray_rotation_row0;
        private Vector4 probe_ray_rotation_row1;
        private Vector4 probe_ray_rotation_row2;
        private uint dispatch_width;
        private uint dispatch_height;
        private uint dispatch_depth;

        private void Render(ComputeGraphContext context)
        {
            var cmd = context.cmd;
            scene_accel_struct.BuildIfNeeded(cmd);
            cmd.SetRayTracingShaderPass(shader, ShaderPassName);
            cmd.SetRayTracingAccelerationStructure(shader, acceleration_structure_ID,
                scene_accel_struct.AccelerationStructure);
            cmd.SetRayTracingTextureParam(shader, probe_ray_data_ID, probe_ray_data);
            cmd.SetRayTracingTextureParam(shader, probe_irradiance_ID, probe_irradiance);
            cmd.SetRayTracingTextureParam(shader, probe_distance_ID, probe_distance);
            cmd.SetRayTracingBufferParam(shader, LightResources.directional_light_data_ID,
                directional_light_data_buffer);
            cmd.SetRayTracingIntParam(shader, directional_light_count_ID, directional_light_count);
            cmd.SetRayTracingVectorParam(shader, probe_bounds_min_ID,
                new Vector4(probe_bounds_min.x, probe_bounds_min.y, probe_bounds_min.z, 0.0f));
            cmd.SetRayTracingVectorParam(shader, probe_spacing_ID,
                new Vector4(probe_spacing.x, probe_spacing.y, probe_spacing.z, 0.0f));
            cmd.SetRayTracingVectorParam(shader, probe_count_ID,
                new Vector4(probe_count.x, probe_count.y, probe_count.z, 0.0f));
            cmd.SetRayTracingFloatParam(shader, probe_max_ray_distance_ID, probe_max_ray_distance);
            cmd.SetRayTracingFloatParam(shader, probe_ray_radiance_max_ID, probe_ray_radiance_max);
            cmd.SetRayTracingFloatParam(shader, probe_normal_bias_ID, probe_normal_bias);
            cmd.SetRayTracingFloatParam(shader, probe_view_bias_ID, probe_view_bias);
            cmd.SetRayTracingFloatParam(shader, irradiance_encoding_gamma_ID, irradiance_encoding_gamma);
            cmd.SetRayTracingVectorParam(shader, probe_ray_rotation_row0_ID, probe_ray_rotation_row0);
            cmd.SetRayTracingVectorParam(shader, probe_ray_rotation_row1_ID, probe_ray_rotation_row1);
            cmd.SetRayTracingVectorParam(shader, probe_ray_rotation_row2_ID, probe_ray_rotation_row2);
            cmd.DispatchRays(shader, RayGenName, dispatch_width, dispatch_height, dispatch_depth, null);
        }

        private static Matrix4x4 ComputeProbeRayRotation(uint frame_index)
        {
            var u1 = 2.0f * Mathf.PI * Hash01(frame_index * 3u + 1u);
            var cos1 = Mathf.Cos(u1);
            var sin1 = Mathf.Sin(u1);

            var u2 = 2.0f * Mathf.PI * Hash01(frame_index * 3u + 2u);
            var cos2 = Mathf.Cos(u2);
            var sin2 = Mathf.Sin(u2);

            var u3 = Hash01(frame_index * 3u + 3u);
            var sq3 = 2.0f * Mathf.Sqrt(u3 * (1.0f - u3));

            var s2 = 2.0f * u3 * sin2 * sin2 - 1.0f;
            var c2 = 2.0f * u3 * cos2 * cos2 - 1.0f;
            var sc = 2.0f * u3 * sin2 * cos2;

            var matrix = Matrix4x4.identity;
            matrix.m00 = cos1 * c2 - sin1 * sc;
            matrix.m01 = sin1 * c2 + cos1 * sc;
            matrix.m02 = sq3 * cos2;
            matrix.m10 = cos1 * sc - sin1 * s2;
            matrix.m11 = sin1 * sc + cos1 * s2;
            matrix.m12 = sq3 * sin2;
            matrix.m20 = cos1 * (sq3 * cos2) - sin1 * (sq3 * sin2);
            matrix.m21 = sin1 * (sq3 * cos2) + cos1 * (sq3 * sin2);
            matrix.m22 = 1.0f - 2.0f * u3;
            return matrix;
        }

        private static float Hash01(uint value)
        {
            value ^= 2747636419u;
            value *= 2654435769u;
            value ^= value >> 16;
            value *= 2654435769u;
            value ^= value >> 16;
            return (value & 0x00FFFFFFu) / 16777216.0f;
        }
    }
}
