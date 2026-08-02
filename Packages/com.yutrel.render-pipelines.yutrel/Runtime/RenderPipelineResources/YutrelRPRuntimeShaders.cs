using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace YutrelRP
{
    [Serializable]
    [SupportedOnRenderPipeline(typeof(YutrelRPAsset))]
    [UnityEngine.Categorization.CategoryInfo(Name = "R: Runtime Shaders", Order = 1000)]
    public sealed class YutrelRPRuntimeShaders : IRenderPipelineResources
    {
        [SerializeField, HideInInspector] private int m_Version = 0;

        public int version => m_Version;

        bool IRenderPipelineGraphicsSettings.isAvailableInPlayerBuild => true;

        [SerializeField, ResourcePath("Shaders/DirectionalLightPass.shader")]
        private Shader directionalLightPass;

        [SerializeField, ResourcePath("Shaders/EnvironmentLightingPass.shader")]
        private Shader environmentLightingPass;

        [SerializeField, ResourcePath("Shaders/SkyboxEquirectangular.shader")]
        private Shader skyboxPass;

        [SerializeField, ResourcePath("Shaders/ShadowMaskPass.shader")]
        private Shader shadowMaskPass;

        [SerializeField, ResourcePath("Shaders/ToneMapping.shader")]
        private Shader toneMapping;

        [SerializeField, ResourcePath("Shaders/DebugViewPass.shader")]
        private Shader debugView;

        [SerializeField, ResourcePath("Shaders/SSAO.shader")]
        private Shader ssao;

        [SerializeField, ResourcePath("Shaders/HBAO.shader")]
        private Shader hbao;

        [SerializeField, ResourcePath("Shaders/GTAO.shader")]
        private Shader gtao;

        public Shader directional_light_pass => directionalLightPass;
        public Shader environment_lighting_pass => environmentLightingPass;
        public Shader skybox_pass => skyboxPass;
        public Shader shadow_mask_pass => shadowMaskPass;
        public Shader tone_mapping => toneMapping;
        public Shader debug_view => debugView;
        public Shader ssao_shader => ssao;
        public Shader hbao_shader => hbao;
        public Shader gtao_shader => gtao;
    }

    internal static class YutrelRPRuntimeShaderUtility
    {
        private static readonly HashSet<string> warned_missing_resources = new();

        public static bool TryCreateMaterial(Shader shader, string resource_name, ref Material material)
        {
            if (material != null)
            {
                return true;
            }

            if (shader == null)
            {
                WarnMissingResourceOnce(resource_name);
                return false;
            }

            material = CoreUtils.CreateEngineMaterial(shader);
            return material != null;
        }

        public static bool TryGetResources(out YutrelRPRuntimeShaders resources)
        {
            if (GraphicsSettings.TryGetRenderPipelineSettings(out resources) && resources != null)
            {
                return true;
            }

            WarnMissingResourceOnce(nameof(YutrelRPRuntimeShaders));
            return false;
        }

        public static void WarnMissingResourceOnce(string resource_name)
        {
            if (!warned_missing_resources.Add(resource_name))
            {
                return;
            }

            Debug.LogError($"YutrelRP runtime shader resource '{resource_name}' is missing.");
        }

        public static void ClearWarnings()
        {
            warned_missing_resources.Clear();
        }
    }
}
