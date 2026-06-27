#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using NameAndTooltip = UnityEngine.Rendering.DebugUI.Widget.NameAndTooltip;

namespace YutrelRP
{
    internal sealed class YutrelRPDebugSettings
    {
        internal DebugViewMode debug_view_mode = DebugViewMode.Disabled;

        internal void Reset()
        {
            debug_view_mode = DebugViewMode.Disabled;
        }

        internal enum DebugViewMode
        {
            [InspectorName("None/Disabled")]
            Disabled = 0,
            [InspectorName("GBuffer/Base Color")]
            GBufferBaseColor = 1,
            [InspectorName("GBuffer/Roughness")]
            GBufferRoughness = 2,
            [InspectorName("GBuffer/Metallic")]
            GBufferMetallic = 3,
            [InspectorName("GBuffer/Specular")]
            GBufferSpecular = 4,
            [InspectorName("GBuffer/World Space Normal")]
            GBufferWorldSpaceNormal = 5,
            [InspectorName("Scene & Lighting/Scene Depth")]
            SceneDepth = 6,
            [InspectorName("Scene & Lighting/Shadow Only")]
            ShadowOnly = 7,
            [InspectorName("Scene & Lighting/CSM Cascade Levels")]
            CSMCascadeLevels = 8,
            [InspectorName("Scene & Lighting/Ambient Occlusion")]
            AmbientOcclusion = 9,
        }
    }

    internal sealed class YutrelRPDebugDisplaySettings : IDebugDisplaySettings
    {
        private readonly List<IDebugDisplaySettingsData> settings_data = new();
        private readonly YutrelRPDebugViewSettings debug_view_settings;

        internal YutrelRPDebugDisplaySettings(YutrelRPDebugSettings settings)
        {
            debug_view_settings = new YutrelRPDebugViewSettings(settings);
            settings_data.Add(debug_view_settings);
        }

        public bool AreAnySettingsActive => debug_view_settings.AreAnySettingsActive;

        public bool IsPostProcessingAllowed => true;

        public bool IsLightingActive => true;

        public bool TryGetScreenClearColor(ref Color color) => false;

        public void Reset()
        {
            foreach (var data in settings_data)
            {
                data.Reset();
            }
        }

        public void ForEach(Action<IDebugDisplaySettingsData> onExecute)
        {
            foreach (var data in settings_data)
            {
                onExecute(data);
            }
        }

        public IDebugDisplaySettingsData Add(IDebugDisplaySettingsData newData)
        {
            settings_data.Add(newData);
            return newData;
        }
    }

    internal sealed class YutrelRPDebugViewSettings : IDebugDisplaySettingsData
    {
        private readonly YutrelRPDebugSettings settings;

        internal YutrelRPDebugViewSettings(YutrelRPDebugSettings settings)
        {
            this.settings = settings;
        }

        public bool AreAnySettingsActive => settings != null &&
                                            settings.debug_view_mode != YutrelRPDebugSettings.DebugViewMode.Disabled;

        public bool IsPostProcessingAllowed => true;

        public bool IsLightingActive => true;

        public bool TryGetScreenClearColor(ref Color color) => false;

        public IDebugDisplaySettingsPanelDisposable CreatePanel()
        {
            return new YutrelRPDebugViewPanel(this);
        }

        public void Reset()
        {
            if (settings == null)
            {
                return;
            }

            settings.Reset();
            RequestRepaint();
        }

        internal YutrelRPDebugSettings.DebugViewMode DebugViewMode
        {
            get => settings != null ? settings.debug_view_mode : YutrelRPDebugSettings.DebugViewMode.Disabled;
            set
            {
                if (settings == null || settings.debug_view_mode == value)
                {
                    return;
                }

                settings.debug_view_mode = value;
                RequestRepaint();
            }
        }

        private static void RequestRepaint()
        {
            SceneView.RepaintAll();
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
        }
    }

