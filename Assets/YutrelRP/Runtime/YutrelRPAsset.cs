using UnityEngine;
using UnityEngine.Rendering;

namespace YutrelRP
{
    [CreateAssetMenu(menuName = "Rendering/YutrelRP Asset")]
    public class YutrelRPAsset : RenderPipelineAsset<YutrelRP>
    {
        [SerializeField] private YutrelRPSettings m_settings;

        protected override RenderPipeline CreatePipeline()
        {
            return new YutrelRP(m_settings);
        }
    }
}