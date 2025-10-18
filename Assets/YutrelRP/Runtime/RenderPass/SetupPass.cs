using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace YutrelRP
{
    internal class SetupPass
    {
        private static readonly ProfilingSampler sampler =
            new("Setup Pass");

        private static readonly int rt_size_ID = Shader.PropertyToID("_CameraBufferSize");
        private Camera camera;

        private Vector2Int rt_size;

        // datas
        private TextureHandle
            scene_color,
            scene_depth,
            GBuffer_A,
            GBuffer_B,
            GBuffer_C;

        private void Render(ComputeGraphContext context)
        {
            var cmd = context.cmd;

            cmd.SetupCameraProperties(camera);
            cmd.SetGlobalVector(rt_size_ID,
                new Vector4(1.0f / rt_size.x,
                    1.0f / rt_size.y,
                    rt_size.x,
                    rt_size.y));
        }

        internal static void Record(RenderGraph render_graph, Camera camera, ref RenderTargets textures,
            Vector2Int attachment_size)
        {
            using var builder = render_graph.AddComputePass<SetupPass>(sampler.name, out var pass, sampler);
            pass.rt_size = attachment_size;
            pass.camera = camera;

            // scene color
            var color_desc = new TextureDesc(attachment_size.x, attachment_size.y)
            {
                colorFormat = GraphicsFormatUtility.GetGraphicsFormat(RenderTextureFormat.ARGBFloat, false),
                clearBuffer = camera.clearFlags <= CameraClearFlags.Color,
                clearColor = camera.clearFlags == CameraClearFlags.Color ? camera.backgroundColor.linear : Color.clear,
                name = "Scene Color"
            };
            pass.scene_color = render_graph.CreateTexture(color_desc);
            builder.UseTexture(pass.scene_color, AccessFlags.WriteAll);

            // scene depth
            var depth_desc = new TextureDesc(attachment_size.x, attachment_size.y)
            {
                colorFormat = SystemInfo.GetGraphicsFormat(DefaultFormat.LDR),
                depthBufferBits = DepthBits.Depth32,
                clearBuffer = camera.clearFlags <= CameraClearFlags.Depth,
                name = "Scene Depth"
            };
            pass.scene_depth = render_graph.CreateTexture(depth_desc);
            builder.UseTexture(pass.scene_depth, AccessFlags.WriteAll);

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
            pass.GBuffer_A = render_graph.CreateTexture(gbuffer_desc);
            gbuffer_desc.name = "GBuffer B";
            pass.GBuffer_B = render_graph.CreateTexture(gbuffer_desc);
            gbuffer_desc.name = "GBuffer C";
            pass.GBuffer_C = render_graph.CreateTexture(gbuffer_desc);

            builder.AllowPassCulling(false);
            builder.AllowGlobalStateModification(true);

            builder.SetRenderFunc<SetupPass>(static (pass, context) => { pass.Render(context); });

            textures.camera_output = CreateRenderGraphCameraRenderTarget(render_graph, camera);
            textures.scene_color = pass.scene_color;
            textures.scene_depth = pass.scene_depth;
            textures.GBuffer_A = pass.GBuffer_A;
            textures.GBuffer_B = pass.GBuffer_B;
            textures.GBuffer_C = pass.GBuffer_C;
        }

        public static TextureHandle CreateRenderGraphCameraRenderTarget(RenderGraph render_graph, Camera camera)
        {
            var camera_target_texture = camera.targetTexture;
            var is_builtin_texture = camera_target_texture == null;

            var rt_color_id = is_builtin_texture
                ? BuiltinRenderTextureType.CameraTarget
                : new RenderTargetIdentifier(camera_target_texture);

            var camera_target = RTHandles.Alloc(rt_color_id, "BackBuffer Color");

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

            return render_graph.ImportTexture(camera_target, color_import_info, import_backbuffer_color_params);
        }
    }
}