    [DisplayInfo(name = "YutrelRP", order = 100)]
    internal sealed class YutrelRPDebugViewPanel : DebugDisplaySettingsPanel<YutrelRPDebugViewSettings>
    {
        public YutrelRPDebugViewPanel(YutrelRPDebugViewSettings data)
            : base(data)
        {
            AddWidget(new DebugUI.Foldout
            {
                displayName = "Debug View",
                opened = true,
                children =
                {
                    new DebugUI.Value
                    {
                        displayName = "Current",
                        getter = () => GetModeLabel(data.DebugViewMode)
                    },
                    CreateAllModesField(data),
                    new DebugUI.Button
                    {
                        displayName = "Disable Debug View",
                        action = () => data.DebugViewMode = YutrelRPDebugSettings.DebugViewMode.Disabled
                    }
                }
            });

            AddWidget(new DebugUI.Foldout
            {
                displayName = "GBuffer",
                opened = false,
                children =
                {
                    CreateModeGroupField(data, "Mode", "Select a GBuffer debug view.",
                        new ModeOption(YutrelRPDebugSettings.DebugViewMode.GBufferBaseColor, "Base Color"),
                        new ModeOption(YutrelRPDebugSettings.DebugViewMode.GBufferRoughness, "Roughness"),
                        new ModeOption(YutrelRPDebugSettings.DebugViewMode.GBufferMetallic, "Metallic"),
                        new ModeOption(YutrelRPDebugSettings.DebugViewMode.GBufferSpecular, "Specular"),
                        new ModeOption(YutrelRPDebugSettings.DebugViewMode.GBufferWorldSpaceNormal, "World Space Normal"))
                }
            });

            AddWidget(new DebugUI.Foldout
            {
                displayName = "Scene & Lighting",
                opened = false,
                children =
                {
                    CreateModeGroupField(data, "Mode", "Select a scene or lighting debug view.",
                        new ModeOption(YutrelRPDebugSettings.DebugViewMode.SceneDepth, "Scene Depth"),
                        new ModeOption(YutrelRPDebugSettings.DebugViewMode.ShadowOnly, "Shadow Only"),
                        new ModeOption(YutrelRPDebugSettings.DebugViewMode.CSMCascadeLevels, "CSM Cascade Levels"),
                        new ModeOption(YutrelRPDebugSettings.DebugViewMode.AmbientOcclusion, "Ambient Occlusion"))
                }
            });

        }

        private static DebugUI.EnumField CreateAllModesField(YutrelRPDebugViewSettings data)
        {
            return new DebugUI.EnumField
            {
                nameAndTooltip = new NameAndTooltip
                {
                    name = "All Modes",
                    tooltip = "Select any YutrelRP debug view."
                },
                autoEnum = typeof(YutrelRPDebugSettings.DebugViewMode),
                getter = () => (int)data.DebugViewMode,
                setter = value => data.DebugViewMode = (YutrelRPDebugSettings.DebugViewMode)value,
                getIndex = () => (int)data.DebugViewMode,
                setIndex = value => data.DebugViewMode = (YutrelRPDebugSettings.DebugViewMode)value
            };
        }

        private static DebugUI.EnumField CreateModeGroupField(YutrelRPDebugViewSettings data, string displayName,
            string tooltip, params ModeOption[] options)
        {
            var enum_names = new GUIContent[options.Length + 1];
            var enum_values = new int[options.Length + 1];

            enum_names[0] = new GUIContent("Disabled");
            enum_values[0] = (int)YutrelRPDebugSettings.DebugViewMode.Disabled;

            for (var i = 0; i < options.Length; ++i)
            {
                enum_names[i + 1] = new GUIContent(options[i].display_name);
                enum_values[i + 1] = (int)options[i].mode;
            }

            return new DebugUI.EnumField
            {
                nameAndTooltip = new NameAndTooltip
                {
                    name = displayName,
                    tooltip = tooltip
                },
                enumNames = enum_names,
                enumValues = enum_values,
                getter = () => GetGroupValue(data.DebugViewMode, options),
                setter = value => data.DebugViewMode = (YutrelRPDebugSettings.DebugViewMode)value,
                getIndex = () => GetGroupIndex(data.DebugViewMode, options),
                setIndex = value =>
                {
                    if (value >= 0 && value < enum_values.Length)
                    {
                        data.DebugViewMode = (YutrelRPDebugSettings.DebugViewMode)enum_values[value];
                    }
                }
            };
        }

        private static int GetGroupValue(YutrelRPDebugSettings.DebugViewMode mode, ModeOption[] options)
        {
            if (mode == YutrelRPDebugSettings.DebugViewMode.Disabled)
            {
                return (int)YutrelRPDebugSettings.DebugViewMode.Disabled;
            }

            for (var i = 0; i < options.Length; ++i)
            {
                if (options[i].mode == mode)
                {
                    return (int)mode;
                }
            }

            return (int)YutrelRPDebugSettings.DebugViewMode.Disabled;
        }

        private static int GetGroupIndex(YutrelRPDebugSettings.DebugViewMode mode, ModeOption[] options)
        {
            if (mode == YutrelRPDebugSettings.DebugViewMode.Disabled)
            {
                return 0;
            }

            for (var i = 0; i < options.Length; ++i)
            {
                if (options[i].mode == mode)
                {
                    return i + 1;
                }
            }

            return 0;
        }

        private static string GetModeLabel(YutrelRPDebugSettings.DebugViewMode mode)
        {
            return ObjectNames.NicifyVariableName(mode.ToString());
        }

        private readonly struct ModeOption
        {
            internal readonly YutrelRPDebugSettings.DebugViewMode mode;
            internal readonly string display_name;

            internal ModeOption(YutrelRPDebugSettings.DebugViewMode mode, string displayName)
            {
                this.mode = mode;
                display_name = displayName;
            }
        }
    }
}
#endif
