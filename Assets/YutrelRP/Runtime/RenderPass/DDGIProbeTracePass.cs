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
        private static IRayTracingShader probe_trace_shader;
        private static GraphicsBuffer trace_scratch_buffer;

        internal static void Record(RenderGraph render_graph, DDGIResources resources,
            YutrelRayTracingContext ray_tracing_context, YutrelRayTracingWorld ray_tracing_world)
        {
            if (resources == null || !resources.is_valid || ray_tracing_context == null || ray_tracing_world == null)
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
            ray_tracing_world.SyncSceneIfNeeded();

            using var builder = render_graph.AddUnsafePass<DDGIProbeTracePass>(sampler.name, out var pass, sampler);
            pass.shader = probe_trace_shader;
            pass.scene_accel_struct = ray_tracing_world.SceneAccelStruct;
            pass.trace_scratch = trace_scratch_buffer;
            pass.probe_ray_data = resources.probe_ray_data;
            pass.dispatch_width = dispatch_width;
            pass.dispatch_height = dispatch_height;
            pass.dispatch_depth = dispatch_depth;
            builder.UseTexture(resources.probe_ray_data, AccessFlags.Write);
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
        private uint dispatch_width;
        private uint dispatch_height;
        private uint dispatch_depth;

        private void Render(UnsafeGraphContext context)
        {
            var cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
            scene_accel_struct.BuildIfNeeded(cmd);
            scene_accel_struct.Bind(cmd, AccelStructName, shader);
            shader.SetTextureParam(cmd, probe_ray_data_ID, probe_ray_data);
            shader.Dispatch(cmd, trace_scratch, dispatch_width, dispatch_height, dispatch_depth);
        }
    }
}
