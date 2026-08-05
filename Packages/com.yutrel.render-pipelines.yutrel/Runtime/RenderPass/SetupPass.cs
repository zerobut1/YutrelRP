using System;
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
            inverseViewAndProjectionMatrix = Shader.PropertyToID("unity_MatrixInvVP"),
            pre_exposure_ID = Shader.PropertyToID("_PreExposure"),
            one_over_pre_exposure_ID = Shader.PropertyToID("_OneOverPreExposure");

        internal static void Record(RenderGraph render_graph, Camera camera,
            Vector2Int attachment_size, ResolvedPostProcessSettings post_process_settings)
        {
            OpenPBRLUTs.EnsureCreated();

            var exposure = post_process_settings.exposure;
            var pre_exposure = exposure.pre_exposure;

            using var builder = render_graph.AddComputePass<SetupPass>(sampler.name, out var pass, sampler);
            pass.rt_size = attachment_size;
            pass.camera = camera;
            pass.pre_exposure = pre_exposure;
            pass.one_over_pre_exposure = exposure.one_over_pre_exposure;

            builder.AllowPassCulling(false);
            builder.AllowGlobalStateModification(true);

            builder.SetRenderFunc<SetupPass>(static (pass, context) => { pass.Render(context); });
        }

        internal static void CreateDeferredTargets(RenderGraph render_graph, Camera camera,
            ref RenderTargets textures, Vector2Int attachment_size, GraphicsFormat scene_color_format,
            float pre_exposure)
        {
            // scene color
            var scene_color_desc = new TextureDesc(attachment_size.x, attachment_size.y)
            {
                colorFormat = scene_color_format,
                clearBuffer = camera.clearFlags <= CameraClearFlags.Color,
                clearColor = camera.clearFlags == CameraClearFlags.Color
                    ? PreExposeColor(camera.backgroundColor.linear, pre_exposure)
                    : Color.clear,
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
            var standard_gbuffer_format = GraphicsFormatUtility.GetGraphicsFormat(RenderTextureFormat.ARGB32, false);
            var gbuffer_desc = new TextureDesc(attachment_size.x, attachment_size.y)
            {
                colorFormat = standard_gbuffer_format,
                depthBufferBits = 0,
                msaaSamples = MSAASamples.None,
                enableRandomWrite = false,
                clearBuffer = true,
                clearColor = new Color(0, 0, 0, 0),
                name = "GBuffer A"
            };
            textures.GBuffer_A = render_graph.CreateTexture(gbuffer_desc);
            gbuffer_desc.name = "GBuffer B";
            gbuffer_desc.colorFormat = standard_gbuffer_format;
            textures.GBuffer_B = render_graph.CreateTexture(gbuffer_desc);
            gbuffer_desc.name = "GBuffer C";
            gbuffer_desc.colorFormat = standard_gbuffer_format;
            textures.GBuffer_C = render_graph.CreateTexture(gbuffer_desc);
            gbuffer_desc.name = "GBuffer D";
            textures.GBuffer_D = render_graph.CreateTexture(gbuffer_desc);

        }

        private static Color PreExposeColor(Color color, float pre_exposure)
        {
            color.r *= pre_exposure;
            color.g *= pre_exposure;
            color.b *= pre_exposure;
            return color;
        }

        // data
        private Camera camera;
        private Vector2Int rt_size;
        private float pre_exposure;
        private float one_over_pre_exposure;

        private void Render(ComputeGraphContext context)
        {
            var cmd = context.cmd;

            cmd.SetupCameraProperties(camera);
            cmd.SetGlobalFloat(pre_exposure_ID, pre_exposure);
            cmd.SetGlobalFloat(one_over_pre_exposure_ID, one_over_pre_exposure);
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

    }
}
