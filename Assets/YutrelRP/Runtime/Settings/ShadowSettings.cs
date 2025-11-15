using UnityEngine;

namespace YutrelRP
{
    [System.Serializable]
    public class ShadowSettings
    {
        [Min(0f)]
        public float max_distance = 100.0f;

        public enum MapSize
        {
            _256 = 256,
            _512 = 512,
            _1024 = 1024,
            _2048 = 2048,
            _4096 = 4096,
            _8192 = 8192,
        }

        [System.Serializable]
        public struct Directional
        {
            public MapSize atlas_size;

            [Range(1, 4)]
            public int cascade_count;

            [Range(0.0f, 1.0f)]
            public float cascade_ratio_1, cascade_ratio_2, cascade_ratio_3;

            public readonly Vector3 CascadeRatios => new(cascade_ratio_1, cascade_ratio_2, cascade_ratio_3);
        }

        public Directional directional = new Directional
        {
            atlas_size = MapSize._2048,
            cascade_count = 4,
            cascade_ratio_1 = 0.1f,
            cascade_ratio_2 = 0.25f,
            cascade_ratio_3 = 0.5f
        };
    }
}