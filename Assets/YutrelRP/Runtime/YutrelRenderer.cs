using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using YutrelRP.FrameData;

namespace YutrelRP
{
    public class YutrelRenderer : IDisposable
    {
        private TextureHandle m_backbuffer_color = TextureHandle.nullHandle;
        private RTHandle m_rt_color = null;
        private TextureHandle m_backbuffer_depth = TextureHandle.nullHandle;
        private RTHandle m_rt_depth = null;

        private TextureHandle m_GBuffer_A;
        private TextureHandle m_GBuffer_B;
        private TextureHandle m_GBuffer_C;

        private SetupCameraPass m_setup_camera_pass;
        private SetupLightPass m_setup_light_pass;
        private BasePass m_base_pass;
        private DrawSkyboxPass m_draw_skybox_pass;

        private TempShadingPass m_temp_shading_pass;

        public YutrelRenderer()
        {
            m_setup_camera_pass = new SetupCameraPass();
            m_setup_light_pass = new SetupLightPass();
            m_base_pass = new BasePass();
            m_draw_skybox_pass = new DrawSkyboxPass();

            m_temp_shading_pass = new TempShadingPass();
        }

        public void RecordRenderGraph(RenderGraph graph, ContextContainer frame_data)
        {
            var camera_data = frame_data.Get<CameraData>();
            var light_data = frame_data.Get<LightData>();
            

            CreateRenderGraphCameraRenderTargets(graph, camera_data);

            CreateGBufferRenderTarget(graph, camera_data);

            m_setup_camera_pass.Setup(graph, camera_data);

            m_setup_light_pass.Setup(graph, light_data);

            var clear_flags = camera_data.camera.clearFlags;

            m_base_pass.Render(graph, camera_data, m_GBuffer_A, m_GBuffer_B, m_GBuffer_C, m_backbuffer_depth);

            m_temp_shading_pass.Render(graph, m_GBuffer_A, m_GBuffer_B, m_GBuffer_C, m_backbuffer_color);

            if (clear_flags == CameraClearFlags.Skybox && RenderSettings.skybox != null)
            {
                m_draw_skybox_pass.Render(graph, camera_data, m_backbuffer_color, m_backbuffer_depth);
            }
        }

        public void Dispose()
        {
            RTHandles.Release(m_rt_color);
            RTHandles.Release(m_rt_depth);
            GC.SuppressFinalize(this);
        }

        private void CreateRenderGraphCameraRenderTargets(RenderGraph graph, CameraData camera_data)
        {
            var camera_target_texture = camera_data.camera.targetTexture;
            var target_texture = camera_data.camera.targetTexture;
            var is_builtin_texture = (camera_target_texture == null);
            var is_camera_target_offscreen_depth = !is_builtin_texture &&
                                                   camera_data.camera.targetTexture.format == RenderTextureFormat.Depth;

            var rt_color_id = is_builtin_texture
                ? BuiltinRenderTextureType.CameraTarget
                : new RenderTargetIdentifier(camera_target_texture);
            if (m_rt_color == null)
            {
                m_rt_color = RTHandles.Alloc((RenderTargetIdentifier)rt_color_id, "BackBuffer Color");
            }
            else if (m_rt_color.nameID != rt_color_id)
            {
                RTHandleStaticHelpers.SetRTHandleUserManagedWrapper(ref m_rt_color, rt_color_id);
            }

            var rt_depth_id = is_builtin_texture
                ? BuiltinRenderTextureType.Depth
                : new RenderTargetIdentifier(camera_target_texture);
            if (m_rt_depth == null)
            {
                m_rt_depth = RTHandles.Alloc((RenderTargetIdentifier)rt_depth_id, "BackBuffer Depth");
            }
            else if (m_rt_depth.nameID != rt_depth_id)
            {
                RTHandleStaticHelpers.SetRTHandleUserManagedWrapper(ref m_rt_depth, rt_depth_id);
            }

            var clear_color = camera_data.GetClearColor();

            var clear_backbuffer_on_first_use = !graph.nativeRenderPassesEnabled;
            var discard_color_backbuffer_on_last_use = !graph.nativeRenderPassesEnabled;
            var discard_depth_backbuffer_on_last_use = !is_camera_target_offscreen_depth;

            var import_backbuffer_color_params = new ImportResourceParams
            {
                clearOnFirstUse = clear_backbuffer_on_first_use,
                clearColor = clear_color,
                discardOnLastUse = discard_color_backbuffer_on_last_use,
            };

            var import_backbuffer_depth_params = new ImportResourceParams
            {
                clearOnFirstUse = clear_backbuffer_on_first_use,
                clearColor = clear_color,
                discardOnLastUse = discard_depth_backbuffer_on_last_use,
            };

            var is_rt_color_sRGB = (QualitySettings.activeColorSpace == ColorSpace.Linear);

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

            var depth_import_info = color_import_info;
            depth_import_info.format = SystemInfo.GetGraphicsFormat(DefaultFormat.DepthStencil);

            m_backbuffer_color =
                graph.ImportTexture(m_rt_color, color_import_info, import_backbuffer_color_params);
            m_backbuffer_depth =
                graph.ImportTexture(m_rt_depth, depth_import_info, import_backbuffer_depth_params);
        }

        private void CreateGBufferRenderTarget(RenderGraph graph, CameraData camera_data)
        {
            var camera = camera_data.camera;

            m_GBuffer_A =
                YutrelRPUtils.CreateColorTexture(graph, camera.pixelWidth, camera.pixelHeight, "GBuffer A");
            m_GBuffer_B =
                YutrelRPUtils.CreateColorTexture(graph, camera.pixelWidth, camera.pixelHeight, "GBuffer B");
            m_GBuffer_C =
                YutrelRPUtils.CreateColorTexture(graph, camera.pixelWidth, camera.pixelHeight, "GBuffer C");
        }
    }
}