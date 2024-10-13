using UnityEngine;
using UnityEngine.Rendering;

namespace YutrelRP
{
    [CreateAssetMenu(menuName = "Rendering/YutrelRP Asset")]
    public class YutrelRPAsset : RenderPipelineAsset<YutrelRP>
    {
        protected override RenderPipeline CreatePipeline()
        {
            QualitySettings.antiAliasing = 1;
            Screen.SetMSAASamples(1);
            return new YutrelRP(this);
        }
    }
}