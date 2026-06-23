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
        internal int debug_probe_ray_data_slice;
        internal int debug_probe_irradiance_atlas_slice;
        internal int debug_probe_distance_atlas_slice;
        internal int debug_probe_data_slice;

        internal void Reset()
        {
            debug_view_mode = DebugViewMode.Disabled;
            debug_probe_ray_data_slice = 0;
            debug_probe_irradiance_atlas_slice = 0;
            debug_probe_distance_atlas_slice = 0;
            debug_probe_data_slice = 0;
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
            [InspectorName("DDGI Texture/Probe Ray Data")]
            DDGIProbeRayData = 12,
            [InspectorName("DDGI Texture/Probe Irradiance Atlas")]
            DDGIProbeIrradianceAtlas = 13,
            [InspectorName("DDGI Texture/Probe Distance Atlas")]
            DDGIProbeDistanceAtlas = 14,
            [InspectorName("DDGI Texture/Probe Data")]
            DDGIProbeData = 15,
            [InspectorName("DDGI Surface/Diffuse Only")]
            DDGIDiffuseOnly = 16,
            [InspectorName("DDGI Surface/Coverage")]
            DDGICoverage = 17,
            [InspectorName("DDGI Surface/Visibility Coverage")]
            DDGIVisibilityCoverage = 18,
            [InspectorName("DDGI Probe Scene/Probe Irradiance")]
            DDGIProbeIrradianceScene = 19,
            [InspectorName("DDGI Probe Scene/Probe Ray Data Quality")]
            DDGIProbeRayDataQualityScene = 20,
            [InspectorName("DDGI Probe Scene/Probe Distance")]
            DDGIProbeDistanceScene = 21,
            [InspectorName("DDGI Texture/Trace Albedo")]
            DDGITraceAlbedo = 22,
            [InspectorName("DDGI Texture/Screen Trace")]
            DDGIScreenTrace = 23,
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

        internal int DebugProbeRayDataSlice
        {
            get => settings != null ? settings.debug_probe_ray_data_slice : 0;
            set
            {
                if (settings == null)
                {
                    return;
                }

                var clamped_value = Math.Max(0, value);
                if (settings.debug_probe_ray_data_slice == clamped_value)
                {
                    return;
                }

                settings.debug_probe_ray_data_slice = clamped_value;
                RequestRepaint();
            }
        }

        internal int DebugProbeIrradianceAtlasSlice
        {
            get => settings != null ? settings.debug_probe_irradiance_atlas_slice : 0;
            set
            {
                if (settings == null)
                {
                    return;
                }

                var clamped_value = Math.Max(0, value);
                if (settings.debug_probe_irradiance_atlas_slice == clamped_value)
                {
                    return;
                }

                settings.debug_probe_irradiance_atlas_slice = clamped_value;
                RequestRepaint();
            }
        }

        internal int DebugProbeDistanceAtlasSlice
        {
            get => settings != null ? settings.debug_probe_distance_atlas_slice : 0;
            set
            {
                if (settings == null)
                {
                    return;
                }

                var clamped_value = Math.Max(0, value);
                if (settings.debug_probe_distance_atlas_slice == clamped_value)
                {
                    return;
                }

                settings.debug_probe_distance_atlas_slice = clamped_value;
                RequestRepaint();
            }
        }

        internal int DebugProbeDataSlice
        {
            get => settings != null ? settings.debug_probe_data_slice : 0;
            set
            {
                if (settings == null)
                {
                    return;
                }

                var clamped_value = Math.Max(0, value);
                if (settings.debug_probe_data_slice == clamped_value)
                {
                    return;
                }

                settings.debug_probe_data_slice = clamped_value;
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

            AddWidget(new DebugUI.Foldout
            {
                displayName = "DDGI Texture & Trace",
                opened = true,
                children =
                {
                    CreateModeGroupField(data, "Mode", "Select a DDGI texture, trace, or surface debug view.",
                        new ModeOption(YutrelRPDebugSettings.DebugViewMode.DDGIProbeRayData, "Probe Ray Data"),
                        new ModeOption(YutrelRPDebugSettings.DebugViewMode.DDGIProbeIrradianceAtlas, "Probe Irradiance Atlas"),
                        new ModeOption(YutrelRPDebugSettings.DebugViewMode.DDGIProbeDistanceAtlas, "Probe Distance Atlas"),
                        new ModeOption(YutrelRPDebugSettings.DebugViewMode.DDGIProbeData, "Probe Data"),
                        new ModeOption(YutrelRPDebugSettings.DebugViewMode.DDGITraceAlbedo, "Trace Albedo"),
                        new ModeOption(YutrelRPDebugSettings.DebugViewMode.DDGIScreenTrace, "Screen Trace"),
                        new ModeOption(YutrelRPDebugSettings.DebugViewMode.DDGIDiffuseOnly, "Diffuse Only"),
                        new ModeOption(YutrelRPDebugSettings.DebugViewMode.DDGICoverage, "Coverage"),
                        new ModeOption(YutrelRPDebugSettings.DebugViewMode.DDGIVisibilityCoverage, "Visibility Coverage")),
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
                        new ModeOption(YutrelRPDebugSettings.DebugViewMode.DDGIProbeIrradianceScene, "Probe Irradiance"),
                        new ModeOption(YutrelRPDebugSettings.DebugViewMode.DDGIProbeRayDataQualityScene, "Probe Ray Data Quality"),
                        new ModeOption(YutrelRPDebugSettings.DebugViewMode.DDGIProbeDistanceScene, "Probe Distance"))
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
