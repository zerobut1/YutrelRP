using UnityEngine;
using UnityEngine.Rendering;

namespace YutrelRP
{
    [CreateAssetMenu(menuName = "YutrelRP/YutrelRP Asset")]
    public class YutrelRPAsset : RenderPipelineAsset<YutrelRP>
    {
        [SerializeField] private YutrelRPSettings settings;

        protected override RenderPipeline CreatePipeline()
        {
#if UNITY_EDITOR
            YutrelRPGlobalSettings.Ensure();
#endif
            return new YutrelRP(settings);
        }
    }
}
