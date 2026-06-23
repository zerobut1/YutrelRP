#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using NameAndTooltip = UnityEngine.Rendering.DebugUI.Widget.NameAndTooltip;

namespace YutrelRP
{
    internal sealed class YutrelRPDebugDisplaySettings : IDebugDisplaySettings
    {
        private readonly List<IDebugDisplaySettingsData> settings_data = new();
        private readonly YutrelRPDebugViewSettings debug_view_settings;

        internal YutrelRPDebugDisplaySettings(YutrelRPSettings settings)
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
        private readonly YutrelRPSettings settings;

        internal YutrelRPDebugViewSettings(YutrelRPSettings settings)
        {
            this.settings = settings;
        }

        public bool AreAnySettingsActive => settings != null &&
                                            settings.debugViewMode != YutrelRPSettings.DebugViewMode.Disabled;

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

            settings.debugViewMode = YutrelRPSettings.DebugViewMode.Disabled;
            if (settings.ddgiSettings != null)
            {
                settings.ddgiSettings.debugProbeRayDataSlice = 0;
                settings.ddgiSettings.debugProbeIrradianceAtlasSlice = 0;
                settings.ddgiSettings.debugProbeDistanceAtlasSlice = 0;
                settings.ddgiSettings.debugProbeDataSlice = 0;
            }
            RequestRepaint();
        }

        internal YutrelRPSettings.DebugViewMode DebugViewMode
        {
            get => settings != null ? settings.debugViewMode : YutrelRPSettings.DebugViewMode.Disabled;
            set
            {
                if (settings == null || settings.debugViewMode == value)
                {
                    return;
                }

                settings.debugViewMode = value;
                RequestRepaint();
            }
        }

        internal int DebugProbeRayDataSlice
        {
            get => settings?.ddgiSettings != null ? settings.ddgiSettings.debugProbeRayDataSlice : 0;
            set
            {
                if (settings?.ddgiSettings == null)
                {
                    return;
                }

                var clamped_value = Math.Max(0, value);
                if (settings.ddgiSettings.debugProbeRayDataSlice == clamped_value)
                {
                    return;
                }

                settings.ddgiSettings.debugProbeRayDataSlice = clamped_value;
                RequestRepaint();
            }
        }

        internal int DebugProbeIrradianceAtlasSlice
        {
            get => settings?.ddgiSettings != null ? settings.ddgiSettings.debugProbeIrradianceAtlasSlice : 0;
            set
            {
                if (settings?.ddgiSettings == null)
                {
                    return;
                }

                var clamped_value = Math.Max(0, value);
                if (settings.ddgiSettings.debugProbeIrradianceAtlasSlice == clamped_value)
                {
                    return;
                }

                settings.ddgiSettings.debugProbeIrradianceAtlasSlice = clamped_value;
                RequestRepaint();
            }
        }

        internal int DebugProbeDistanceAtlasSlice
        {
            get => settings?.ddgiSettings != null ? settings.ddgiSettings.debugProbeDistanceAtlasSlice : 0;
            set
            {
                if (settings?.ddgiSettings == null)
                {
                    return;
                }

                var clamped_value = Math.Max(0, value);
                if (settings.ddgiSettings.debugProbeDistanceAtlasSlice == clamped_value)
                {
                    return;
                }

                settings.ddgiSettings.debugProbeDistanceAtlasSlice = clamped_value;
                RequestRepaint();
            }
        }

