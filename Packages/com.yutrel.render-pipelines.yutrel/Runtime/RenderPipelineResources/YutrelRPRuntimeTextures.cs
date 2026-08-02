using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace YutrelRP
{
    [Serializable]
    [SupportedOnRenderPipeline(typeof(YutrelRPAsset))]
    [UnityEngine.Categorization.CategoryInfo(Name = "R: Runtime Textures", Order = 1001)]
    public sealed class YutrelRPRuntimeTextures : IRenderPipelineResources
    {
        [SerializeField, HideInInspector] private int m_Version;

        public int version => m_Version;

        bool IRenderPipelineGraphicsSettings.isAvailableInPlayerBuild => true;

        [SerializeField, ResourcePath("Runtime/Textures/DFG_LUT.exr")]
        private Texture2D dfgLut;

        public Texture2D dfg_lut => dfgLut;
    }

    internal static class YutrelRPRuntimeTextureUtility
    {
        public static bool TryGetResources(out YutrelRPRuntimeTextures resources)
        {
            if (GraphicsSettings.TryGetRenderPipelineSettings(out resources) && resources != null)
            {
                return true;
            }

            YutrelRPRuntimeShaderUtility.WarnMissingResourceOnce(nameof(YutrelRPRuntimeTextures));
            return false;
        }
    }
}
