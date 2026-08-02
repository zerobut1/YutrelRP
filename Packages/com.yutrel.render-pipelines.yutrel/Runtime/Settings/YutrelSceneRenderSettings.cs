using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace YutrelRP
{
    [Serializable]
    public struct ExposureSettings
    {
        public const float MinFixedEV100 = -10.0f;
        public const float MaxFixedEV100 = 20.0f;
        public const float DefaultFixedEV100 = 14.0f;
        public const float MinExposureCompensation = -15.0f;
        public const float MaxExposureCompensation = 15.0f;
        public const float DefaultExposureCompensation = 0.0f;

        public float fixedEV100;
        public float exposureCompensation;

        public static ExposureSettings Default => new()
        {
            fixedEV100 = DefaultFixedEV100,
            exposureCompensation = DefaultExposureCompensation
        };

        public float pre_exposure
        {
            get
            {
                var ev = Mathf.Clamp(fixedEV100, MinFixedEV100, MaxFixedEV100) +
                         Mathf.Clamp(exposureCompensation, MinExposureCompensation, MaxExposureCompensation);
                return 1.0f / (1.2f * Mathf.Pow(2.0f, ev));
            }
        }

        public float one_over_pre_exposure => 1.0f / pre_exposure;
    }

    [Serializable]
    public struct ToneMappingSettings
    {
        public enum Mode
        {
            None,
            ACES,
        }

        public static ToneMappingSettings Default => new()
        {
            mode = Mode.ACES
        };

        public Mode mode;

        public static bool IsValidMode(Mode mode)
        {
            switch (mode)
            {
                case Mode.None:
                case Mode.ACES:
                    return true;
                default:
                    return false;
            }
        }

        public static ToneMappingSettings Validate(ToneMappingSettings settings)
        {
            if (!IsValidMode(settings.mode))
            {
                settings.mode = Default.mode;
            }

            return settings;
        }
    }

    public readonly struct ResolvedPostProcessSettings
    {
        public readonly ExposureSettings exposure;
        public readonly ToneMappingSettings tone_mapping;

        public ResolvedPostProcessSettings(ExposureSettings exposure,
            ToneMappingSettings tone_mapping)
        {
            this.exposure = exposure;
            this.tone_mapping = ToneMappingSettings.Validate(tone_mapping);
        }

        public static ResolvedPostProcessSettings Default => new(ExposureSettings.Default, ToneMappingSettings.Default);
    }

    [Serializable]
    public sealed class YutrelToneMappingModeParameter : VolumeParameter<ToneMappingSettings.Mode>
    {
        public YutrelToneMappingModeParameter(ToneMappingSettings.Mode value,
            bool overrideState = false) : base(value, overrideState)
        {
        }
    }

    [Serializable]
    [VolumeComponentMenu("YutrelRP/Scene Render Settings")]
    [SupportedOnRenderPipeline(typeof(YutrelRPAsset))]
    public sealed class YutrelSceneRenderSettings : VolumeComponent
    {
        [Tooltip("Fixed EV100 exposure used to derive YutrelRP pre-exposure.")]
        public ClampedFloatParameter fixedEV100 = new(
            ExposureSettings.DefaultFixedEV100,
            ExposureSettings.MinFixedEV100,
            ExposureSettings.MaxFixedEV100);

        [Tooltip("Exposure compensation in EV applied on top of Fixed EV100.")]
        public ClampedFloatParameter exposureCompensation = new(
            ExposureSettings.DefaultExposureCompensation,
            ExposureSettings.MinExposureCompensation,
            ExposureSettings.MaxExposureCompensation);

        [Tooltip("Tone mapping mode used by YutrelRP after scene lighting.")]
        public YutrelToneMappingModeParameter toneMapping = new(ToneMappingSettings.Default.mode);

        public static ResolvedPostProcessSettings Resolve(VolumeStack stack)
        {
            var resolved = ResolvedPostProcessSettings.Default;
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
                ToneMappingSettings.IsValidMode(toneMapping.value))
            {
                tone_mapping.mode = toneMapping.value;
            }

            return new ResolvedPostProcessSettings(exposure, tone_mapping);
        }
    }
}
