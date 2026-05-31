using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace YutrelRP
{
    public readonly struct ResolvedPostProcessSettings
    {
        public readonly PostProcessSettings.ExposureSettings exposure;
        public readonly PostProcessSettings.ToneMappingSettings tone_mapping;

        public ResolvedPostProcessSettings(PostProcessSettings.ExposureSettings exposure,
            PostProcessSettings.ToneMappingSettings tone_mapping)
        {
            this.exposure = exposure;
            this.tone_mapping = PostProcessSettings.ToneMappingSettings.Validate(tone_mapping);
        }

        public static ResolvedPostProcessSettings FromFallback(PostProcessSettings settings)
        {
            return new ResolvedPostProcessSettings(
                PostProcessSettings.GetExposure(settings),
                PostProcessSettings.GetToneMapping(settings));
        }
    }

    [Serializable]
    [VolumeComponentMenu("YutrelRP/Scene Render Settings")]
    [SupportedOnRenderPipeline(typeof(YutrelRPAsset))]
    public sealed class YutrelSceneRenderSettings : VolumeComponent
    {
        [Tooltip("Fixed EV100 exposure used to derive YutrelRP pre-exposure.")]
        public ClampedFloatParameter fixedEV100 = new(
            PostProcessSettings.ExposureSettings.DefaultFixedEV100,
            PostProcessSettings.ExposureSettings.MinFixedEV100,
            PostProcessSettings.ExposureSettings.MaxFixedEV100);

        [Tooltip("Exposure compensation in EV applied on top of Fixed EV100.")]
        public ClampedFloatParameter exposureCompensation = new(
            PostProcessSettings.ExposureSettings.DefaultExposureCompensation,
            PostProcessSettings.ExposureSettings.MinExposureCompensation,
            PostProcessSettings.ExposureSettings.MaxExposureCompensation);

        [Tooltip("Tone mapping mode used by YutrelRP after scene lighting.")]
        public YutrelToneMappingModeParameter toneMapping = new(PostProcessSettings.ToneMappingSettings.Default.mode);

        public static ResolvedPostProcessSettings Resolve(PostProcessSettings fallback_settings, VolumeStack stack)
        {
            var resolved = ResolvedPostProcessSettings.FromFallback(fallback_settings);
            var scene_settings = stack?.GetComponent<YutrelSceneRenderSettings>();
            return scene_settings == null ? resolved : scene_settings.Resolve(resolved);
        }

        private ResolvedPostProcessSettings Resolve(ResolvedPostProcessSettings fallback)
        {
            var exposure = fallback.exposure;
            if (fixedEV100.overrideState)
            {
                exposure.fixedEV100 = fixedEV100.value;
            }

            if (exposureCompensation.overrideState)
            {
                exposure.exposureCompensation = exposureCompensation.value;
            }

            var tone_mapping = fallback.tone_mapping;
            if (toneMapping.overrideState &&
                PostProcessSettings.ToneMappingSettings.IsValidMode(toneMapping.value))
            {
                tone_mapping.mode = toneMapping.value;
            }

            return new ResolvedPostProcessSettings(exposure, tone_mapping);
        }
    }

    [Serializable]
    public sealed class YutrelToneMappingModeParameter : VolumeParameter<PostProcessSettings.ToneMappingSettings.Mode>
    {
        public YutrelToneMappingModeParameter(PostProcessSettings.ToneMappingSettings.Mode value,
            bool overrideState = false) : base(value, overrideState)
        {
        }
    }
}
