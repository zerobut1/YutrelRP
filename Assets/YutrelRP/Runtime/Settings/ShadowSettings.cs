using UnityEngine;
using UnityEngine.Rendering;

namespace YutrelRP
{
    [System.Serializable]
    public class ShadowSettings
    {
        public const float MinMaxDistance = 0.001f;
        public const float MinDistanceFade = 0.001f;
        public const float MinCascadeFade = 0.001f;
        public const int MinCascadeCount = 1;
        public const int MaxCascadeCount = 4;
        public const int DefaultNumIterationsEnclosingSphere = 64;

        [Min(MinMaxDistance)]
        public float max_distance = 100.0f;

        [Range(MinDistanceFade, 1.0f)]
        public float distance_fade = 0.1f;

        public enum MapSize
        {
            _256 = 256,
            _512 = 512,
            _1024 = 1024,
            _2048 = 2048,
            _4096 = 4096,
            _8192 = 8192,
        }

        public DepthBits directional_depth_bits = DepthBits.Depth32;

        public bool conservative_enclosing_sphere = true;

        [Min(1)]
        public int num_iterations_enclosing_sphere = DefaultNumIterationsEnclosingSphere;

        [System.Serializable]
        public struct Directional
        {
            public enum SoftShadowQuality
            {
                None = 0,
                Low = 1,
                Medium = 2,
                High = 3,
            }

            public MapSize atlas_tile_size;

            public SoftShadowQuality soft_shadow_quality;

            [Range(MinCascadeCount, MaxCascadeCount)]
            public int cascade_count;

            [Range(0.0f, 1.0f)]
            public float cascade_ratio_1, cascade_ratio_2, cascade_ratio_3;

            [Range(MinCascadeFade, 1.0f)]
            public float cascade_fade;

            public readonly Vector3 cascade_ratios => new(cascade_ratio_1, cascade_ratio_2, cascade_ratio_3);
        }

        public Directional directional = new Directional
        {
            atlas_tile_size = MapSize._2048,
            soft_shadow_quality = Directional.SoftShadowQuality.Medium,
            cascade_count = 4,
            cascade_ratio_1 = 0.1f,
            cascade_ratio_2 = 0.25f,
            cascade_ratio_3 = 0.5f,
            cascade_fade = 0.1f,
        };
    }

    public readonly struct ResolvedShadowSettings
    {
        public readonly float max_distance;
        public readonly float distance_fade;
        public readonly DepthBits directional_depth_bits;
        public readonly bool conservative_enclosing_sphere;
        public readonly int num_iterations_enclosing_sphere;
        public readonly Directional directional;

        public ResolvedShadowSettings(
            float max_distance,
            float distance_fade,
            DepthBits directional_depth_bits,
            bool conservative_enclosing_sphere,
            int num_iterations_enclosing_sphere,
            Directional directional)
        {
            this.max_distance = Mathf.Max(ShadowSettings.MinMaxDistance, max_distance);
            this.distance_fade = Mathf.Clamp(distance_fade, ShadowSettings.MinDistanceFade, 1.0f);
            this.directional_depth_bits = ValidateDepthBits(directional_depth_bits);
            this.conservative_enclosing_sphere = conservative_enclosing_sphere;
            this.num_iterations_enclosing_sphere = Mathf.Max(1, num_iterations_enclosing_sphere);
            this.directional = Directional.Validate(directional);
        }

        public static ResolvedShadowSettings FromProjectSettings(ShadowSettings settings)
        {
            if (settings == null)
            {
                settings = new ShadowSettings();
            }

            return new ResolvedShadowSettings(
                settings.max_distance,
                settings.distance_fade,
                settings.directional_depth_bits,
                settings.conservative_enclosing_sphere,
                settings.num_iterations_enclosing_sphere,
                new Directional(
                    settings.directional.atlas_tile_size,
                    settings.directional.soft_shadow_quality,
                    settings.directional.cascade_count,
                    settings.directional.cascade_ratios,
                    settings.directional.cascade_fade));
        }

        private static DepthBits ValidateDepthBits(DepthBits depth_bits)
        {
            return depth_bits switch
            {
                DepthBits.Depth16 => DepthBits.Depth16,
                DepthBits.Depth24 => DepthBits.Depth24,
                DepthBits.Depth32 => DepthBits.Depth32,
                _ => DepthBits.Depth32
            };
        }

        public readonly struct Directional
        {
            public readonly ShadowSettings.MapSize atlas_tile_size;
            public readonly ShadowSettings.Directional.SoftShadowQuality soft_shadow_quality;
            public readonly int cascade_count;
            public readonly Vector3 cascade_ratios;
            public readonly float cascade_fade;

            public Directional(
                ShadowSettings.MapSize atlas_tile_size,
                ShadowSettings.Directional.SoftShadowQuality soft_shadow_quality,
                int cascade_count,
                Vector3 cascade_ratios,
                float cascade_fade)
            {
                this.atlas_tile_size = atlas_tile_size;
                this.soft_shadow_quality = ValidateSoftShadowQuality(soft_shadow_quality);
                this.cascade_count = Mathf.Clamp(
                    cascade_count,
                    ShadowSettings.MinCascadeCount,
                    ShadowSettings.MaxCascadeCount);
                this.cascade_ratios = ValidateCascadeRatios(cascade_ratios, this.cascade_count);
                this.cascade_fade = Mathf.Clamp(cascade_fade, ShadowSettings.MinCascadeFade, 1.0f);
            }

            public static Directional Validate(Directional settings)
            {
                return new Directional(
                    settings.atlas_tile_size,
                    settings.soft_shadow_quality,
                    settings.cascade_count,
                    settings.cascade_ratios,
                    settings.cascade_fade);
            }

            private static ShadowSettings.Directional.SoftShadowQuality ValidateSoftShadowQuality(
                ShadowSettings.Directional.SoftShadowQuality quality)
            {
                return quality switch
                {
                    ShadowSettings.Directional.SoftShadowQuality.None => quality,
                    ShadowSettings.Directional.SoftShadowQuality.Low => quality,
                    ShadowSettings.Directional.SoftShadowQuality.Medium => quality,
                    ShadowSettings.Directional.SoftShadowQuality.High => quality,
                    _ => ShadowSettings.Directional.SoftShadowQuality.Medium
                };
            }

            private static Vector3 ValidateCascadeRatios(Vector3 ratios, int cascade_count)
            {
                float ratio_1 = Mathf.Clamp01(ratios.x);
                float ratio_2 = Mathf.Clamp01(ratios.y);
                float ratio_3 = Mathf.Clamp01(ratios.z);

                if (cascade_count >= 3)
                {
                    ratio_2 = Mathf.Max(ratio_2, ratio_1);
                }

                if (cascade_count >= 4)
                {
                    ratio_3 = Mathf.Max(ratio_3, ratio_2);
                }

                return new Vector3(ratio_1, ratio_2, ratio_3);
            }
        }
    }

