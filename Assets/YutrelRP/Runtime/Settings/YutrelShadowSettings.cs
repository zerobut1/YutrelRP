using UnityEngine;
using UnityEngine.Rendering;

namespace YutrelRP
{
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