        internal int DebugProbeDataSlice
        {
            get => settings?.ddgiSettings != null ? settings.ddgiSettings.debugProbeDataSlice : 0;
            set
            {
                if (settings?.ddgiSettings == null)
                {
                    return;
                }

                var clamped_value = Math.Max(0, value);
                if (settings.ddgiSettings.debugProbeDataSlice == clamped_value)
                {
                    return;
                }

                settings.ddgiSettings.debugProbeDataSlice = clamped_value;
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
        private const int other_category_value = -1;

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
                        action = () => data.DebugViewMode = YutrelRPSettings.DebugViewMode.Disabled
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
                        new ModeOption(YutrelRPSettings.DebugViewMode.GBufferBaseColor, "Base Color"),
                        new ModeOption(YutrelRPSettings.DebugViewMode.GBufferRoughness, "Roughness"),
                        new ModeOption(YutrelRPSettings.DebugViewMode.GBufferMetallic, "Metallic"),
                        new ModeOption(YutrelRPSettings.DebugViewMode.GBufferSpecular, "Specular"),
                        new ModeOption(YutrelRPSettings.DebugViewMode.GBufferWorldSpaceNormal, "World Space Normal"))
                }
            });

            AddWidget(new DebugUI.Foldout
            {
                displayName = "Scene & Lighting",
                opened = false,
                children =
                {
                    CreateModeGroupField(data, "Mode", "Select a scene or lighting debug view.",
                        new ModeOption(YutrelRPSettings.DebugViewMode.SceneDepth, "Scene Depth"),
                        new ModeOption(YutrelRPSettings.DebugViewMode.ShadowOnly, "Shadow Only"),
                        new ModeOption(YutrelRPSettings.DebugViewMode.CSMCascadeLevels, "CSM Cascade Levels"),
                        new ModeOption(YutrelRPSettings.DebugViewMode.AmbientOcclusion, "Ambient Occlusion"))
                }
            });

            AddWidget(new DebugUI.Foldout
            {
                displayName = "Ray Tracing",
                opened = false,
                children =
                {
                    CreateModeGroupField(data, "Smoke Test", "Select a ray tracing smoke test debug view.",
                        new ModeOption(YutrelRPSettings.DebugViewMode.RayTracingSmokeTestRayGen, "Ray Gen"),
                        new ModeOption(YutrelRPSettings.DebugViewMode.RayTracingSmokeTestRTASHitMiss, "RTAS Hit Miss"))
                }
            });

            AddWidget(new DebugUI.Foldout
            {
                displayName = "DDGI Texture & Trace",
                opened = true,
                children =
                {
                    CreateModeGroupField(data, "Mode", "Select a DDGI texture, trace, or surface debug view.",
                        new ModeOption(YutrelRPSettings.DebugViewMode.DDGIProbeRayData, "Probe Ray Data"),
                        new ModeOption(YutrelRPSettings.DebugViewMode.DDGIProbeIrradianceAtlas, "Probe Irradiance Atlas"),
                        new ModeOption(YutrelRPSettings.DebugViewMode.DDGIProbeDistanceAtlas, "Probe Distance Atlas"),
                        new ModeOption(YutrelRPSettings.DebugViewMode.DDGIProbeData, "Probe Data"),
                        new ModeOption(YutrelRPSettings.DebugViewMode.DDGITraceAlbedo, "Trace Albedo"),
                        new ModeOption(YutrelRPSettings.DebugViewMode.DDGIScreenTrace, "Screen Trace"),
                        new ModeOption(YutrelRPSettings.DebugViewMode.DDGIDiffuseOnly, "Diffuse Only"),
                        new ModeOption(YutrelRPSettings.DebugViewMode.DDGICoverage, "Coverage"),
                        new ModeOption(YutrelRPSettings.DebugViewMode.DDGIVisibilityCoverage, "Visibility Coverage")),
                    CreateIntField("Probe Ray Data Slice", "Y slice for DDGI probe ray data and trace albedo debug views.",
                        () => data.DebugProbeRayDataSlice,
                        value => data.DebugProbeRayDataSlice = value),
                    CreateIntField("Probe Irradiance Atlas Slice", "Texture array slice for DDGI irradiance atlas debug view.",
                        () => data.DebugProbeIrradianceAtlasSlice,
                        value => data.DebugProbeIrradianceAtlasSlice = value),
                    CreateIntField("Probe Distance Atlas Slice", "Texture array slice for DDGI distance atlas debug view.",
                        () => data.DebugProbeDistanceAtlasSlice,
                        value => data.DebugProbeDistanceAtlasSlice = value),
                    CreateIntField("Probe Data Slice", "Texture array slice for DDGI probe data debug view.",
                        () => data.DebugProbeDataSlice,
                        value => data.DebugProbeDataSlice = value)
                }
            });

            AddWidget(new DebugUI.Foldout
            {
                displayName = "DDGI Probe Scene",
                opened = false,
                children =
                {
                    CreateModeGroupField(data, "Mode", "Select a Scene View probe visualization.",
                        new ModeOption(YutrelRPSettings.DebugViewMode.DDGIProbeIrradianceScene, "Probe Irradiance"),
                        new ModeOption(YutrelRPSettings.DebugViewMode.DDGIProbeRayDataQualityScene, "Probe Ray Data Quality"),
                        new ModeOption(YutrelRPSettings.DebugViewMode.DDGIProbeDistanceScene, "Probe Distance"))
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
                autoEnum = typeof(YutrelRPSettings.DebugViewMode),
                getter = () => (int)data.DebugViewMode,
                setter = value => data.DebugViewMode = (YutrelRPSettings.DebugViewMode)value,
                getIndex = () => (int)data.DebugViewMode,
                setIndex = value => data.DebugViewMode = (YutrelRPSettings.DebugViewMode)value
            };
        }

        private static DebugUI.EnumField CreateModeGroupField(YutrelRPDebugViewSettings data, string displayName,
            string tooltip, params ModeOption[] options)
        {
            var enum_names = new GUIContent[options.Length + 2];
            var enum_values = new int[options.Length + 2];

            enum_names[0] = new GUIContent("Disabled");
            enum_values[0] = (int)YutrelRPSettings.DebugViewMode.Disabled;

            for (var i = 0; i < options.Length; ++i)
            {
                enum_names[i + 1] = new GUIContent(options[i].display_name);
                enum_values[i + 1] = (int)options[i].mode;
            }

            enum_names[enum_names.Length - 1] = new GUIContent("Other Category");
            enum_values[enum_values.Length - 1] = other_category_value;

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
                setter = value =>
                {
                    if (value != other_category_value)
                    {
                        data.DebugViewMode = (YutrelRPSettings.DebugViewMode)value;
                    }
                },
                getIndex = () => GetGroupIndex(data.DebugViewMode, options),
                setIndex = value =>
                {
                    if (value >= 0 && value < enum_values.Length && enum_values[value] != other_category_value)
                    {
                        data.DebugViewMode = (YutrelRPSettings.DebugViewMode)enum_values[value];
                    }
                }
            };
        }

        private static DebugUI.IntField CreateIntField(string displayName, string tooltip, Func<int> getter,
            Action<int> setter)
        {
            return new DebugUI.IntField
            {
                nameAndTooltip = new NameAndTooltip
                {
                    name = displayName,
                    tooltip = tooltip
                },
                getter = getter,
                setter = setter,
                min = () => 0,
                incStep = 1,
                incStepMult = 8
            };
        }

        private static int GetGroupValue(YutrelRPSettings.DebugViewMode mode, ModeOption[] options)
        {
            if (mode == YutrelRPSettings.DebugViewMode.Disabled)
            {
                return (int)YutrelRPSettings.DebugViewMode.Disabled;
            }

            for (var i = 0; i < options.Length; ++i)
            {
                if (options[i].mode == mode)
                {
                    return (int)mode;
                }
            }

            return other_category_value;
        }

        private static int GetGroupIndex(YutrelRPSettings.DebugViewMode mode, ModeOption[] options)
        {
            if (mode == YutrelRPSettings.DebugViewMode.Disabled)
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

            return options.Length + 1;
        }

        private static string GetModeLabel(YutrelRPSettings.DebugViewMode mode)
        {
            return ObjectNames.NicifyVariableName(mode.ToString());
        }

        private readonly struct ModeOption
        {
            internal readonly YutrelRPSettings.DebugViewMode mode;
            internal readonly string display_name;

            internal ModeOption(YutrelRPSettings.DebugViewMode mode, string displayName)
            {
                this.mode = mode;
                display_name = displayName;
            }
        }
    }
}
#endif
