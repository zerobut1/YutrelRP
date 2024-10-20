using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using YutrelRP.FrameData;

namespace YutrelRP
{
    public partial class YutrelRenderGraphRecorder
    {
        private static readonly ProfilingSampler s_draw_editor_gizmo_profiling_sampler =
            new ProfilingSampler("Draw Editor Gizmo Pass");

        internal class DrawEditorGizmoPassData
        {
            internal RendererListHandle editor_gizmo_list_handle;
        }

        private void AddDrawEditorGizmoPass(RenderGraph render_graph, CameraData camera_data, GizmoSubset gizmo_subset)
        {
            if (!Handles.ShouldRenderGizmos() ||
                camera_data.camera.sceneViewFilterMode == Camera.SceneViewFilterMode.ShowFiltered)
            {
                return;
            }

            var pass_name = (gizmo_subset == GizmoSubset.PreImageEffects)
                ? "Draw Pre Gizmos Pass"
                : "Draw Post Gizmos Pass";
            using var builder = render_graph.AddRasterRenderPass<DrawEditorGizmoPassData>(pass_name,
                out var pass_data, s_draw_editor_gizmo_profiling_sampler);

            if (m_backbuffer_color_handle.IsValid())
            {
                builder.SetRenderAttachment(m_backbuffer_color_handle, 0, AccessFlags.Write);
            }

            if (m_backbuffer_depth_handle.IsValid())
            {
                builder.SetRenderAttachmentDepth(m_backbuffer_depth_handle, AccessFlags.Read);
            }

            pass_data.editor_gizmo_list_handle = render_graph.CreateGizmoRendererList(camera_data.camera, gizmo_subset);

            builder.UseRendererList(pass_data.editor_gizmo_list_handle);

            builder.SetRenderFunc((DrawEditorGizmoPassData data, RasterGraphContext context) =>
            {
                context.cmd.DrawRendererList(data.editor_gizmo_list_handle);
            });
        }
    }
}