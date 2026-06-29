using System;
using System.Collections.Generic;
using System.ComponentModel;
using UnityEngine;
using UnityEngine.Rendering;
#if UNITY_EDITOR
using UnityEditor.Rendering;
#endif

namespace YutrelRP
{
    [Serializable]
    [SupportedOnRenderPipeline(typeof(YutrelRPAsset))]
    [DisplayName("YutrelRP")]
    public sealed class YutrelRPGlobalSettings : RenderPipelineGlobalSettings<YutrelRPGlobalSettings, YutrelRP>
    {
        [SerializeField] private RenderPipelineGraphicsSettingsContainer settings = new();

        protected override List<IRenderPipelineGraphicsSettings> settingsList => settings.settingsList;

#if UNITY_EDITOR
        private const string DefaultPath = "Assets/YutrelRP/Settings/YutrelRPGlobalSettings.asset";

        internal static YutrelRPGlobalSettings Ensure()
        {
            var instance = GraphicsSettings.GetSettingsForRenderPipeline<YutrelRP>() as YutrelRPGlobalSettings;
            RenderPipelineGlobalSettingsUtils.TryEnsure<YutrelRPGlobalSettings, YutrelRP>(
                ref instance,
                DefaultPath,
                true);
            return instance;
        }
#endif
    }
}
