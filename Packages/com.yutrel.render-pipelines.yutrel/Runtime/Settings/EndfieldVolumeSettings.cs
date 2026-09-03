using UnityEngine;
using UnityEngine.Rendering;

namespace YutrelRP
{
    public readonly struct ResolvedEndfieldSettings
    {
        public static readonly Color DefaultShadowFillColor =
            new(0.8490771f, 0.8957686f, 1.150923f, 1.0f);

        public const float DefaultShadowFillIntensity = 0.28772247f;

        public readonly Color shadow_fill_color;
        public readonly float shadow_fill_intensity;

        public ResolvedEndfieldSettings(Color shadow_fill_color, float shadow_fill_intensity)
        {
            this.shadow_fill_color = shadow_fill_color;
            this.shadow_fill_intensity = Mathf.Max(0.0f, shadow_fill_intensity);
        }

        public static ResolvedEndfieldSettings Default => new(
            DefaultShadowFillColor,
            DefaultShadowFillIntensity);

        public static ResolvedEndfieldSettings Resolve(VolumeStack stack)
        {
            var volume_settings = stack?.GetComponent<EndfieldVolumeSettings>();
            return volume_settings == null ? Default : volume_settings.Resolve();
        }
    }

    [VolumeComponentMenu("YutrelRP/Endfield Settings")]
    [SupportedOnRenderPipeline(typeof(YutrelRPAsset))]
    public sealed class EndfieldVolumeSettings : VolumeComponent
    {
        [Tooltip("Capture-calibrated flat ambient chromaticity. Not multiplied by PreExposure.")]
        public ColorParameter shadowFillColor = new(
            ResolvedEndfieldSettings.DefaultShadowFillColor,
            hdr: true,
            showAlpha: false,
            showEyeDropper: true);

        [Tooltip("Capture-calibrated flat ambient intensity. Not multiplied by PreExposure.")]
        public MinFloatParameter shadowFillIntensity = new(
            ResolvedEndfieldSettings.DefaultShadowFillIntensity,
            0.0f);

        public static ResolvedEndfieldSettings Resolve(VolumeStack stack)
        {
            return ResolvedEndfieldSettings.Resolve(stack);
        }

        internal ResolvedEndfieldSettings Resolve()
        {
            return new ResolvedEndfieldSettings(shadowFillColor.value, shadowFillIntensity.value);
        }
    }
}
