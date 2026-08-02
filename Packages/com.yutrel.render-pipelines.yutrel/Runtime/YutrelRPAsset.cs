using UnityEngine;
using UnityEngine.Rendering;

namespace YutrelRP
{
    [CreateAssetMenu(menuName = "Rendering/YutrelRP Asset", fileName = "YutrelRPAsset")]
    public sealed class YutrelRPAsset : RenderPipelineAsset<YutrelRP>
    {
        [SerializeField] private YutrelRPSettings settings = new();

        public YutrelRPSettings Settings => settings;

        public override string renderPipelineShaderTag => YutrelRP.ShaderTagName;

        protected override bool requiresCompatibleRenderPipelineGlobalSettings => true;

        protected override void EnsureGlobalSettings()
        {
            base.EnsureGlobalSettings();
#if UNITY_EDITOR
            YutrelRPGlobalSettings.Ensure();
#endif
        }

        protected override RenderPipeline CreatePipeline() => new YutrelRP(settings ??= new YutrelRPSettings());
    }
}
