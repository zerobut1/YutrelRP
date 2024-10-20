using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using YutrelRP.FrameData;

namespace YutrelRP
{
    public partial class YutrelRenderGraphRecorder : IRenderGraphRecorder, IDisposable
    {
        private TextureHandle m_backbuffer_color_handle = TextureHandle.nullHandle;
        private RTHandle m_rt_color_handle = null;

        private TextureHandle m_backbuffer_depth_handle = TextureHandle.nullHandle;
        private RTHandle m_rt_depth_handle = null;

        public void RecordRenderGraph(RenderGraph render_graph, ContextContainer frame_data)
        {
            var camera_data = frame_data.Get<CameraData>();
            CreateRenderGraphCameraRenderTargets(render_graph, camera_data);
            AddSetupCameraPass(render_graph, camera_data);
            var clear_flags = camera_data.camera.clearFlags;

            if (!render_graph.nativeRenderPassesEnabled && clear_flags != CameraClearFlags.Nothing)
            {
                // AddClearRenderTargetPass(render_graph, camera_data);
            }

            // AddDrawObjectPass(render_graph, camera_data);

            if (clear_flags == CameraClearFlags.Skybox && RenderSettings.skybox != null)
            {
                // AddDrawSkyboxPass(render_graph, camera_data);
            }

#if UNITY_EDITOR
            // AddDrawEditorGizmoPass(render_graph, camera_data, GizmoSubset.PreImageEffects);
            // AddDrawEditorGizmoPass(render_graph, camera_data, GizmoSubset.PostImageEffects);
#endif

            var base_pass_data = AddBasePass(render_graph, camera_data);
            AddTempShadingPass(render_graph, base_pass_data);
        }

        public void Dispose()
        {
            RTHandles.Release(m_rt_color_handle);
            RTHandles.Release(m_rt_depth_handle);
            GC.SuppressFinalize(this);
        }

        private void CreateRenderGraphCameraRenderTargets(RenderGraph render_graph, CameraData camera_data)
        {
            var camera_target_texture = camera_data.camera.targetTexture;
            var target_texture = camera_data.camera.targetTexture;
            var is_builtin_texture = (camera_target_texture == null);
            var is_camera_target_offscreen_depth = !is_builtin_texture &&
                                                   camera_data.camera.targetTexture.format == RenderTextureFormat.Depth;

            var rt_color_id = is_builtin_texture
                ? BuiltinRenderTextureType.CameraTarget
                : new RenderTargetIdentifier(camera_target_texture);
            if (m_rt_color_handle == null)
            {
                m_rt_color_handle = RTHandles.Alloc((RenderTargetIdentifier)rt_color_id, "BackBuffer Color");
            }
            else if (m_rt_color_handle.nameID != rt_color_id)
            {
                RTHandleStaticHelpers.SetRTHandleUserManagedWrapper(ref m_rt_color_handle, rt_color_id);
            }

            var rt_depth_id = is_builtin_texture
                ? BuiltinRenderTextureType.Depth
                : new RenderTargetIdentifier(camera_target_texture);
            if (m_rt_depth_handle == null)
            {
                m_rt_depth_handle = RTHandles.Alloc((RenderTargetIdentifier)rt_depth_id, "BackBuffer Depth");
            }
            else if (m_rt_depth_handle.nameID != rt_depth_id)
            {
                RTHandleStaticHelpers.SetRTHandleUserManagedWrapper(ref m_rt_depth_handle, rt_depth_id);
            }

            var clear_color = camera_data.GetClearColor();

            var clear_backbuffer_on_first_use = !render_graph.nativeRenderPassesEnabled;
            var discard_color_backbuffer_on_last_use = !render_graph.nativeRenderPassesEnabled;
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

            m_backbuffer_color_handle =
                render_graph.ImportTexture(m_rt_color_handle, color_import_info, import_backbuffer_color_params);
            m_backbuffer_depth_handle =
                render_graph.ImportTexture(m_rt_depth_handle, depth_import_info, import_backbuffer_depth_params);
        }
    }
}