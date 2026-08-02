#if UNITY_EDITOR
using System.Diagnostics;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace YutrelRP
{
    internal class GizmosPass
    {
        private static readonly ProfilingSampler pre_image_effects_sampler =
            new("Pre Image Effects Gizmos Pass");

        private static readonly ProfilingSampler post_image_effects_sampler =
            new("Post Image Effects Gizmos Pass");

        [Conditional("UNITY_EDITOR")]
        internal static void Record(RenderGraph render_graph, Camera camera, TextureHandle color, TextureHandle depth,
            GizmoSubset gizmo_subset)
        {
            if (!color.IsValid() || !depth.IsValid()) return;
            if (!ShouldRender(camera)) return;

            var sampler = GetSampler(gizmo_subset);
            using var builder = render_graph.AddUnsafePass<GizmosPass>(sampler.name, out var pass, sampler);

            pass.color = color;
            pass.depth = depth;
            pass.renderer_list = render_graph.CreateGizmoRendererList(camera, gizmo_subset);

            builder.UseTexture(pass.color, AccessFlags.Write);
            builder.UseTexture(pass.depth, AccessFlags.ReadWrite);
            builder.UseRendererList(pass.renderer_list);
            builder.AllowPassCulling(false);

            builder.SetRenderFunc<GizmosPass>(static (pass, context) => pass.Render(context));
        }

        private static bool ShouldRender(Camera camera)
        {
            return Handles.ShouldRenderGizmos() &&
                   camera.sceneViewFilterMode != Camera.SceneViewFilterMode.ShowFiltered;
        }

        private static ProfilingSampler GetSampler(GizmoSubset gizmo_subset)
        {
            return gizmo_subset == GizmoSubset.PreImageEffects
                ? pre_image_effects_sampler
                : post_image_effects_sampler;
        }

        private TextureHandle color;
        private TextureHandle depth;
        private RendererListHandle renderer_list;

        private void Render(UnsafeGraphContext context)
        {
            context.cmd.SetRenderTarget(color, depth);
            context.cmd.DrawRendererList(renderer_list);
        }
    }
}
#endif
