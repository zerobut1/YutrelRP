using UnityEngine;
using UnityEngine.Rendering;

namespace YutrelRP
{
    public readonly struct ResolvedNtePalette
    {
        public readonly Color shape;
        public readonly Color at_one;
        public readonly Color at_zero;

        public ResolvedNtePalette(Color shape, Color at_one, Color at_zero)
        {
            this.shape = shape;
            this.at_one = at_one;
            this.at_zero = at_zero;
        }
    }

    public readonly struct ResolvedNteSettings
    {
        // Fixed YutrelRP adaptation calibration: EV100 14, compensation 0.
        public const float ReferencePreExposure = 1.0f / (1.2f * 16384.0f);
        public const float NativeExposureInput = 1.0f;
        public const float DefaultOutputMultiplier = 1.0f;
        public const float DefaultRimWidth = 2.5f;

        public static readonly Vector3 DefaultLightDirection =
            new(-0.9709105f, 0.19238801f, 0.14254709f);

        public static readonly Color DefaultCommonEndpoint = new(1.01f, 1.01f, 1.01f, 1.0f);
        public static readonly Color DefaultRimColorAtOne = new(0.018112f, 0.020736f, 0.03125f, 1.0f);
        public static readonly Color DefaultRimColorAtZero = new(0.0f, 0.0f, 0.0f, 1.0f);

        public static readonly ResolvedNtePalette DefaultPalette0 = new(
            new Color(1.01f, 1.01f, 1.01f, 1.0f),
            new Color(0.527845f, 0.498751f, 0.63f, 1.0f),
            new Color(0.737483f, 0.696832f, 0.880208f, 1.0f));

        public static readonly ResolvedNtePalette DefaultPalette1 = new(
            new Color(0.99f, 0.99f, 0.99f, 1.0f),
            new Color(0.638391f, 0.624802f, 0.8f, 1.0f),
            new Color(0.797989f, 0.781002f, 1.0f, 1.0f));

        public static readonly ResolvedNtePalette DefaultPalette2 = new(
            new Color(1.01f, 1.01f, 1.01f, 1.0f),
            new Color(0.700364f, 0.590656f, 0.75f, 1.0f),
            new Color(0.840437f, 0.708787f, 0.9f, 1.0f));

        public static readonly ResolvedNtePalette DefaultPalette3 = new(
            new Color(0.99f, 0.99f, 0.99f, 1.0f),
            new Color(0.75f, 0.567615f, 0.70968f, 1.0f),
            new Color(0.9f, 0.681138f, 0.851616f, 1.0f));

        public static readonly ResolvedNtePalette DefaultPalette4 = new(
            new Color(1.01f, 1.01f, 1.01f, 1.0f),
            new Color(0.462927f, 0.492974f, 0.6f, 1.0f),
            new Color(0.55053f, 0.586263f, 0.713542f, 1.0f));

        public readonly ResolvedNtePalette palette0;
        public readonly ResolvedNtePalette palette1;
        public readonly ResolvedNtePalette palette2;
        public readonly ResolvedNtePalette palette3;
        public readonly ResolvedNtePalette palette4;
        public readonly Color common_endpoint;
        public readonly Color rim_color_at_one;
        public readonly Color rim_color_at_zero;
        public readonly float rim_width;
        public readonly float output_multiplier;

        public ResolvedNteSettings(ResolvedNtePalette palette0, ResolvedNtePalette palette1,
            ResolvedNtePalette palette2, ResolvedNtePalette palette3, ResolvedNtePalette palette4,
            Color common_endpoint, Color rim_color_at_one, Color rim_color_at_zero,
            float rim_width, float output_multiplier)
        {
            this.palette0 = palette0;
            this.palette1 = palette1;
            this.palette2 = palette2;
            this.palette3 = palette3;
            this.palette4 = palette4;
            this.common_endpoint = common_endpoint;
            this.rim_color_at_one = rim_color_at_one;
            this.rim_color_at_zero = rim_color_at_zero;
            this.rim_width = Mathf.Max(0.0f, rim_width);
            this.output_multiplier = Mathf.Max(0.0f, output_multiplier);
        }

        public static ResolvedNteSettings Default => new(
            DefaultPalette0, DefaultPalette1, DefaultPalette2, DefaultPalette3, DefaultPalette4,
            DefaultCommonEndpoint, DefaultRimColorAtOne, DefaultRimColorAtZero,
            DefaultRimWidth, DefaultOutputMultiplier);

        public float GetOutputScale(float current_pre_exposure)
        {
            return output_multiplier * current_pre_exposure / ReferencePreExposure;
        }

        public static ResolvedNteSettings Resolve(VolumeStack stack)
        {
            var volume_settings = stack?.GetComponent<NteVolumeSettings>();
            return volume_settings == null ? Default : volume_settings.Resolve();
        }
    }

