using UnityEngine;
using UnityEngine.Rendering;

namespace YutrelRP
{
    // Compatibility parameters shared by every Endfield forward draw.
    internal readonly struct EndfieldShaderGlobals
    {
        private static readonly int ambient_color_ID = Shader.PropertyToID("_EndfieldAmbientColor");
        private static readonly int ambient_intensity_ID = Shader.PropertyToID("_EndfieldAmbientIntensity");
        private static readonly int input_scale_ID = Shader.PropertyToID("_EndfieldYutrelInputScale");
        private static readonly int scene_pre_exposure_ID = Shader.PropertyToID("_EndfieldScenePreExposure");
        private static readonly int use_screen_space_ao_ID = Shader.PropertyToID("_EndfieldUseScreenSpaceAO");

        private readonly Color ambient_color;
        private readonly float ambient_intensity, input_scale, scene_pre_exposure;

        internal EndfieldShaderGlobals(ResolvedEndfieldSettings settings)
        {
            ambient_color = settings.ambient_color;
            ambient_intensity = settings.ambient_intensity;
            input_scale = settings.GetYutrelInputScale();
            scene_pre_exposure = settings.endfield_scene_pre_exposure;
        }

        internal void Bind(IBaseCommandBuffer cmd, bool use_screen_space_ao)
        {
            cmd.SetGlobalVector(ambient_color_ID, ambient_color);
            cmd.SetGlobalFloat(ambient_intensity_ID, ambient_intensity);
            cmd.SetGlobalFloat(input_scale_ID, input_scale);
            cmd.SetGlobalFloat(scene_pre_exposure_ID, scene_pre_exposure);
            cmd.SetGlobalFloat(use_screen_space_ao_ID, use_screen_space_ao ? 1.0f : 0.0f);
        }
    }
}
