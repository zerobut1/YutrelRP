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
        private static readonly int environment_intensity_ID = Shader.PropertyToID("_EndfieldEnvironmentIntensity");
        private static readonly int environment_specular_multiplier_ID = Shader.PropertyToID("_EndfieldEnvironmentSpecularMultiplier");
        private static readonly int use_screen_space_ao_ID = Shader.PropertyToID("_EndfieldUseScreenSpaceAO");

        private readonly Color ambient_color;
        private readonly float ambient_intensity, input_scale, scene_pre_exposure;
        private readonly float environment_intensity, environment_specular_multiplier;

        internal EndfieldShaderGlobals(ResolvedEndfieldSettings settings, LightResources lights, float pre_exposure)
        {
            ambient_color = settings.ambient_color;
            ambient_intensity = settings.ambient_intensity;
            input_scale = settings.GetYutrelInputScale();
            scene_pre_exposure = settings.endfield_scene_pre_exposure;
            environment_intensity = lights.has_environment_reflection
                ? 20000.0f * settings.lighting_reference_scale *
                  (pre_exposure / ResolvedEndfieldSettings.ReferencePreExposure)
                : 0.0f;
            environment_specular_multiplier = lights.has_environment_reflection
                ? lights.environment_specular_multiplier : 0.0f;
        }

        internal void Bind(IBaseCommandBuffer cmd, bool use_screen_space_ao)
        {
            cmd.SetGlobalVector(ambient_color_ID, ambient_color);
            cmd.SetGlobalFloat(ambient_intensity_ID, ambient_intensity);
            cmd.SetGlobalFloat(input_scale_ID, input_scale);
            cmd.SetGlobalFloat(scene_pre_exposure_ID, scene_pre_exposure);
            cmd.SetGlobalFloat(environment_intensity_ID, environment_intensity);
            cmd.SetGlobalFloat(environment_specular_multiplier_ID, environment_specular_multiplier);
            cmd.SetGlobalFloat(use_screen_space_ao_ID, use_screen_space_ao ? 1.0f : 0.0f);
        }
    }
}
