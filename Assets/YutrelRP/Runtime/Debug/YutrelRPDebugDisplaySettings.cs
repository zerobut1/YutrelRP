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
        internal const float DefaultDDGIProbeDebugRadius = 0.05f;
        internal const float DefaultDDGIProbeDebugDistanceScale = 10.0f;

        internal DebugViewMode debug_view_mode = DebugViewMode.Disabled;
        internal bool ddgi_ray_data_debug_texture;
        internal DDGIProbeDebugMode ddgi_probe_debug_mode = DDGIProbeDebugMode.Disabled;
        internal float ddgi_probe_debug_radius = DefaultDDGIProbeDebugRadius;
        internal float ddgi_probe_debug_distance_scale = DefaultDDGIProbeDebugDistanceScale;

        internal void Reset()
        {
            debug_view_mode = DebugViewMode.Disabled;
            ddgi_ray_data_debug_texture = false;
            ddgi_probe_debug_mode = DDGIProbeDebugMode.Disabled;
            ddgi_probe_debug_radius = DefaultDDGIProbeDebugRadius;
            ddgi_probe_debug_distance_scale = DefaultDDGIProbeDebugDistanceScale;
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

        internal enum DDGIProbeDebugMode
        {
            [InspectorName("Disabled")]
            Disabled = 0,
            [InspectorName("Probe Irradiance")]
            ProbeIrradiance = 1,
            [InspectorName("Probe Distance")]
            ProbeDistance = 2,
            [InspectorName("Irradiance Atlas")]
            IrradianceAtlas = 3,
            [InspectorName("Distance Atlas")]
            DistanceAtlas = 4,
            [InspectorName("Ray Data Radiance")]
            RayDataRadiance = 5
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
                                            (settings.debug_view_mode != YutrelRPDebugSettings.DebugViewMode.Disabled ||
                                             settings.ddgi_ray_data_debug_texture ||
                                             settings.ddgi_probe_debug_mode !=
                                             YutrelRPDebugSettings.DDGIProbeDebugMode.Disabled);

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

        internal YutrelRPDebugSettings.DDGIProbeDebugMode DDGIProbeDebugMode
        {
            get => settings != null
                ? settings.ddgi_probe_debug_mode
                : YutrelRPDebugSettings.DDGIProbeDebugMode.Disabled;
            set
            {
                if (settings == null || settings.ddgi_probe_debug_mode == value)
                {
                    return;
                }

                settings.ddgi_probe_debug_mode = value;
                RequestRepaint();
            }
        }

        internal bool DDGIRayDataDebugTexture
        {
            get => settings != null && settings.ddgi_ray_data_debug_texture;
            set
            {
                if (settings == null || settings.ddgi_ray_data_debug_texture == value)
                {
                    return;
                }

                settings.ddgi_ray_data_debug_texture = value;
                RequestRepaint();
            }
        }

        internal float DDGIProbeDebugRadius
        {
            get => settings != null
                ? settings.ddgi_probe_debug_radius
                : YutrelRPDebugSettings.DefaultDDGIProbeDebugRadius;
            set
            {
                if (settings == null)
                {
                    return;
                }

                settings.ddgi_probe_debug_radius = Mathf.Max(0.001f, value);
                RequestRepaint();
            }
        }

        internal float DDGIProbeDebugDistanceScale
        {
            get => settings != null
                ? settings.ddgi_probe_debug_distance_scale
                : YutrelRPDebugSettings.DefaultDDGIProbeDebugDistanceScale;
            set
            {
                if (settings == null)
                {
                    return;
                }

                settings.ddgi_probe_debug_distance_scale = Mathf.Max(0.001f, value);
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
                displayName = "DDGI",
                opened = false,
                children =
                {
                    new DebugUI.Value
                    {
                        displayName = "Current",
                        getter = () => GetDDGIModeLabel(data.DDGIProbeDebugMode)
                    },
                    new DebugUI.BoolField
                    {
                        displayName = "Ray Data Debug Texture",
                        tooltip = "Unpack DDGI probe ray data into a debug Texture2DArray resource.",
                        getter = () => data.DDGIRayDataDebugTexture,
                        setter = value => data.DDGIRayDataDebugTexture = value
                    },
                    CreateDDGIModeField(data),
                    new DebugUI.FloatField
                    {
                        displayName = "Probe Radius",
                        tooltip = "World-space radius used to draw DDGI probe debug spheres.",
                        getter = () => data.DDGIProbeDebugRadius,
                        setter = value => data.DDGIProbeDebugRadius = value,
                        min = () => 0.001f
                    },
                    new DebugUI.FloatField
                    {
                        displayName = "Distance Scale",
                        tooltip = "World-space distance value mapped to white in DDGI distance debug views.",
                        getter = () => data.DDGIProbeDebugDistanceScale,
                        setter = value => data.DDGIProbeDebugDistanceScale = value,
                        min = () => 0.001f
                    },
                    new DebugUI.Button
                    {
                        displayName = "Disable DDGI Debug",
                        action = () => data.DDGIProbeDebugMode =
                            YutrelRPDebugSettings.DDGIProbeDebugMode.Disabled
                    }
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

        private static DebugUI.EnumField CreateDDGIModeField(YutrelRPDebugViewSettings data)
        {
            return new DebugUI.EnumField
            {
                nameAndTooltip = new NameAndTooltip
                {
                    name = "Mode",
                    tooltip = "Select a DDGI probe debug visualization."
                },
                autoEnum = typeof(YutrelRPDebugSettings.DDGIProbeDebugMode),
                getter = () => (int)data.DDGIProbeDebugMode,
                setter = value => data.DDGIProbeDebugMode =
                    (YutrelRPDebugSettings.DDGIProbeDebugMode)value,
                getIndex = () => (int)data.DDGIProbeDebugMode,
                setIndex = value => data.DDGIProbeDebugMode =
                    (YutrelRPDebugSettings.DDGIProbeDebugMode)value
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

        private static string GetDDGIModeLabel(YutrelRPDebugSettings.DDGIProbeDebugMode mode)
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
