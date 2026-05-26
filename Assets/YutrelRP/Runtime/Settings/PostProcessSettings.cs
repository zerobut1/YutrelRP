using System;
using UnityEngine;

namespace YutrelRP
{
    [CreateAssetMenu(menuName = "YutrelRP/Post Process Settings")]
    public class PostProcessSettings : ScriptableObject
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

            [Range(MinFixedEV100, MaxFixedEV100)] public float fixedEV100;
            [Range(MinExposureCompensation, MaxExposureCompensation)] public float exposureCompensation;

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

            public Mode mode;
        }

        [SerializeField] ExposureSettings m_exposure = ExposureSettings.Default;
        [SerializeField] ToneMappingSettings m_tone_mapping;

        public ExposureSettings exposure => m_exposure;
        public ToneMappingSettings tone_mapping => m_tone_mapping;

        public static ExposureSettings GetExposure(PostProcessSettings settings)
        {
            return settings == null ? ExposureSettings.Default : settings.exposure;
        }
    }
}