    [System.Serializable]
    [VolumeComponentMenu("YutrelRP/Shadow Settings")]
    [SupportedOnRenderPipeline(typeof(YutrelRPAsset))]
    public sealed class YutrelShadowSettings : VolumeComponent
    {
        [Tooltip("Maximum distance at which realtime shadows are rendered.")]
        public MinFloatParameter maxDistance = new(100.0f, ShadowSettings.MinMaxDistance);

        [Tooltip("Distance fade range for realtime shadows.")]
        public ClampedFloatParameter distanceFade = new(0.1f, ShadowSettings.MinDistanceFade, 1.0f);

        [Tooltip("Number of cascades used by directional shadows.")]
        public ClampedIntParameter directionalCascadeCount = new(
            4,
            ShadowSettings.MinCascadeCount,
            ShadowSettings.MaxCascadeCount);

        [Tooltip("First directional shadow cascade split ratio.")]
        public ClampedFloatParameter directionalCascadeRatio1 = new(0.1f, 0.0f, 1.0f);

        [Tooltip("Second directional shadow cascade split ratio.")]
        public ClampedFloatParameter directionalCascadeRatio2 = new(0.25f, 0.0f, 1.0f);

        [Tooltip("Third directional shadow cascade split ratio.")]
        public ClampedFloatParameter directionalCascadeRatio3 = new(0.5f, 0.0f, 1.0f);

        [Tooltip("Directional shadow cascade fade range.")]
        public ClampedFloatParameter directionalCascadeFade = new(0.1f, ShadowSettings.MinCascadeFade, 1.0f);

        [Tooltip("Use conservative enclosing spheres for shadow cascade culling.")]
        public BoolParameter conservativeEnclosingSphere = new(true);

        [Tooltip("Iteration count used by conservative enclosing sphere culling.")]
        public MinIntParameter numIterationsEnclosingSphere = new(
            ShadowSettings.DefaultNumIterationsEnclosingSphere,
            1);

        public static ResolvedShadowSettings Resolve(ShadowSettings project_settings, VolumeStack stack)
        {
            var resolved = ResolvedShadowSettings.FromProjectSettings(project_settings);
            var volume_settings = stack?.GetComponent<YutrelShadowSettings>();
            return volume_settings == null ? resolved : volume_settings.Resolve(resolved);
        }

        private ResolvedShadowSettings Resolve(ResolvedShadowSettings fallback)
        {
            float resolved_max_distance = maxDistance.overrideState ? maxDistance.value : fallback.max_distance;
            float resolved_distance_fade = distanceFade.overrideState ? distanceFade.value : fallback.distance_fade;
            int resolved_cascade_count = directionalCascadeCount.overrideState
                ? directionalCascadeCount.value
                : fallback.directional.cascade_count;

            var resolved_cascade_ratios = fallback.directional.cascade_ratios;
            if (directionalCascadeRatio1.overrideState)
            {
                resolved_cascade_ratios.x = directionalCascadeRatio1.value;
            }

            if (directionalCascadeRatio2.overrideState)
            {
                resolved_cascade_ratios.y = directionalCascadeRatio2.value;
            }

            if (directionalCascadeRatio3.overrideState)
            {
                resolved_cascade_ratios.z = directionalCascadeRatio3.value;
            }

            float resolved_cascade_fade = directionalCascadeFade.overrideState
                ? directionalCascadeFade.value
                : fallback.directional.cascade_fade;
            bool resolved_conservative_enclosing_sphere = conservativeEnclosingSphere.overrideState
                ? conservativeEnclosingSphere.value
                : fallback.conservative_enclosing_sphere;
            int resolved_num_iterations_enclosing_sphere = numIterationsEnclosingSphere.overrideState
                ? numIterationsEnclosingSphere.value
                : fallback.num_iterations_enclosing_sphere;

            var directional = new ResolvedShadowSettings.Directional(
                fallback.directional.atlas_tile_size,
                fallback.directional.soft_shadow_quality,
                resolved_cascade_count,
                resolved_cascade_ratios,
                resolved_cascade_fade);

            return new ResolvedShadowSettings(
                resolved_max_distance,
                resolved_distance_fade,
                fallback.directional_depth_bits,
                resolved_conservative_enclosing_sphere,
                resolved_num_iterations_enclosing_sphere,
                directional);
        }
    }
}
