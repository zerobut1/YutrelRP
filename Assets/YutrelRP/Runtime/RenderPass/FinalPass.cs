using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace YutrelRP
{
    internal class FinalPass
    {
        private static readonly ProfilingSampler sampler = new("Final Pass");
        private static readonly int
            source_color_ID = Shader.PropertyToID("_SourceColor"),
            source_scale_bias_ID = Shader.PropertyToID("_SourceScaleBias");
        private static readonly Vector4 identity_source_scale_bias = new(1.0f, 1.0f, 0.0f, 0.0f);
        private static readonly Vector4 flip_y_source_scale_bias = new(1.0f, -1.0f, 0.0f, 1.0f);
        private static Material material;

        internal static void Record(RenderGraph render_graph, Camera camera, RenderTargets textures)
        {
            using var builder = render_graph.AddRasterRenderPass<FinalPass>(sampler.name, out var pass, sampler);
            if (material == null) material = CoreUtils.CreateEngineMaterial(Shader.Find("YutrelRP/ToneMapping"));

            pass.source_color = textures.final_color;
            pass.source_scale_bias = GetFinalBlitScaleBias(camera);

            builder.UseTexture(pass.source_color);
            builder.SetRenderAttachment(textures.camera_output, 0);

            builder.SetRenderFunc<FinalPass>(static (pass, context) => { pass.Render(context); });
        }

        // data
        private TextureHandle source_color;
        private Vector4 source_scale_bias;

        private void Render(RasterGraphContext context)
        {
            var cmd = context.cmd;

            material.SetTexture(source_color_ID, source_color);
            material.SetVector(source_scale_bias_ID, source_scale_bias);

            CoreUtils.DrawFullScreen(cmd, material);
        }

        private static Vector4 GetFinalBlitScaleBias(Camera camera)
        {
            var outputs_to_game_backbuffer = camera.cameraType == CameraType.Game && camera.targetTexture == null;
            return outputs_to_game_backbuffer && SystemInfo.graphicsUVStartsAtTop
                ? flip_y_source_scale_bias
                : identity_source_scale_bias;
        }

        public static void Cleanup()
        {
            CoreUtils.Destroy(material);
            material = null;
        }
    }
}
