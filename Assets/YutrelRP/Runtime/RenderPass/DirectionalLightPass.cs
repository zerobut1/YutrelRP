using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace YutrelRP
{
    internal class DirectionalLightPass
    {
        private static readonly ProfilingSampler sampler = new ProfilingSampler("Directional Light Pass");

        private static readonly Shader m_temp_shading_shader = Shader.Find("YutrelRP/DirectionalLightPass");
        private static Material m_temp_shading_material;
        private static Mesh m_full_screen_mesh;

        private TextureHandle GBuffer_A;
        private TextureHandle GBuffer_B;
        private TextureHandle GBuffer_C;
        private TextureHandle scene_depth;

        private void Render(RasterGraphContext context)
        {
            var cmd = context.cmd;
            m_temp_shading_material.SetTexture("_GBuffer_A", GBuffer_A);
            m_temp_shading_material.SetTexture("_GBuffer_B", GBuffer_B);
            m_temp_shading_material.SetTexture("_GBuffer_C", GBuffer_C);
            m_temp_shading_material.SetTexture("_SceneDepth", scene_depth);

            context.cmd.DrawMesh(m_full_screen_mesh, Matrix4x4.identity, m_temp_shading_material, 0, 0);
        }

        public static void Record(RenderGraph graph, RenderTargets textures)
        {
            if (m_temp_shading_material == null)
                m_temp_shading_material = CoreUtils.CreateEngineMaterial(m_temp_shading_shader);

            if (m_full_screen_mesh == null)
                m_full_screen_mesh = CreateFullscreenMesh();

            using var builder =
                graph.AddRasterRenderPass<DirectionalLightPass>("Temp Shading Pass", out var pass, sampler);

            pass.GBuffer_A = textures.GBuffer_A;
            pass.GBuffer_B = textures.GBuffer_B;
            pass.GBuffer_C = textures.GBuffer_C;
            pass.scene_depth = textures.scene_depth;
            builder.UseTexture(pass.GBuffer_A);
            builder.UseTexture(pass.GBuffer_B);
            builder.UseTexture(pass.GBuffer_C);
            builder.UseTexture(pass.scene_depth);

            builder.SetRenderAttachment(textures.scene_color, 0, AccessFlags.Write);

            builder.SetRenderFunc<DirectionalLightPass>(static (pass, context) => pass.Render(context));
        }

        static Mesh CreateFullscreenMesh()
        {
            // Simple full-screen triangle.
            Vector3[] positions =
            {
                new Vector3(-1.0f, 1.0f, 0.0f),
                new Vector3(-1.0f, -3.0f, 0.0f),
                new Vector3(3.0f, 1.0f, 0.0f)
            };

            int[] indices = { 0, 1, 2 };

            Mesh mesh = new Mesh();
            mesh.indexFormat = IndexFormat.UInt16;
            mesh.vertices = positions;
            mesh.triangles = indices;

            return mesh;
        }
    }
}