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
        }

        public Directional directional = new Directional
        {
            atlas_size = MapSize._2048,
        };
    }
}