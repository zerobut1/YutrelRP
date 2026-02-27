using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace YutrelRP
{
    internal class SetupPass
    {
        private static readonly ProfilingSampler sampler = new("Setup Pass");

        private static readonly int
            rt_size_ID = Shader.PropertyToID("_CameraBufferSize"),
            inverseViewAndProjectionMatrix = Shader.PropertyToID("unity_MatrixInvVP");

        internal static void Record(RenderGraph render_graph, Camera camera, ref RenderTargets textures,
            Vector2Int attachment_size)
        {
            using var builder = render_graph.AddComputePass<SetupPass>(sampler.name, out var pass, sampler);
            pass.rt_size = attachment_size;
            pass.camera = camera;

            // camera output
            textures.camera_output = CreateRenderGraphCameraRenderTarget(render_graph, camera);

            // scene color
            var scene_color_desc = new TextureDesc(attachment_size.x, attachment_size.y)
            {
                colorFormat = GraphicsFormatUtility.GetGraphicsFormat(RenderTextureFormat.DefaultHDR, false),
                clearBuffer = camera.clearFlags <= CameraClearFlags.Color,
                clearColor = camera.clearFlags == CameraClearFlags.Color ? camera.backgroundColor.linear : Color.clear,
                name = "Scene Color"
            };
            textures.scene_color = render_graph.CreateTexture(scene_color_desc);

            // scene depth
            var scene_depth_desc = new TextureDesc(attachment_size.x, attachment_size.y)
            {
                colorFormat = SystemInfo.GetGraphicsFormat(DefaultFormat.LDR),
                depthBufferBits = DepthBits.Depth32,
                clearBuffer = camera.clearFlags <= CameraClearFlags.Depth,
                name = "Scene Depth"
            };
            textures.scene_depth = render_graph.CreateTexture(scene_depth_desc);

            // GBuffer
            var gbuffer_desc = new TextureDesc(attachment_size.x, attachment_size.y)
            {
                colorFormat = GraphicsFormatUtility.GetGraphicsFormat(RenderTextureFormat.ARGB32, false),
                depthBufferBits = 0,
                msaaSamples = MSAASamples.None,
                enableRandomWrite = false,
                clearBuffer = true,
                clearColor = new Color(0, 0, 0, 0),
                name = "GBuffer A"
            };
            textures.GBuffer_A = render_graph.CreateTexture(gbuffer_desc);
            gbuffer_desc.name = "GBuffer B";
            textures.GBuffer_B = render_graph.CreateTexture(gbuffer_desc);
            gbuffer_desc.name = "GBuffer C";
            textures.GBuffer_C = render_graph.CreateTexture(gbuffer_desc);

            // final color
            var final_color_desc = new TextureDesc(attachment_size.x, attachment_size.y)
            {
                colorFormat = GraphicsFormatUtility.GetGraphicsFormat(RenderTextureFormat.Default, true),
                clearBuffer = camera.clearFlags <= CameraClearFlags.Color,
                clearColor = camera.clearFlags == CameraClearFlags.Color ? camera.backgroundColor.linear : Color.clear,
                name = "Final Color"
            };
            textures.final_color = render_graph.CreateTexture(final_color_desc);

            builder.AllowPassCulling(false);
            builder.AllowGlobalStateModification(true);

            builder.SetRenderFunc<SetupPass>(static (pass, context) => { pass.Render(context); });
        }

        // data
        private Camera camera;
        private Vector2Int rt_size;

        private void Render(ComputeGraphContext context)
        {
            var cmd = context.cmd;

            cmd.SetupCameraProperties(camera);
            cmd.SetGlobalVector(rt_size_ID,
                new Vector4(1.0f / rt_size.x,
                    1.0f / rt_size.y,
                    rt_size.x,
                    rt_size.y));

            Matrix4x4 view_matrix = camera.worldToCameraMatrix;
            Matrix4x4 projection_matrix = camera.projectionMatrix;
            projection_matrix = GL.GetGPUProjectionMatrix(projection_matrix, true);
            Matrix4x4 inverse_VP = (projection_matrix * view_matrix).inverse;
            cmd.SetGlobalMatrix(inverseViewAndProjectionMatrix, inverse_VP);
        }

        private static TextureHandle CreateRenderGraphCameraRenderTarget(RenderGraph render_graph, Camera camera)
        {
            var camera_target_texture = camera.targetTexture;
            var is_builtin_texture = camera_target_texture == null;

            var rt_color_id = is_builtin_texture
                ? BuiltinRenderTextureType.CameraTarget
                : new RenderTargetIdentifier(camera_target_texture);

            var clear_backbuffer_on_first_use = !render_graph.nativeRenderPassesEnabled;
            var discard_color_backbuffer_on_last_use = !render_graph.nativeRenderPassesEnabled;

            var import_backbuffer_color_params = new ImportResourceParams
            {
                clearOnFirstUse = clear_backbuffer_on_first_use,
                clearColor = camera.clearFlags == CameraClearFlags.Color ? camera.backgroundColor.linear : Color.clear,
                discardOnLastUse = discard_color_backbuffer_on_last_use
            };

            var is_rt_color_sRGB = QualitySettings.activeColorSpace == ColorSpace.Linear;

            var color_import_info = new RenderTargetInfo();

            if (is_builtin_texture)
            {
                color_import_info.width = Screen.width;
                color_import_info.height = Screen.height;
                color_import_info.volumeDepth = 1;
                color_import_info.msaaSamples = 1;
                color_import_info.format =
                    GraphicsFormatUtility.GetGraphicsFormat(RenderTextureFormat.Default, is_rt_color_sRGB);
                color_import_info.bindMS = false;
            }
            else
            {
                color_import_info.width = camera_target_texture.width;
                color_import_info.height = camera_target_texture.height;
                color_import_info.volumeDepth = camera_target_texture.volumeDepth;
                color_import_info.msaaSamples = camera_target_texture.antiAliasing;
                color_import_info.format =
                    GraphicsFormatUtility.GetGraphicsFormat(RenderTextureFormat.Default, is_rt_color_sRGB);
            }

            return render_graph.ImportBackbuffer(rt_color_id, color_import_info, import_backbuffer_color_params);
        }
    }
}
