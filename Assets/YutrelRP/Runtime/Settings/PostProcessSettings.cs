using System;
using UnityEngine;

namespace YutrelRP
{
    [CreateAssetMenu(menuName = "YutrelRP/Post Process Settings")]
    public class PostProcessSettings : ScriptableObject
    {
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

        [SerializeField] ToneMappingSettings m_tone_mapping;

        public ToneMappingSettings tone_mapping => m_tone_mapping;
    }
}