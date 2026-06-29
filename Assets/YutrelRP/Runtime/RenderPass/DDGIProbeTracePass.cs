using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.UnifiedRayTracing;

namespace YutrelRP
{
    internal sealed class DDGIProbeTracePass
    {
        private const string AccelStructName = "_DDGIAccelStruct";

        private static readonly ProfilingSampler sampler = new("DDGI Probe Trace");
        private static readonly int probe_ray_data_ID = Shader.PropertyToID("_DDGIProbeRayData");
        private static readonly int directional_light_count_ID = Shader.PropertyToID("_DirectionalLightCount");
        private static readonly int probe_bounds_min_ID = Shader.PropertyToID("_DDGIProbeBoundsMin");
        private static readonly int probe_spacing_ID = Shader.PropertyToID("_DDGIProbeSpacing");
        private static readonly int probe_count_ID = Shader.PropertyToID("_DDGIProbeCount");
        private static readonly int probe_max_ray_distance_ID = Shader.PropertyToID("_DDGIProbeMaxRayDistance");
        private static readonly int probe_ray_radiance_max_ID = Shader.PropertyToID("_DDGIProbeRayRadianceMax");
        private static readonly int probe_normal_bias_ID = Shader.PropertyToID("_DDGIProbeNormalBias");
        private static IRayTracingShader probe_trace_shader;
        private static GraphicsBuffer trace_scratch_buffer;

        internal static void Record(RenderGraph render_graph, DDGIResources resources,
            LightResources light_resources, YutrelRayTracingContext ray_tracing_context,
            YutrelRayTracingWorld ray_tracing_world)
        {
            if (resources == null || !resources.is_valid || light_resources == null ||
                ray_tracing_context == null || ray_tracing_world == null)
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
            if (!ray_tracing_context.EnsureInitialized() || !ray_tracing_world.EnsureInitialized(ray_tracing_context))
            {
                return;
            }

            if (!GraphicsSettings.TryGetRenderPipelineSettings<YutrelDDGIShaderResources>(out var shader_resources))
            {
                return;
            }

            probe_trace_shader ??= ray_tracing_context.CreateUnifiedShader(
                shader_resources.probe_trace_compute, shader_resources.probe_trace_ray_tracing);
            if (probe_trace_shader == null)
            {
                return;
            }

            RayTracingHelper.ResizeScratchBufferForTrace(probe_trace_shader, dispatch_width, dispatch_height,
                dispatch_depth, ref trace_scratch_buffer);
            ray_tracing_world.MarkSceneDirty();
            ray_tracing_world.SyncSceneIfNeeded();

            using var builder = render_graph.AddUnsafePass<DDGIProbeTracePass>(sampler.name, out var pass, sampler);
            var bounds = volume.WorldBounds;
            pass.shader = probe_trace_shader;
            pass.scene_accel_struct = ray_tracing_world.SceneAccelStruct;
            pass.trace_scratch = trace_scratch_buffer;
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
            trace_scratch_buffer?.Dispose();
            trace_scratch_buffer = null;
        }

        private IRayTracingShader shader;
        private YutrelRayTracingAccelStruct scene_accel_struct;
        private GraphicsBuffer trace_scratch;
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

        private void Render(UnsafeGraphContext context)
        {
            var cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
            scene_accel_struct.BuildIfNeeded(cmd);
            scene_accel_struct.Bind(cmd, AccelStructName, shader);
            shader.SetTextureParam(cmd, probe_ray_data_ID, probe_ray_data);
            shader.SetBufferParam(cmd, LightResources.directional_light_data_ID, directional_light_data_buffer);
            shader.SetIntParam(cmd, directional_light_count_ID, directional_light_count);
            shader.SetVectorParam(cmd, probe_bounds_min_ID, probe_bounds_min);
            shader.SetVectorParam(cmd, probe_spacing_ID, probe_spacing);
            shader.SetVectorParam(cmd, probe_count_ID,
                new Vector4(probe_count.x, probe_count.y, probe_count.z, 0.0f));
            shader.SetFloatParam(cmd, probe_max_ray_distance_ID, probe_max_ray_distance);
            shader.SetFloatParam(cmd, probe_ray_radiance_max_ID, probe_ray_radiance_max);
            shader.SetFloatParam(cmd, probe_normal_bias_ID, probe_normal_bias);
            shader.Dispatch(cmd, trace_scratch, dispatch_width, dispatch_height, dispatch_depth);
        }
    }
}
