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
        private static RayTracingShader probe_trace_shader;
        private static Material probe_trace_fallback_material;
        private static Shader probe_trace_fallback_shader;

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
            if (probe_trace_shader == null ||
                !EnsureFallbackMaterial(shader_resources.probe_trace_fallback_shader))
            {
                return;
            }

            var build_config = new YutrelRayTracingBuildConfig(
                probe_trace_fallback_material,
                RayTracingSubMeshFlags.Enabled | RayTracingSubMeshFlags.ClosestHitOnly,
                0xFFu);
            ray_tracing_world.SyncSceneIfNeeded(build_config);

            using var builder = render_graph.AddComputePass<DDGIProbeTracePass>(sampler.name, out var pass, sampler);
            var bounds = volume.WorldBounds;
            pass.shader = probe_trace_shader;
            pass.scene_accel_struct = ray_tracing_world.SceneAccelStruct;
            pass.probe_ray_data = resources.probe_ray_data;
            pass.directional_light_data_buffer = light_resources.directional_light_data_buffer;
            pass.directional_light_count = light_resources.directional_light_count;
            pass.probe_bounds_min = bounds.min;
            pass.probe_spacing = volume.GetWorldProbeSpacing();
            pass.probe_count = probe_count;
            pass.probe_max_ray_distance = volume.ProbeMaxRayDistance;
            pass.probe_ray_radiance_max = volume.ProbeRayRadianceMax;
            pass.probe_normal_bias = volume.ProbeNormalBias;
            pass.dispatch_width = dispatch_width;
            pass.dispatch_height = dispatch_height;
            pass.dispatch_depth = dispatch_depth;
            builder.UseTexture(resources.probe_ray_data, AccessFlags.Write);
            builder.UseBuffer(light_resources.directional_light_data_buffer, AccessFlags.Read);
            builder.AllowPassCulling(false);
            builder.SetRenderFunc<DDGIProbeTracePass>(static (pass, context) => pass.Render(context));
        }

        internal static void Cleanup()
        {
            probe_trace_shader = null;
            CoreUtils.Destroy(probe_trace_fallback_material);
            probe_trace_fallback_material = null;
            probe_trace_fallback_shader = null;
        }

        private RayTracingShader shader;
        private YutrelRayTracingAccelStruct scene_accel_struct;
        private TextureHandle probe_ray_data;
        private BufferHandle directional_light_data_buffer;
        private int directional_light_count;
        private Vector3 probe_bounds_min;
        private Vector3 probe_spacing;
        private Vector3Int probe_count;
        private float probe_max_ray_distance;
        private float probe_ray_radiance_max;
        private float probe_normal_bias;
        private uint dispatch_width;
        private uint dispatch_height;
        private uint dispatch_depth;

        private static bool EnsureFallbackMaterial(Shader shader)
        {
            if (shader == null)
            {
                return false;
            }

            if (probe_trace_fallback_material != null && probe_trace_fallback_shader == shader)
            {
                return true;
            }

            CoreUtils.Destroy(probe_trace_fallback_material);
            probe_trace_fallback_shader = shader;
            probe_trace_fallback_material = CoreUtils.CreateEngineMaterial(shader);
            return probe_trace_fallback_material != null;
        }

        private void Render(ComputeGraphContext context)
        {
            var cmd = context.cmd;
            scene_accel_struct.BuildIfNeeded(cmd);
            cmd.SetRayTracingShaderPass(shader, ShaderPassName);
            cmd.SetRayTracingAccelerationStructure(shader, acceleration_structure_ID,
                scene_accel_struct.AccelerationStructure);
            cmd.SetRayTracingTextureParam(shader, probe_ray_data_ID, probe_ray_data);
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
            cmd.DispatchRays(shader, RayGenName, dispatch_width, dispatch_height, dispatch_depth, null);
        }
    }
}
