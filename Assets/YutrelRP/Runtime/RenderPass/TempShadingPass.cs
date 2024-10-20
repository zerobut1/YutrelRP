using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace YutrelRP
{
    public partial class YutrelRenderGraphRecorder
    {
        // private Shader m_temp_shading_shader = Shader.Find("YutrelRP/TempShading");
        private Material m_temp_shading_material;

        internal class ShadingPassData
        {
            internal TextureHandle m_GBuffer_A;
            internal TextureHandle m_GBuffer_B;
            internal TextureHandle m_GBuffer_C;

            internal TextureHandle m_camera_target;
        }

        private void AddTempShadingPass(RenderGraph graph, BasePassData base_pass_data)
        {
            if (m_temp_shading_material == null)
            {
                m_temp_shading_material = CoreUtils.CreateEngineMaterial(Shader.Find("YutrelRP/TempShading"));
            }

            using (var builder = graph.AddRasterRenderPass<ShadingPassData>("Temp Shading Pass", out var pass_data,
                       new ProfilingSampler("Temp Shading Pass")))
            {
                // pass_data.m_GBuffer_A = builder.ReadTexture(base_pass_data.m_GBuffer_A);
                // pass_data.m_GBuffer_B = builder.ReadTexture(base_pass_data.m_GBuffer_B);
                // pass_data.m_GBuffer_C = builder.ReadTexture(base_pass_data.m_GBuffer_C);
                pass_data.m_GBuffer_A = base_pass_data.m_GBuffer_A;
                pass_data.m_GBuffer_B = base_pass_data.m_GBuffer_B;
                pass_data.m_GBuffer_C = base_pass_data.m_GBuffer_C;
                builder.UseTexture(pass_data.m_GBuffer_A, AccessFlags.Read);
                builder.UseTexture(pass_data.m_GBuffer_B, AccessFlags.Read);
                builder.UseTexture(pass_data.m_GBuffer_C, AccessFlags.Read);

                // builder.SetInputAttachment(pass_data.m_GBuffer_A, 0, AccessFlags.Read);
                // builder.SetInputAttachment(pass_data.m_GBuffer_B, 1, AccessFlags.Read);
                // builder.SetInputAttachment(pass_data.m_GBuffer_C, 2, AccessFlags.Read);

                pass_data.m_camera_target = graph.ImportBackbuffer(BuiltinRenderTextureType.CameraTarget);
                // pass_data.m_camera_target = m_backbuffer_color_handle;
                if (m_backbuffer_color_handle.IsValid())
                {
                    builder.SetRenderAttachment(m_backbuffer_color_handle, 0, AccessFlags.Write);
                }

                // builder.SetRenderAttachment(pass_data.m_camera_target, 0, AccessFlags.Write);

                builder.AllowPassCulling(false);


                builder.SetRenderFunc((ShadingPassData data, RasterGraphContext context) =>
                {
                    m_temp_shading_material.SetTexture("_GBuffer_A", data.m_GBuffer_A);
                    m_temp_shading_material.SetTexture("_GBuffer_B", data.m_GBuffer_B);
                    m_temp_shading_material.SetTexture("_GBuffer_C", data.m_GBuffer_C);

                    Blitter.BlitTexture(context.cmd, graph.defaultResources.whiteTexture, Vector4.one,
                        m_temp_shading_material, 0);
                });
            }
        }
    }
}