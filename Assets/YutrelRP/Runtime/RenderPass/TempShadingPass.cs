using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace YutrelRP
{
    internal class TempShadingPass : YutrelRenderPass
    {
        private readonly Shader m_temp_shading_shader = Shader.Find("YutrelRP/TempShading");
        private Material m_temp_shading_material;
        private Mesh m_full_screen_mesh;

        private class PassData
        {
            internal TextureHandle m_GBuffer_A;
            internal TextureHandle m_GBuffer_B;
            internal TextureHandle m_GBuffer_C;
            internal TextureHandle m_scene_depth;
        }

        internal void Render(RenderGraph graph, TextureHandle GBuffer_A, TextureHandle GBuffer_B,
            TextureHandle GBuffer_C, TextureHandle scene_color, TextureHandle scene_depth)
        {
            if (m_temp_shading_material == null)
            {
                m_temp_shading_material = CoreUtils.CreateEngineMaterial(m_temp_shading_shader);
            }

            if (m_full_screen_mesh == null)
            {
                m_full_screen_mesh = YutrelRPUtils.CreateFullscreenMesh();
            }

            using var builder = graph.AddRasterRenderPass<PassData>("Temp Shading Pass", out var pass_data,
                new ProfilingSampler("Temp Shading Pass"));

            pass_data.m_GBuffer_A = GBuffer_A;
            pass_data.m_GBuffer_B = GBuffer_B;
            pass_data.m_GBuffer_C = GBuffer_C;
            pass_data.m_scene_depth = scene_depth;
            builder.UseTexture(pass_data.m_GBuffer_A, AccessFlags.Read);
            builder.UseTexture(pass_data.m_GBuffer_B, AccessFlags.Read);
            builder.UseTexture(pass_data.m_GBuffer_C, AccessFlags.Read);
            builder.UseTexture(pass_data.m_scene_depth, AccessFlags.Read);

            builder.SetRenderAttachment(scene_color, 0, AccessFlags.Write);

            builder.SetRenderFunc((PassData data, RasterGraphContext context) =>
            {
                m_temp_shading_material.SetTexture("_GBuffer_A", data.m_GBuffer_A);
                m_temp_shading_material.SetTexture("_GBuffer_B", data.m_GBuffer_B);
                m_temp_shading_material.SetTexture("_GBuffer_C", data.m_GBuffer_C);
                m_temp_shading_material.SetTexture("_SceneDepth", data.m_scene_depth);

                context.cmd.DrawMesh(m_full_screen_mesh, Matrix4x4.identity, m_temp_shading_material, 0, 0);
            });
        }
    }
}