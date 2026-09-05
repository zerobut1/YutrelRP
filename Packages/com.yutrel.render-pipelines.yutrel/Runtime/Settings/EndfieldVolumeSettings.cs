using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Serialization;

namespace YutrelRP
{
    public readonly struct ResolvedEndfieldSettings
    {
        public static readonly Color DefaultAmbientColor =
            new(0.8490771f, 0.8957686f, 1.150923f, 1.0f);

        public const float DefaultAmbientIntensity = 0.28772247f;
        public const float DefaultLightingReferenceScale = 1.6243868f;
        public const float MinLightingReferenceScale = 0.0001f;

        public readonly float lighting_reference_scale;
        public readonly Color ambient_color;
        public readonly float ambient_intensity;

        public ResolvedEndfieldSettings(Color ambient_color, float ambient_intensity,
            float lighting_reference_scale = DefaultLightingReferenceScale)
        {
            this.lighting_reference_scale = Mathf.Max(MinLightingReferenceScale, lighting_reference_scale);
            this.ambient_color = ambient_color;
            this.ambient_intensity = Mathf.Max(0.0f, ambient_intensity);
        }

        public static ResolvedEndfieldSettings Default => new(
            DefaultAmbientColor,
            DefaultAmbientIntensity);

        public static ResolvedEndfieldSettings Resolve(VolumeStack stack)
        {
            var volume_settings = stack?.GetComponent<EndfieldVolumeSettings>();
            return volume_settings == null ? Default : volume_settings.Resolve();
        }
    }

    [VolumeComponentMenu("YutrelRP/Endfield Settings")]
    [DisplayInfo(name = "Endfield Settings")]
    [SupportedOnRenderPipeline(typeof(YutrelRPAsset))]
    public sealed class EndfieldVolumeSettings : VolumeComponent
    {
        [Tooltip("Core directional light intensity at 100000 lux. Output uses the reciprocal scale before camera exposure.")]
        public MinFloatParameter lightingReferenceScale = new(
            ResolvedEndfieldSettings.DefaultLightingReferenceScale,
            ResolvedEndfieldSettings.MinLightingReferenceScale);

        [Header("Ambient")]
        [DisplayInfo(name = "Color")]
        [FormerlySerializedAs("shadowFillColor")]
        [Tooltip("Flat ambient tint shared by all Endfield materials. HDR values are passed directly to the stylized shading.")]
        public ColorParameter ambientColor = new(
            ResolvedEndfieldSettings.DefaultAmbientColor,
            hdr: true,
            showAlpha: false,
            showEyeDropper: true);

        [DisplayInfo(name = "Intensity")]
        [FormerlySerializedAs("shadowFillIntensity")]
        [Tooltip("Flat ambient intensity in Core units. Zero retains the shader's base shadow fill. Output receives the reciprocal lighting scale and camera exposure.")]
        public MinFloatParameter ambientIntensity = new(
            ResolvedEndfieldSettings.DefaultAmbientIntensity,
            0.0f);

        public static ResolvedEndfieldSettings Resolve(VolumeStack stack)
        {
            return ResolvedEndfieldSettings.Resolve(stack);
        }

        internal ResolvedEndfieldSettings Resolve()
        {
            return new ResolvedEndfieldSettings(
                ambientColor.value, ambientIntensity.value, lightingReferenceScale.value);
        }
    }
}
