using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace YutrelRP
{
    public readonly struct ResolvedDDGISettings
    {
        public readonly bool enabled;
        public readonly EncodingSettings encoding;
        public readonly SamplingSettings sampling;
        public readonly BlendingSettings blending;
        public readonly RelocationSettings relocation;
        public readonly ClassificationSettings classification;

        public ResolvedDDGISettings(
            bool enabled,
            EncodingSettings encoding,
            SamplingSettings sampling,
            BlendingSettings blending,
            RelocationSettings relocation,
            ClassificationSettings classification)
        {
            this.enabled = enabled;
            this.encoding = encoding;
            this.sampling = sampling;
            this.blending = blending;
            this.relocation = relocation;
            this.classification = classification;
        }

        public static ResolvedDDGISettings FromProjectSettings(YutrelRPSettings.DDGISettings settings)
        {
            settings ??= new YutrelRPSettings.DDGISettings();

            var encoding = settings.encoding ?? new YutrelRPSettings.DDGISettings.EncodingSettings();
            var sampling = settings.sampling ?? new YutrelRPSettings.DDGISettings.SamplingSettings();
            var blending = settings.blending ?? new YutrelRPSettings.DDGISettings.BlendingSettings();
            var relocation = settings.relocation ?? new YutrelRPSettings.DDGISettings.RelocationSettings();
            var classification = settings.classification ?? new YutrelRPSettings.DDGISettings.ClassificationSettings();

            return new ResolvedDDGISettings(
                settings.enabled,
                new EncodingSettings(encoding.lightingIntensityScale, encoding.irradianceEncodingGamma),
                new SamplingSettings(sampling.probeNormalBias, sampling.probeViewBias),
                new BlendingSettings(
                    blending.probeHysteresis,
                    blending.distanceExponent,
                    blending.irradianceThreshold,
                    blending.brightnessThreshold,
                    blending.probeRandomRayBackfaceThreshold),
                new RelocationSettings(
                    relocation.enabled,
                    relocation.probeMinFrontfaceDistance,
                    relocation.probeFixedRayBackfaceThreshold),
                new ClassificationSettings(classification.enabled));
        }

        public readonly struct EncodingSettings
        {
            public readonly float lightingIntensityScale;
            public readonly float irradianceEncodingGamma;

            public EncodingSettings(float lightingIntensityScale, float irradianceEncodingGamma)
            {
                this.lightingIntensityScale = Mathf.Max(0.001f, lightingIntensityScale);
                this.irradianceEncodingGamma = Mathf.Max(0.01f, irradianceEncodingGamma);
            }
        }

        public readonly struct SamplingSettings
        {
            public readonly float probeNormalBias;
            public readonly float probeViewBias;

            public SamplingSettings(float probeNormalBias, float probeViewBias)
            {
                this.probeNormalBias = Mathf.Max(0.0f, probeNormalBias);
                this.probeViewBias = Mathf.Max(0.0f, probeViewBias);
            }
        }

        public readonly struct BlendingSettings
        {
            public readonly float probeHysteresis;
            public readonly float distanceExponent;
            public readonly float irradianceThreshold;
            public readonly float brightnessThreshold;
            public readonly float probeRandomRayBackfaceThreshold;

            public BlendingSettings(
                float probeHysteresis,
                float distanceExponent,
                float irradianceThreshold,
                float brightnessThreshold,
                float probeRandomRayBackfaceThreshold)
            {
                this.probeHysteresis = Mathf.Clamp01(probeHysteresis);
                this.distanceExponent = Mathf.Max(0.01f, distanceExponent);
                this.irradianceThreshold = Mathf.Max(0.0f, irradianceThreshold);
                this.brightnessThreshold = Mathf.Max(0.0f, brightnessThreshold);
                this.probeRandomRayBackfaceThreshold = Mathf.Clamp01(probeRandomRayBackfaceThreshold);
            }
        }

        public readonly struct RelocationSettings
        {
            public readonly bool enabled;
            public readonly float probeMinFrontfaceDistance;
            public readonly float probeFixedRayBackfaceThreshold;

            public RelocationSettings(bool enabled, float probeMinFrontfaceDistance,
                float probeFixedRayBackfaceThreshold)
            {
                this.enabled = enabled;
                this.probeMinFrontfaceDistance = Mathf.Max(0.0f, probeMinFrontfaceDistance);
                this.probeFixedRayBackfaceThreshold = Mathf.Clamp01(probeFixedRayBackfaceThreshold);
            }
        }

        public readonly struct ClassificationSettings
        {
            public readonly bool enabled;

            public ClassificationSettings(bool enabled)
            {
                this.enabled = enabled;
            }
        }
    }

    [Serializable]
    [VolumeComponentMenu("YutrelRP/DDGI Settings")]
    [SupportedOnRenderPipeline(typeof(YutrelRPAsset))]
    public sealed class YutrelDDGISettings : VolumeComponent
    {
        [Tooltip("Enable Dynamic Diffuse Global Illumination for this scene.")]
        public BoolParameter enabled = new(false);

        [Header("Encoding")]
        [Tooltip("Physical light intensity mapped to 1.0 in the RTXGI DDGI lighting domain.")]
        public MinFloatParameter lightingIntensityScale = new(100000.0f, 0.001f);

        [Tooltip("Gamma used to encode probe irradiance.")]
        public MinFloatParameter irradianceEncodingGamma = new(5.0f, 0.01f);

        [Header("Sampling")]
        [Tooltip("Normal bias applied when sampling DDGI probes.")]
        public MinFloatParameter probeNormalBias = new(0.2f, 0.0f);

        [Tooltip("View bias applied when sampling DDGI probes.")]
        public MinFloatParameter probeViewBias = new(0.1f, 0.0f);

        [Header("Blending")]
        [Tooltip("Probe history hysteresis for irradiance and distance blending.")]
        public ClampedFloatParameter probeHysteresis = new(0.97f, 0.0f, 1.0f);

        [Tooltip("Distance exponent used when blending probe distance moments.")]
        public MinFloatParameter distanceExponent = new(50.0f, 0.01f);

        [Tooltip("Irradiance threshold used by RTXGI probe blending.")]
        public MinFloatParameter irradianceThreshold = new(0.2f, 0.0f);

        [Tooltip("Brightness threshold used by RTXGI probe blending.")]
        public MinFloatParameter brightnessThreshold = new(2.0f, 0.0f);

        [Tooltip("Backface threshold for random probe rays.")]
        public ClampedFloatParameter probeRandomRayBackfaceThreshold = new(0.1f, 0.0f, 1.0f);

        [Header("Relocation")]
        [Tooltip("Enable probe relocation.")]
        public BoolParameter relocationEnabled = new(false);

        [Tooltip("Minimum frontface distance used by probe relocation.")]
        public MinFloatParameter probeMinFrontfaceDistance = new(0.1f, 0.0f);

        [Tooltip("Backface threshold for fixed relocation rays.")]
        public ClampedFloatParameter probeFixedRayBackfaceThreshold = new(0.25f, 0.0f, 1.0f);

        [Header("Classification")]
        [Tooltip("Enable probe classification.")]
        public BoolParameter classificationEnabled = new(false);

        public static ResolvedDDGISettings Resolve(YutrelRPSettings.DDGISettings fallback, VolumeStack stack)
        {
            var resolved = ResolvedDDGISettings.FromProjectSettings(fallback);
            var volume_settings = stack?.GetComponent<YutrelDDGISettings>();
            return volume_settings == null ? resolved : volume_settings.Resolve(resolved);
        }

        private ResolvedDDGISettings Resolve(ResolvedDDGISettings fallback)
        {
            return new ResolvedDDGISettings(
                enabled.overrideState ? enabled.value : fallback.enabled,
                new ResolvedDDGISettings.EncodingSettings(
                    lightingIntensityScale.overrideState
                        ? lightingIntensityScale.value
                        : fallback.encoding.lightingIntensityScale,
                    irradianceEncodingGamma.overrideState
                        ? irradianceEncodingGamma.value
                        : fallback.encoding.irradianceEncodingGamma),
                new ResolvedDDGISettings.SamplingSettings(
                    probeNormalBias.overrideState ? probeNormalBias.value : fallback.sampling.probeNormalBias,
                    probeViewBias.overrideState ? probeViewBias.value : fallback.sampling.probeViewBias),
                new ResolvedDDGISettings.BlendingSettings(
                    probeHysteresis.overrideState ? probeHysteresis.value : fallback.blending.probeHysteresis,
                    distanceExponent.overrideState ? distanceExponent.value : fallback.blending.distanceExponent,
                    irradianceThreshold.overrideState
                        ? irradianceThreshold.value
                        : fallback.blending.irradianceThreshold,
                    brightnessThreshold.overrideState ? brightnessThreshold.value : fallback.blending.brightnessThreshold,
                    probeRandomRayBackfaceThreshold.overrideState
                        ? probeRandomRayBackfaceThreshold.value
                        : fallback.blending.probeRandomRayBackfaceThreshold),
                new ResolvedDDGISettings.RelocationSettings(
                    relocationEnabled.overrideState ? relocationEnabled.value : fallback.relocation.enabled,
                    probeMinFrontfaceDistance.overrideState
                        ? probeMinFrontfaceDistance.value
                        : fallback.relocation.probeMinFrontfaceDistance,
                    probeFixedRayBackfaceThreshold.overrideState
                        ? probeFixedRayBackfaceThreshold.value
                        : fallback.relocation.probeFixedRayBackfaceThreshold),
                new ResolvedDDGISettings.ClassificationSettings(
                    classificationEnabled.overrideState
                        ? classificationEnabled.value
                        : fallback.classification.enabled));
        }
    }
}