    [VolumeComponentMenu("YutrelRP/NTE Settings")]
    [DisplayInfo(name = "NTE Settings")]
    [SupportedOnRenderPipeline(typeof(YutrelRPAsset))]
    public sealed class NteVolumeSettings : VolumeComponent
    {
        [Header("Palette 0")]
        public ColorParameter palette0Shape = Hdr(ResolvedNteSettings.DefaultPalette0.shape);
        public ColorParameter palette0AtOne = Hdr(ResolvedNteSettings.DefaultPalette0.at_one);
        public ColorParameter palette0AtZero = Hdr(ResolvedNteSettings.DefaultPalette0.at_zero);

        [Header("Palette 1")]
        public ColorParameter palette1Shape = Hdr(ResolvedNteSettings.DefaultPalette1.shape);
        public ColorParameter palette1AtOne = Hdr(ResolvedNteSettings.DefaultPalette1.at_one);
        public ColorParameter palette1AtZero = Hdr(ResolvedNteSettings.DefaultPalette1.at_zero);

        [Header("Palette 2")]
        public ColorParameter palette2Shape = Hdr(ResolvedNteSettings.DefaultPalette2.shape);
        public ColorParameter palette2AtOne = Hdr(ResolvedNteSettings.DefaultPalette2.at_one);
        public ColorParameter palette2AtZero = Hdr(ResolvedNteSettings.DefaultPalette2.at_zero);

        [Header("Palette 3")]
        public ColorParameter palette3Shape = Hdr(ResolvedNteSettings.DefaultPalette3.shape);
        public ColorParameter palette3AtOne = Hdr(ResolvedNteSettings.DefaultPalette3.at_one);
        public ColorParameter palette3AtZero = Hdr(ResolvedNteSettings.DefaultPalette3.at_zero);

        [Header("Palette 4")]
        public ColorParameter palette4Shape = Hdr(ResolvedNteSettings.DefaultPalette4.shape);
        public ColorParameter palette4AtOne = Hdr(ResolvedNteSettings.DefaultPalette4.at_one);
        public ColorParameter palette4AtZero = Hdr(ResolvedNteSettings.DefaultPalette4.at_zero);

        [Header("Palette Common Endpoint")]
        public ColorParameter commonEndpoint = Hdr(ResolvedNteSettings.DefaultCommonEndpoint);

        [Header("Screen-Depth Rim")]
        public ColorParameter rimColorAtOne = Hdr(ResolvedNteSettings.DefaultRimColorAtOne);
        public ColorParameter rimColorAtZero = Hdr(ResolvedNteSettings.DefaultRimColorAtZero);
        public MinFloatParameter rimWidth = new(ResolvedNteSettings.DefaultRimWidth, 0.0f);

        [Header("Scene Output")]
        [Tooltip("Independent NTE scene-color multiplier before the camera exposure ratio.")]
        public MinFloatParameter outputMultiplier = new(ResolvedNteSettings.DefaultOutputMultiplier, 0.0f);

        public static ResolvedNteSettings Resolve(VolumeStack stack) =>
            ResolvedNteSettings.Resolve(stack);

        internal ResolvedNteSettings Resolve()
        {
            return new ResolvedNteSettings(
                Palette(palette0Shape, palette0AtOne, palette0AtZero),
                Palette(palette1Shape, palette1AtOne, palette1AtZero),
                Palette(palette2Shape, palette2AtOne, palette2AtZero),
                Palette(palette3Shape, palette3AtOne, palette3AtZero),
                Palette(palette4Shape, palette4AtOne, palette4AtZero),
                commonEndpoint.value,
                rimColorAtOne.value,
                rimColorAtZero.value,
                rimWidth.value,
                outputMultiplier.value);
        }

        private static ColorParameter Hdr(Color value) =>
            new(value, hdr: true, showAlpha: false, showEyeDropper: true);

        private static ResolvedNtePalette Palette(ColorParameter shape,
            ColorParameter at_one, ColorParameter at_zero) =>
            new(shape.value, at_one.value, at_zero.value);
    }
}
