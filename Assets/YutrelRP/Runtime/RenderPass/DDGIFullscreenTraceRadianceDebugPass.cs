#if UNITY_EDITOR
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace YutrelRP
{
    internal sealed class DDGIFullscreenTraceRadianceDebugPass
    {
        private const string ShaderPassName = "DDGIProbeTrace";
        private const string RayGenName = "RayGenDDGIFullscreenTraceRadiance";

        private static readonly ProfilingSampler sampler = new("DDGI Fullscreen Trace Radiance Debug");
        private static readonly int acceleration_structure_ID = Shader.PropertyToID("_DDGIAccelerationStructure");
        private static readonly int output_ID = Shader.PropertyToID("_DDGIFullscreenTraceRadiance");
        private static readonly int directional_light_count_ID = Shader.PropertyToID("_DirectionalLightCount");
        private static readonly int lighting_intensity_scale_ID = Shader.PropertyToID("_DDGILightingIntensityScale");
        private static readonly int environment_cube_ID = Shader.PropertyToID("_DDGIEnvironmentCube");
        private static readonly int environment_cube_hdr_ID = Shader.PropertyToID("_DDGIEnvironmentCube_HDR");
        private static readonly int environment_enabled_ID = Shader.PropertyToID("_DDGIEnvironmentEnabled");
        private static readonly int debug_inv_view_proj_ID = Shader.PropertyToID("_DDGIDebugInvViewProj");
        private static readonly int debug_camera_position_WS_ID =
            Shader.PropertyToID("_DDGIDebugCameraPositionWS");
        private static readonly int debug_camera_far_clip_ID = Shader.PropertyToID("_DDGIDebugCameraFarClip");
        private static readonly int debug_projection_params_x_ID =
            Shader.PropertyToID("_DDGIDebugProjectionParamsX");
        private static readonly int debug_mode_ID = Shader.PropertyToID("_DDGIFullscreenTraceDebugMode");

        private static RayTracingShader cached_shader;

        internal static bool IsFullscreenTraceMode(YutrelRPDebugSettings.DDGIProbeDebugMode mode)
        {
            return mode == YutrelRPDebugSettings.DDGIProbeDebugMode.FullscreenTraceRadiance ||
                   mode == YutrelRPDebugSettings.DDGIProbeDebugMode.FullscreenTraceNormal;
        }

        internal static void Record(RenderGraph render_graph, Camera camera, RenderTargets textures,
            DDGIResources resources, LightResources light_resources, YutrelRayTracingWorld ray_tracing_world,
            ResolvedDDGISettings ddgi_settings, YutrelRPDebugSettings debug_settings, Vector2Int attachment_size)
        {
            var debug_mode = debug_settings != null
                ? debug_settings.ddgi_probe_debug_mode
                : YutrelRPDebugSettings.DDGIProbeDebugMode.Disabled;
            if (!IsFullscreenTraceMode(debug_mode))
            {
                return;
            }

            if (camera.cameraType != CameraType.SceneView && camera.cameraType != CameraType.Game)
            {
                return;
            }

            if (resources == null || light_resources == null || ray_tracing_world == null ||
                !SystemInfo.supportsRayTracing)
            {
                return;
            }

            var volume = resources.active_volume;
            if (volume == null || attachment_size.x <= 0 || attachment_size.y <= 0)
            {
                return;
            }

            if (!ray_tracing_world.EnsureInitialized())
            {
                return;
            }

            if (!GraphicsSettings.TryGetRenderPipelineSettings<YutrelDDGIShaderResources>(out var shader_resources) ||
                shader_resources == null)
            {
                YutrelRPRuntimeShaderUtility.WarnMissingResourceOnce(nameof(YutrelDDGIShaderResources));
                return;
            }

            cached_shader ??= shader_resources.fullscreen_trace_radiance;
            if (cached_shader == null)
            {
                YutrelRPRuntimeShaderUtility.WarnMissingResourceOnce(
                    nameof(YutrelDDGIShaderResources.fullscreen_trace_radiance));
                return;
            }

            ray_tracing_world.SyncSceneIfNeeded(0xFFu);

            var output_desc = new TextureDesc(attachment_size.x, attachment_size.y)
            {
                colorFormat = GraphicsFormat.R16G16B16A16_SFloat,
                enableRandomWrite = true,
                clearBuffer = true,
                clearColor = Color.black,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                name = "DDGI Fullscreen Trace Debug"
            };
            var output = render_graph.CreateTexture(output_desc);

            using var builder =
                render_graph.AddComputePass<DDGIFullscreenTraceRadianceDebugPass>(
                    sampler.name, out var pass, sampler);

            var encoding_settings = ddgi_settings.encoding;
            var projection_matrix = GL.GetGPUProjectionMatrix(camera.projectionMatrix, true);

            pass.shader = cached_shader;
            pass.debug_mode = (int)debug_mode;
            pass.scene_accel_struct = ray_tracing_world.SceneAccelStruct;
            pass.output = output;
            pass.directional_light_data_buffer = light_resources.directional_light_data_buffer;
            pass.directional_light_count = light_resources.directional_light_count;
            pass.environment_enabled = light_resources.has_environment_reflection &&
                                       light_resources.environment_reflection_cube.IsValid()
                ? 1
                : 0;
            pass.environment_cube = light_resources.environment_reflection_cube;
            pass.environment_cube_hdr = light_resources.environment_reflection_cube_hdr;
            pass.environment_intensity = light_resources.environment_intensity;
            pass.environment_diffuse_multiplier = light_resources.environment_diffuse_multiplier;
            pass.lighting_intensity_scale = Mathf.Max(0.001f, encoding_settings.lightingIntensityScale);
            pass.inv_view_proj = (projection_matrix * camera.worldToCameraMatrix).inverse;
            pass.camera_position_WS = camera.transform.position;
            pass.camera_far_clip = Mathf.Max(camera.nearClipPlane, camera.farClipPlane);
            pass.projection_params_x = projection_matrix.m11 < 0.0f ? -1.0f : 1.0f;
            pass.dispatch_width = (uint)attachment_size.x;
            pass.dispatch_height = (uint)attachment_size.y;

            builder.UseTexture(pass.output, AccessFlags.Write);
            builder.UseBuffer(pass.directional_light_data_buffer, AccessFlags.Read);
            if (pass.environment_enabled != 0)
            {
                builder.UseTexture(pass.environment_cube, AccessFlags.Read);
            }

            builder.AllowPassCulling(false);
            builder.SetRenderFunc<DDGIFullscreenTraceRadianceDebugPass>(
                static (pass, context) => pass.Render(context));

            textures.final_color = output;
        }

        internal static void Cleanup()
        {
            cached_shader = null;
        }

        private RayTracingShader shader;
        private int debug_mode;
        private YutrelRayTracingAccelStruct scene_accel_struct;
        private TextureHandle output;
        private BufferHandle directional_light_data_buffer;
        private int directional_light_count;
        private TextureHandle environment_cube;
        private Vector4 environment_cube_hdr;
        private float environment_intensity;
        private float environment_diffuse_multiplier;
        private int environment_enabled;
        private float lighting_intensity_scale;
        private Matrix4x4 inv_view_proj;
        private Vector3 camera_position_WS;
        private float camera_far_clip;
        private float projection_params_x;
        private uint dispatch_width;
        private uint dispatch_height;

        private void Render(ComputeGraphContext context)
        {
            var cmd = context.cmd;
            scene_accel_struct.BuildIfNeeded(cmd);
            cmd.SetRayTracingShaderPass(shader, ShaderPassName);
            cmd.SetRayTracingIntParam(shader, debug_mode_ID, debug_mode);
            cmd.SetRayTracingAccelerationStructure(shader, acceleration_structure_ID,
                scene_accel_struct.AccelerationStructure);
            cmd.SetRayTracingTextureParam(shader, output_ID, output);
            cmd.SetRayTracingBufferParam(shader, LightResources.directional_light_data_ID,
                directional_light_data_buffer);
            cmd.SetRayTracingIntParam(shader, directional_light_count_ID, directional_light_count);
            cmd.SetRayTracingIntParam(shader, environment_enabled_ID, environment_enabled);
            cmd.SetRayTracingVectorParam(shader, environment_cube_hdr_ID, environment_cube_hdr);
            cmd.SetRayTracingFloatParam(shader, LightResources.environment_intensity_ID, environment_intensity);
            cmd.SetRayTracingFloatParam(shader, LightResources.environment_diffuse_multiplier_ID,
                environment_diffuse_multiplier);
            if (environment_enabled != 0)
            {
                cmd.SetRayTracingTextureParam(shader, environment_cube_ID, environment_cube);
            }

            cmd.SetRayTracingFloatParam(shader, lighting_intensity_scale_ID, lighting_intensity_scale);
            cmd.SetRayTracingMatrixParam(shader, debug_inv_view_proj_ID, inv_view_proj);
            cmd.SetRayTracingVectorParam(shader, debug_camera_position_WS_ID,
                new Vector4(camera_position_WS.x, camera_position_WS.y, camera_position_WS.z, 1.0f));
            cmd.SetRayTracingFloatParam(shader, debug_camera_far_clip_ID, camera_far_clip);
            cmd.SetRayTracingFloatParam(shader, debug_projection_params_x_ID, projection_params_x);
            cmd.DispatchRays(shader, RayGenName, dispatch_width, dispatch_height, 1, null);
        }
    }
}
#endif
