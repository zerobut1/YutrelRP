using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;

namespace YutrelRP.Editor
{
    public class YutrelCmgenIblGeneratorWindow : EditorWindow
    {
        private const string defaultOutputDirectory = "Assets/YutrelRP/GeneratedIBL";
        private const string defaultOutputName = "Skybox";
        private const string skyboxTexturePropertyName = "_Tex";
        private const string mainTexturePropertyName = "_MainTex";
        private const int defaultCubemapSize = 256;
        private const int defaultSampleCount = 1024;
        private const int dfgLutSize = 128;

        [SerializeField] private Texture sourceTexture;
        private ulong targetSceneHandle;
        [SerializeField] private string outputDirectory = defaultOutputDirectory;
        [SerializeField] private string outputName = defaultOutputName;
        [SerializeField] private string cmgenExecutablePath;
        [SerializeField] private int cubemapSize = defaultCubemapSize;
        [SerializeField] private int sampleCount = defaultSampleCount;
        [SerializeField] private bool overwrite = true;
        [SerializeField] private bool bindGeneratedAssetToScene = true;
        [SerializeField] private bool outputDirectoryManuallyEdited;

        private Vector2 scroll;
        private string lastLog;
        private readonly List<Scene> loadedScenes = new();
        private readonly List<YutrelEnvironmentLight> sceneBindings = new();

        [MenuItem("Window/Rendering/Yutrel Environment Light")]
        public static void ShowEnvironmentLightWindow()
        {
            GetWindow<YutrelCmgenIblGeneratorWindow>("Yutrel Environment Light");
        }

        [MenuItem("Tools/YutrelRP/cmgen IBL Generator")]
        public static void ShowWindow()
        {
            ShowEnvironmentLightWindow();
        }

        [MenuItem("Assets/YutrelRP/Generate IBL with cmgen", true)]
        private static bool ValidateGenerateFromSelection()
        {
            return Selection.activeObject is Texture;
        }

        [MenuItem("Assets/YutrelRP/Generate IBL with cmgen")]
        private static void GenerateFromSelection()
        {
            var window = GetWindow<YutrelCmgenIblGeneratorWindow>("Yutrel Environment Light");
            window.sourceTexture = Selection.activeObject as Texture;
            window.outputDirectoryManuallyEdited = false;
            window.EnsureTargetScene();
            window.ApplySuggestedOutput(force: true);
            window.Show();
        }

        private void OnEnable()
        {
            EnsureTargetScene();

            cmgenExecutablePath = YutrelCmgenEditorSettings.CmgenExecutablePath;
            if (string.IsNullOrWhiteSpace(cmgenExecutablePath) &&
                File.Exists(YutrelCmgenEditorSettings.ProjectToolsCmgenExecutablePath))
            {
                cmgenExecutablePath = YutrelCmgenEditorSettings.ProjectToolsCmgenExecutablePath;
            }

            if (sourceTexture == null)
            {
                sourceTexture = GetDefaultSourceTexture();
            }

            ApplySuggestedOutput(force: outputDirectory == defaultOutputDirectory);
        }

        private void OnGUI()
        {
            EnsureTargetScene();
            scroll = EditorGUILayout.BeginScrollView(scroll);

            DrawSceneBindingSection();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Source", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            sourceTexture = (Texture)EditorGUILayout.ObjectField("Environment Texture", sourceTexture, typeof(Texture), false);
            if (EditorGUI.EndChangeCheck())
            {
                ApplySuggestedOutput(force: false);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Use Scene Skybox", GUILayout.Width(160)))
                {
                    sourceTexture = GetDefaultSourceTexture();
                    ApplySuggestedOutput(force: false);
                    if (sourceTexture == null)
                    {
                        EditorUtility.DisplayDialog("Default skybox missing",
                            "Could not find a texture on RenderSettings.skybox.", "OK");
                    }
                }
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Output", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUI.BeginChangeCheck();
                outputDirectory = EditorGUILayout.TextField("Output Directory", outputDirectory);
                if (EditorGUI.EndChangeCheck())
                {
                    outputDirectoryManuallyEdited = true;
                }

                if (GUILayout.Button("Browse", GUILayout.Width(72)))
                {
                    BrowseOutputDirectory();
                }
            }

            EditorGUILayout.LabelField("Main Name", outputName);

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Use Suggested Scene Path", GUILayout.Width(190)))
                {
                    outputDirectoryManuallyEdited = false;
                    ApplySuggestedOutput(force: true);
                }
            }

            overwrite = EditorGUILayout.Toggle("Overwrite Existing Generated Files", overwrite);
            bindGeneratedAssetToScene = EditorGUILayout.Toggle("Bind Generated Asset To Scene", bindGeneratedAssetToScene);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("cmgen", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUI.BeginChangeCheck();
                cmgenExecutablePath = EditorGUILayout.TextField("Executable Path", cmgenExecutablePath);
                if (EditorGUI.EndChangeCheck())
                {
                    YutrelCmgenEditorSettings.CmgenExecutablePath = cmgenExecutablePath;
                }

                if (GUILayout.Button("Browse", GUILayout.Width(72)))
                {
                    BrowseCmgenExecutable();
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Use tools/cmgen.exe", GUILayout.Width(160)))
                {
                    var projectToolPath = YutrelCmgenEditorSettings.ProjectToolsCmgenExecutablePath;
                    if (!File.Exists(projectToolPath))
                    {
                        EditorUtility.DisplayDialog("cmgen not found",
                            $"Could not find cmgen at {projectToolPath}.", "OK");
                    }
                    else
                    {
                        cmgenExecutablePath = projectToolPath;
                        YutrelCmgenEditorSettings.CmgenExecutablePath = cmgenExecutablePath;
                    }
                }
            }

            EditorGUILayout.HelpBox(
                "Configure this path per machine. cmgen can come from a Filament release tools package or a local Filament build.",
                MessageType.Info);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Generation", EditorStyles.boldLabel);
            cubemapSize = EditorGUILayout.IntPopup("Cubemap Size", cubemapSize,
                new[] { "64", "128", "256", "512", "1024" },
                new[] { 64, 128, 256, 512, 1024 });
            sampleCount = Mathf.Max(1, EditorGUILayout.IntField("Sample Count", sampleCount));
            EditorGUILayout.LabelField("DFG LUT Size", $"{dfgLutSize} (fixed project default)");
            EditorGUILayout.LabelField("Output Format", "Unity Cubemap + EXR DFG + parsed SH metadata");

            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(EditorApplication.isCompiling))
            {
                if (GUILayout.Button("Generate IBL Assets", GUILayout.Height(32)))
                {
                    Generate();
                }
            }

            if (!string.IsNullOrEmpty(lastLog))
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Last Result", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox(lastLog, MessageType.None);
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawSceneBindingSection()
        {
            EditorGUILayout.LabelField("EnvironmentLight Binding", EditorStyles.boldLabel);
            YutrelEnvironmentLightEditorUtility.GetLoadedScenes(loadedScenes);
            if (loadedScenes.Count == 0)
            {
                EditorGUILayout.HelpBox("Open a scene before editing EnvironmentLight data.", MessageType.Warning);
                return;
            }

            var target_scene = GetTargetScene();
            var scene_index = Mathf.Max(0,
                loadedScenes.FindIndex(scene => scene.handle.GetRawData() == target_scene.handle.GetRawData()));
            var scene_labels = loadedScenes.Select(YutrelEnvironmentLightEditorUtility.GetSceneDisplayName).ToArray();
            EditorGUI.BeginChangeCheck();
            scene_index = EditorGUILayout.Popup("Target Scene", scene_index, scene_labels);
            if (EditorGUI.EndChangeCheck())
            {
                targetSceneHandle = loadedScenes[scene_index].handle.GetRawData();
                ApplySuggestedOutput(force: false);
            }

            target_scene = GetTargetScene();
            YutrelEnvironmentLight.InvalidateScene(target_scene);
            YutrelEnvironmentLight.GetEnvironmentLights(target_scene, sceneBindings, include_inactive: true);
            if (sceneBindings.Count == 0)
            {
                EditorGUILayout.HelpBox("This scene has no YutrelEnvironmentLight binding.", MessageType.Info);
                if (GUILayout.Button("Create EnvironmentLight Binding"))
                {
                    YutrelEnvironmentLightEditorUtility.GetOrCreateBinding(target_scene);
                }

                return;
            }

            if (sceneBindings.Count > 1)
            {
                EditorGUILayout.HelpBox(
                    $"This scene has {sceneBindings.Count} EnvironmentLight bindings. The first one below is used deterministically.",
                    MessageType.Warning);
            }

            var binding = sceneBindings[0];
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.ObjectField("Binding Object", binding, typeof(YutrelEnvironmentLight), true);
                if (GUILayout.Button("Ping", GUILayout.Width(56)))
                {
                    EditorGUIUtility.PingObject(binding);
                }
            }

            EditorGUI.BeginChangeCheck();
            var ibl_asset = (YutrelIBLAsset)EditorGUILayout.ObjectField("IBL Asset", binding.IblAsset,
                typeof(YutrelIBLAsset), false);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(binding, "Assign Yutrel IBL Asset");
                binding.IblAsset = ibl_asset;
                YutrelEnvironmentLightEditorUtility.MarkBindingDirty(binding);
            }

            EditorGUI.BeginChangeCheck();
            var intensity = Mathf.Max(0.0f, EditorGUILayout.FloatField("IBL Intensity", binding.Intensity));
            var diffuse_multiplier =
                Mathf.Max(0.0f, EditorGUILayout.FloatField("Diffuse Multiplier", binding.DiffuseMultiplier));
            var specular_multiplier =
                Mathf.Max(0.0f, EditorGUILayout.FloatField("Specular Multiplier", binding.SpecularMultiplier));
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(binding, "Adjust Yutrel Environment Light Intensity");
                binding.Intensity = intensity;
                binding.DiffuseMultiplier = diffuse_multiplier;
                binding.SpecularMultiplier = specular_multiplier;
                YutrelEnvironmentLightEditorUtility.MarkBindingDirty(binding);
            }

            for (var i = 1; i < sceneBindings.Count; i++)
            {
                EditorGUILayout.ObjectField("Duplicate Binding", sceneBindings[i], typeof(YutrelEnvironmentLight), true);
            }
        }

        private void EnsureTargetScene()
        {
            var target_scene = YutrelEnvironmentLightEditorUtility.GetSceneByHandle(targetSceneHandle);
            if (target_scene.IsValid() && target_scene.isLoaded)
            {
                return;
            }

            target_scene = YutrelEnvironmentLightEditorUtility.GetDefaultTargetScene();
            targetSceneHandle = target_scene.IsValid() ? target_scene.handle.GetRawData() : 0UL;
        }

        private Scene GetTargetScene()
        {
            EnsureTargetScene();
            return YutrelEnvironmentLightEditorUtility.GetSceneByHandle(targetSceneHandle);
        }

        private void ApplySuggestedOutput(bool force)
        {
            if (force || !outputDirectoryManuallyEdited || string.IsNullOrWhiteSpace(outputDirectory) ||
                outputDirectory == defaultOutputDirectory)
            {
                outputDirectory = GetSuggestedOutputDirectory();
            }

            outputName = GetSuggestedOutputName();
        }

        private string GetSuggestedOutputDirectory()
        {
            var target_scene = GetTargetScene();
            var scene_directory = target_scene.IsValid() && !string.IsNullOrWhiteSpace(target_scene.path)
                ? NormalizeAssetPath(Path.GetDirectoryName(target_scene.path) ?? "Assets")
                : "Assets";
            var source_name = MakeSafeFileName(sourceTexture != null ? sourceTexture.name : "Environment");
            return $"{scene_directory}/EnvironmentLight/{source_name}";
        }

        private string GetSuggestedOutputName()
        {
            var target_scene = GetTargetScene();
            var scene_name = target_scene.IsValid() && !string.IsNullOrWhiteSpace(target_scene.name)
                ? target_scene.name
                : "Scene";
            var source_name = sourceTexture != null ? sourceTexture.name : "Environment";
            return MakeSafeFileName($"{scene_name}_{source_name}_IBL");
        }

        private static Texture GetDefaultSourceTexture()
        {
            var skybox = RenderSettings.skybox;
            if (skybox == null)
            {
                return null;
            }

            if (skybox.HasProperty(skyboxTexturePropertyName))
            {
                var texture = skybox.GetTexture(skyboxTexturePropertyName);
                if (texture != null)
                {
                    return texture;
                }
            }

            return skybox.HasProperty(mainTexturePropertyName)
                ? skybox.GetTexture(mainTexturePropertyName)
                : null;
        }

        private void BrowseOutputDirectory()
        {
            var startDirectory = AssetPathToFullPath(IsAssetPath(outputDirectory) ? outputDirectory : defaultOutputDirectory);
            var selected = EditorUtility.OpenFolderPanel("Select IBL output directory", startDirectory, "");
            if (string.IsNullOrEmpty(selected))
            {
                return;
            }

            var assetPath = FullPathToAssetPath(selected);
            if (!IsAssetPath(assetPath))
            {
                EditorUtility.DisplayDialog("Invalid output directory",
                    "Generated assets must be written under this Unity project's Assets directory.", "OK");
                return;
            }

            outputDirectory = assetPath;
            outputDirectoryManuallyEdited = true;
        }

        private void BrowseCmgenExecutable()
        {
            var startDirectory = !string.IsNullOrWhiteSpace(cmgenExecutablePath)
                ? Path.GetDirectoryName(cmgenExecutablePath)
                : Path.GetDirectoryName(YutrelCmgenEditorSettings.ProjectToolsCmgenExecutablePath);
            var selected = EditorUtility.OpenFilePanel("Select cmgen executable", startDirectory, "exe");
            if (string.IsNullOrEmpty(selected))
            {
                return;
            }

            cmgenExecutablePath = selected;
            YutrelCmgenEditorSettings.CmgenExecutablePath = cmgenExecutablePath;
        }

        private void Generate()
        {
            try
            {
                var request = new CmgenIblGenerationRequest
                {
                    SourceTexture = sourceTexture,
                    TargetScene = GetTargetScene(),
                    OutputDirectory = outputDirectory,
                    CmgenExecutablePath = cmgenExecutablePath,
                    CubemapSize = cubemapSize,
                    SampleCount = sampleCount,
                    DfgLutSize = dfgLutSize,
                    Overwrite = overwrite,
                    BindToScene = bindGeneratedAssetToScene
                };

                var result = YutrelCmgenIblGenerator.Generate(request);
                lastLog = result.Binding != null
                    ? $"Generated {result.AssetPath}\nBound to {YutrelEnvironmentLightEditorUtility.GetSceneDisplayName(result.BoundScene)}"
                    : $"Generated {result.AssetPath}";
                Selection.activeObject = result.Asset;
                EditorGUIUtility.PingObject(result.Asset);
                Debug.Log($"YutrelRP cmgen IBL generation completed: {result.AssetPath}");
                EditorUtility.DisplayDialog("cmgen IBL generation completed", result.AssetPath, "OK");
            }
            catch (Exception exception)
            {
                lastLog = exception.Message;
                Debug.LogError($"YutrelRP cmgen IBL generation failed:\n{exception}");
                EditorUtility.DisplayDialog("cmgen IBL generation failed", exception.Message, "OK");
            }
        }

        private static bool IsAssetPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            var normalized = NormalizeAssetPath(path);
            return normalized == "Assets" || normalized.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeAssetPath(string path)
        {
            return path.Replace("\\", "/").TrimEnd('/');
        }

        private static string ProjectRoot
        {
            get
            {
                var dataPath = Path.GetFullPath(Application.dataPath);
                var parent = Directory.GetParent(dataPath);
                if (parent == null)
                {
                    throw new InvalidOperationException("Could not resolve Unity project root.");
                }

                return parent.FullName;
            }
        }

        private static string AssetPathToFullPath(string assetPath)
        {
            return Path.GetFullPath(Path.Combine(ProjectRoot, NormalizeAssetPath(assetPath)));
        }

        private static string FullPathToAssetPath(string fullPath)
        {
            var projectRoot = Path.GetFullPath(ProjectRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var normalizedFullPath = Path.GetFullPath(fullPath);

            if (!normalizedFullPath.StartsWith(projectRoot, StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            var relative = normalizedFullPath.Substring(projectRoot.Length).TrimStart(Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);
            return NormalizeAssetPath(relative);
        }

        private static string MakeSafeFileName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return defaultOutputName;
            }

            foreach (var invalid in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(invalid, '_');
            }

            return string.IsNullOrWhiteSpace(name) ? defaultOutputName : name.Trim();
        }
    }

    internal static class YutrelCmgenEditorSettings
    {
        private const string cmgenExecutablePathKey = "YutrelRP.cmgenExecutablePath";

        public static string CmgenExecutablePath
        {
            get => EditorPrefs.GetString(cmgenExecutablePathKey, string.Empty);
            set => EditorPrefs.SetString(cmgenExecutablePathKey, value ?? string.Empty);
        }

        public static string ProjectToolsCmgenExecutablePath
        {
            get
            {
                var projectRoot = Directory.GetParent(Application.dataPath);
                if (projectRoot == null)
                {
                    return "tools/cmgen.exe";
                }

                return Path.GetFullPath(Path.Combine(projectRoot.FullName, "tools", "cmgen.exe"));
            }
        }

        [SettingsProvider]
        public static SettingsProvider CreateSettingsProvider()
        {
            return new SettingsProvider("Project/YutrelRP/cmgen", SettingsScope.Project)
            {
                label = "YutrelRP cmgen",
                guiHandler = _ =>
                {
                    EditorGUILayout.LabelField("cmgen Executable", EditorStyles.boldLabel);

                    EditorGUI.BeginChangeCheck();
                    var path = EditorGUILayout.TextField("Executable Path", CmgenExecutablePath);
                    if (EditorGUI.EndChangeCheck())
                    {
                        CmgenExecutablePath = path;
                    }

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.FlexibleSpace();
                        if (GUILayout.Button("Browse", GUILayout.Width(72)))
                        {
                            var startDirectory = !string.IsNullOrWhiteSpace(CmgenExecutablePath)
                                ? Path.GetDirectoryName(CmgenExecutablePath)
                                : Path.GetDirectoryName(ProjectToolsCmgenExecutablePath);
                            var selected = EditorUtility.OpenFilePanel("Select cmgen executable", startDirectory, "exe");
                            if (!string.IsNullOrEmpty(selected))
                            {
                                CmgenExecutablePath = selected;
                            }
                        }

                        if (GUILayout.Button("Use tools/cmgen.exe", GUILayout.Width(160)))
                        {
                            CmgenExecutablePath = ProjectToolsCmgenExecutablePath;
                        }
                    }

                    if (string.IsNullOrWhiteSpace(CmgenExecutablePath))
                    {
                        EditorGUILayout.HelpBox("Configure cmgen before generating IBL assets.", MessageType.Warning);
                    }
                    else if (!File.Exists(CmgenExecutablePath))
                    {
                        EditorGUILayout.HelpBox($"cmgen was not found at {CmgenExecutablePath}.", MessageType.Error);
                    }
                    else
                    {
                        EditorGUILayout.HelpBox("cmgen path is configured for this local editor.", MessageType.Info);
                    }
                },
                keywords = new HashSet<string>(new[] { "YutrelRP", "cmgen", "Filament", "IBL" })
            };
        }
    }

    internal struct CmgenIblGenerationRequest
    {
        public Texture SourceTexture;
        public Scene TargetScene;
        public string OutputDirectory;
        public string CmgenExecutablePath;
        public int CubemapSize;
        public int SampleCount;
        public int DfgLutSize;
        public bool Overwrite;
        public bool BindToScene;
    }

    internal readonly struct CmgenIblGenerationResult
    {
        public CmgenIblGenerationResult(YutrelIBLAsset asset, string assetPath, Scene boundScene,
            YutrelEnvironmentLight binding)
        {
            Asset = asset;
            AssetPath = assetPath;
            BoundScene = boundScene;
            Binding = binding;
        }

        public YutrelIBLAsset Asset { get; }
        public string AssetPath { get; }
        public Scene BoundScene { get; }
        public YutrelEnvironmentLight Binding { get; }
    }

    internal static class YutrelCmgenIblGenerator
    {
        private const string outputFormat = "exr";
        private const string dfgLutFileName = "dfg_lut.exr";
        private const string dfgMode = "FilamentMultiscatterCloth";
        private const string dfgFormat = "EXR_RGBAHalf";
        private const string shConvention = "FilamentPreScaledIrradianceSH3";
        private const string specularConvention = "FilamentCmgenPrefilteredCubemapNoMirror";
        private const string iblIntensityConvention =
            "Not baked; runtime YutrelEnvironmentLight IBL intensity scales contribution, with diffuse/specular multipliers as overrides.";
        private const string specularCubemapSuffix = "_SpecularCube.asset";
        private const string dfgLutSuffix = "_DFG.exr";
        private const string temporaryAssetRootPrefix = "__YutrelRP_CmgenTemp_";
        private static readonly string[] faceSuffixes = { "px", "nx", "py", "ny", "pz", "nz" };
        // cmgen face images have the opposite vertical row orientation from Unity Cubemap.SetPixels.
        private static readonly (string suffix, CubemapFace face, bool flipVertically)[] faceMappings =
        {
            ("px", CubemapFace.PositiveX, true),
            ("nx", CubemapFace.NegativeX, true),
            ("py", CubemapFace.PositiveY, true),
            ("ny", CubemapFace.NegativeY, true),
            ("pz", CubemapFace.PositiveZ, true),
            ("nz", CubemapFace.NegativeZ, true)
        };

        public static CmgenIblGenerationResult Generate(CmgenIblGenerationRequest request)
        {
            ValidateRequest(request, out var sourceAssetPath, out var sourceFullPath, out var cmgenFullPath,
                out var outputDirectory, out var outputName);

            var outputRootPath = outputDirectory;
            var finalMetadataPath = $"{outputRootPath}/{outputName}.asset";
            var finalSpecularCubemapPath = $"{outputRootPath}/{outputName}{specularCubemapSuffix}";
            var finalDfgLutPath = $"{outputRootPath}/{outputName}{dfgLutSuffix}";
            ValidateCanWriteGeneratedArtifacts(finalMetadataPath, finalSpecularCubemapPath, finalDfgLutPath,
                request.Overwrite);

            var tempRoot = Path.Combine(Path.GetTempPath(), "YutrelRP_cmgen_" + Guid.NewGuid().ToString("N"));
            var deployTempPath = Path.Combine(tempRoot, "deploy");
            var dfgTempPath = Path.Combine(deployTempPath, dfgLutFileName);
            var tempOutputRootPath = $"Assets/{temporaryAssetRootPrefix}{Guid.NewGuid():N}";
            var tempOutputRootFullPath = AssetPathToFullPath(tempOutputRootPath);
            var movedToFinal = false;

            try
            {
                Directory.CreateDirectory(deployTempPath);

                EditorUtility.DisplayProgressBar("cmgen IBL", "Generating specular environment and SH", 0.2f);
                RunCmgen(cmgenFullPath, new[]
                {
                    "--quiet",
                    "--no-mirror",
                    "--deploy=" + deployTempPath,
                    "--format=" + outputFormat,
                    "--size=" + request.CubemapSize.ToString(CultureInfo.InvariantCulture),
                    "--ibl-samples=" + request.SampleCount.ToString(CultureInfo.InvariantCulture),
                    sourceFullPath
                });

                EditorUtility.DisplayProgressBar("cmgen IBL", "Generating DFG LUT", 0.5f);
                RunCmgen(cmgenFullPath, new[]
                {
                    "--quiet",
                    "--size=" + request.DfgLutSize.ToString(CultureInfo.InvariantCulture),
                    "--ibl-dfg-multiscatter",
                    "--ibl-dfg-cloth",
                    "--ibl-dfg=" + dfgTempPath
                });

                var cmgenOutputName = Path.GetFileNameWithoutExtension(sourceAssetPath);
                var specularTempDirectory = Path.Combine(deployTempPath, cmgenOutputName);
                ValidateCmgenOutputs(specularTempDirectory, dfgTempPath);

                EditorUtility.DisplayProgressBar("cmgen IBL", "Importing generated assets", 0.7f);
                PrepareTemporaryOutputRoot(tempOutputRootPath, tempOutputRootFullPath);
                CopyDirectory(deployTempPath, tempOutputRootFullPath);

                var tempGeneratedDfgPath = $"{tempOutputRootPath}/{dfgLutFileName}";
                var tempDfgLutPath = $"{tempOutputRootPath}/{outputName}{dfgLutSuffix}";
                MoveAssetFile(tempGeneratedDfgPath, tempDfgLutPath);

                AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
                var specularAssetDirectory = $"{tempOutputRootPath}/{cmgenOutputName}";
                ConfigureGeneratedImporters(tempDfgLutPath, specularAssetDirectory, request.DfgLutSize);
                AssetDatabase.ImportAsset(tempOutputRootPath, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ImportRecursive);

                var tempSpecularCubemapPath = $"{tempOutputRootPath}/{outputName}{specularCubemapSuffix}";
                var diffuseShPath = $"{specularAssetDirectory}/sh.txt";

                EditorUtility.DisplayProgressBar("cmgen IBL", "Building Unity cubemap asset", 0.85f);
                var specularCubemap = CreateSpecularCubemapAsset(specularAssetDirectory, tempSpecularCubemapPath,
                    request.CubemapSize, out var specularMipCount);

                var diffuseShText = AssetDatabase.LoadAssetAtPath<TextAsset>(diffuseShPath);
                var dfgLut = AssetDatabase.LoadAssetAtPath<Texture2D>(tempDfgLutPath);

                if (diffuseShText == null)
                {
                    throw new InvalidOperationException($"Unity did not import generated SH text at {diffuseShPath}.");
                }

                if (dfgLut == null)
                {
                    throw new InvalidOperationException($"Unity did not import generated DFG LUT at {tempDfgLutPath}.");
                }

                var sh = ParseDiffuseSh(diffuseShText.text);
                var metadataPath = $"{tempOutputRootPath}/{outputName}.asset";
                var metadata = ScriptableObject.CreateInstance<YutrelIBLAsset>();
                metadata.sourceEnvironmentTexture = request.SourceTexture;
                metadata.sourceEnvironmentTexturePath = sourceAssetPath;
                metadata.outputRootPath = outputRootPath;
                metadata.specularDirectoryPath = string.Empty;
                metadata.specularCubemapPath = $"{outputRootPath}/{outputName}{specularCubemapSuffix}";
                metadata.diffuseShPath = string.Empty;
                metadata.dfgLutPath = $"{outputRootPath}/{outputName}{dfgLutSuffix}";
                metadata.specularCubemap = specularCubemap;
                metadata.specularFaceTextures = Array.Empty<Texture2D>();
                metadata.specularFacePaths = Array.Empty<string>();
                metadata.diffuseShText = null;
                metadata.diffuseIrradianceSh = sh;
                metadata.dfgLut = dfgLut;
                metadata.cubemapSize = request.CubemapSize;
                metadata.specularMipCount = specularMipCount;
                metadata.sampleCount = request.SampleCount;
                metadata.dfgLutSize = request.DfgLutSize;
                metadata.outputFormat = outputFormat;
                metadata.dfgMode = dfgMode;
                metadata.dfgFormat = dfgFormat;
                metadata.shConvention = shConvention;
                metadata.specularConvention = specularConvention;
                metadata.iblRoughnessOneLevel = specularMipCount - 1;
                metadata.iblIntensityConvention = iblIntensityConvention;
                metadata.cmgenExecutablePath = cmgenFullPath;
                metadata.generatedAtUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);

                AssetDatabase.CreateAsset(metadata, metadataPath);
                EditorUtility.SetDirty(metadata);
                AssetDatabase.SaveAssets();

                DeleteAssetOrPath(diffuseShPath);
                DeleteAssetOrPath(specularAssetDirectory);
                AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);

                ValidateRetainedOutput(tempOutputRootPath, metadataPath, tempSpecularCubemapPath, tempDfgLutPath);

                EditorUtility.DisplayProgressBar("cmgen IBL", "Replacing final output", 0.95f);
                EnsureAssetDirectory(outputRootPath);
                MoveValidatedGeneratedArtifacts(tempOutputRootPath, outputRootPath, outputName, request.Overwrite);
                movedToFinal = true;

                var finalMetadata = AssetDatabase.LoadAssetAtPath<YutrelIBLAsset>(finalMetadataPath);
                if (finalMetadata == null)
                {
                    throw new InvalidOperationException($"Unity did not move generated IBL metadata to {finalMetadataPath}.");
                }

                finalMetadata.outputRootPath = outputRootPath;
                finalMetadata.specularDirectoryPath = string.Empty;
                finalMetadata.specularCubemapPath = finalSpecularCubemapPath;
                finalMetadata.diffuseShPath = string.Empty;
                finalMetadata.dfgLutPath = finalDfgLutPath;
                finalMetadata.specularCubemap = AssetDatabase.LoadAssetAtPath<Cubemap>(finalSpecularCubemapPath);
                finalMetadata.dfgLut = AssetDatabase.LoadAssetAtPath<Texture2D>(finalDfgLutPath);
                finalMetadata.specularFaceTextures = Array.Empty<Texture2D>();
                finalMetadata.specularFacePaths = Array.Empty<string>();
                finalMetadata.diffuseShText = null;

                if (!finalMetadata.HasCompleteData)
                {
                    throw new InvalidOperationException("Generated IBL metadata is incomplete after moving to the final output.");
                }

                EditorUtility.SetDirty(finalMetadata);
                AssetDatabase.SaveAssets();

                YutrelEnvironmentLight binding = null;
                var boundScene = default(Scene);
                if (request.BindToScene)
                {
                    binding = YutrelEnvironmentLightEditorUtility.AssignIblAsset(request.TargetScene, finalMetadata);
                    boundScene = request.TargetScene;
                }

                return new CmgenIblGenerationResult(finalMetadata, finalMetadataPath, boundScene, binding);
            }
            finally
            {
                EditorUtility.ClearProgressBar();

                if (!movedToFinal)
                {
                    DeleteAssetOrPath(tempOutputRootPath);
                }

                if (Directory.Exists(tempRoot))
                {
                    Directory.Delete(tempRoot, true);
                }
            }
        }

        private static void ValidateRequest(
            CmgenIblGenerationRequest request,
            out string sourceAssetPath,
            out string sourceFullPath,
            out string cmgenFullPath,
            out string outputDirectory,
            out string outputName)
        {
            if (request.SourceTexture == null)
            {
                throw new InvalidOperationException("Select an equirectangular HDR environment texture.");
            }

            sourceAssetPath = AssetDatabase.GetAssetPath(request.SourceTexture);
            if (string.IsNullOrWhiteSpace(sourceAssetPath))
            {
                throw new InvalidOperationException("The selected environment texture is not a project asset.");
            }

            var extension = Path.GetExtension(sourceAssetPath).ToLowerInvariant();
            if (extension != ".exr" && extension != ".hdr" && extension != ".png" && extension != ".psd")
            {
                throw new InvalidOperationException(
                    $"cmgen does not support the selected texture extension '{extension}'. Use EXR, HDR, PNG, or PSD.");
            }

            sourceFullPath = AssetPathToFullPath(sourceAssetPath);
            if (!File.Exists(sourceFullPath))
            {
                throw new InvalidOperationException($"Could not find source texture file: {sourceAssetPath}");
            }

            if (string.IsNullOrWhiteSpace(request.CmgenExecutablePath))
            {
                throw new InvalidOperationException(
                    "cmgen executable path is not configured. Set it in Project Settings/YutrelRP/cmgen or this tool window.");
            }

            cmgenFullPath = Path.GetFullPath(request.CmgenExecutablePath);
            if (!File.Exists(cmgenFullPath))
            {
                throw new InvalidOperationException($"cmgen executable was not found: {cmgenFullPath}");
            }

            if (!IsPowerOfTwo(request.CubemapSize) || request.CubemapSize < 16)
            {
                throw new InvalidOperationException("Cubemap size must be a power of two and at least 16.");
            }

            if (request.SampleCount < 1)
            {
                throw new InvalidOperationException("Sample count must be greater than zero.");
            }

            if (!request.TargetScene.IsValid() || !request.TargetScene.isLoaded)
            {
                throw new InvalidOperationException("Select a loaded target scene before generating IBL data.");
            }

            outputDirectory = NormalizeAssetPath(request.OutputDirectory);
            if (!IsAssetPath(outputDirectory))
            {
                throw new InvalidOperationException("Output directory must be under Assets/.");
            }

            outputName = BuildMainOutputName(request.TargetScene, request.SourceTexture, sourceAssetPath);
        }

        private static void RunCmgen(string cmgenFullPath, IReadOnlyList<string> arguments)
        {
            var stdout = new StringBuilder();
            var stderr = new StringBuilder();

            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = cmgenFullPath,
                Arguments = string.Join(" ", arguments.Select(QuoteProcessArgument)),
                WorkingDirectory = ProjectRoot,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            process.OutputDataReceived += (_, args) =>
            {
                if (args.Data != null)
                {
                    stdout.AppendLine(args.Data);
                }
            };
            process.ErrorDataReceived += (_, args) =>
            {
                if (args.Data != null)
                {
                    stderr.AppendLine(args.Data);
                }
            };

            if (!process.Start())
            {
                throw new InvalidOperationException("Failed to start cmgen.");
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            process.WaitForExit();

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"cmgen failed with exit code {process.ExitCode}.\n{BuildProcessOutput(stdout, stderr)}");
            }
        }

        private static string BuildProcessOutput(StringBuilder stdout, StringBuilder stderr)
        {
            var output = new StringBuilder();
            if (stdout.Length > 0)
            {
                output.AppendLine("stdout:");
                output.AppendLine(stdout.ToString());
            }

            if (stderr.Length > 0)
            {
                output.AppendLine("stderr:");
                output.AppendLine(stderr.ToString());
            }

            return output.Length > 0 ? output.ToString() : "cmgen did not report additional output.";
        }

        private static string QuoteProcessArgument(string argument)
        {
            if (string.IsNullOrEmpty(argument))
            {
                return "\"\"";
            }

            if (!argument.Any(char.IsWhiteSpace) && !argument.Contains("\""))
            {
                return argument;
            }

            var builder = new StringBuilder();
            builder.Append('"');
            var backslashes = 0;

            foreach (var character in argument)
            {
                if (character == '\\')
                {
                    backslashes++;
                }
                else if (character == '"')
                {
                    builder.Append('\\', backslashes * 2 + 1);
                    builder.Append('"');
                    backslashes = 0;
                }
                else
                {
                    builder.Append('\\', backslashes);
                    builder.Append(character);
                    backslashes = 0;
                }
            }

            builder.Append('\\', backslashes * 2);
            builder.Append('"');
            return builder.ToString();
        }

        private static void ValidateCmgenOutputs(string specularDirectory, string dfgPath)
        {
            if (!Directory.Exists(specularDirectory))
            {
                throw new InvalidOperationException($"cmgen did not create expected output directory: {specularDirectory}");
            }

            var shPath = Path.Combine(specularDirectory, "sh.txt");
            if (!File.Exists(shPath))
            {
                throw new InvalidOperationException("cmgen did not create diffuse irradiance SH data.");
            }

            foreach (var faceSuffix in faceSuffixes)
            {
                var facePath = Path.Combine(specularDirectory, "m0_" + faceSuffix + "." + outputFormat);
                if (!File.Exists(facePath))
                {
                    throw new InvalidOperationException($"cmgen did not create expected specular face: {facePath}");
                }
            }

            if (!File.Exists(dfgPath))
            {
                throw new InvalidOperationException("cmgen did not create the DFG LUT.");
            }
        }

        private static void PrepareTemporaryOutputRoot(string outputRootPath, string outputRootFullPath)
        {
            if (Directory.Exists(outputRootFullPath))
            {
                Directory.Delete(outputRootFullPath, true);
            }

            var outputRootMetaPath = outputRootFullPath + ".meta";
            if (File.Exists(outputRootMetaPath))
            {
                File.Delete(outputRootMetaPath);
            }

            Directory.CreateDirectory(outputRootFullPath);
        }

        private static void MoveAssetFile(string sourceAssetPath, string destinationAssetPath)
        {
            var sourceFullPath = AssetPathToFullPath(sourceAssetPath);
            var destinationFullPath = AssetPathToFullPath(destinationAssetPath);
            if (!File.Exists(sourceFullPath))
            {
                throw new InvalidOperationException($"Generated file is missing: {sourceAssetPath}");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destinationFullPath) ?? ProjectRoot);
            if (File.Exists(destinationFullPath))
            {
                File.Delete(destinationFullPath);
            }

            File.Move(sourceFullPath, destinationFullPath);
        }

        private static void CopyDirectory(string sourceDirectory, string destinationDirectory)
        {
            foreach (var directory in Directory.GetDirectories(sourceDirectory, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(sourceDirectory, directory);
                Directory.CreateDirectory(Path.Combine(destinationDirectory, relative));
            }

            foreach (var file in Directory.GetFiles(sourceDirectory, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(sourceDirectory, file);
                var destination = Path.Combine(destinationDirectory, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(destination) ?? destinationDirectory);
                File.Copy(file, destination, true);
            }
        }

        private static void EnsureAssetDirectory(string assetPath)
        {
            var normalized = NormalizeAssetPath(assetPath);
            if (string.IsNullOrWhiteSpace(normalized) || normalized == ".")
            {
                normalized = "Assets";
            }

            Directory.CreateDirectory(AssetPathToFullPath(normalized));
            AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
        }

        private static void MoveValidatedGeneratedArtifacts(string tempOutputRootPath, string outputRootPath,
            string outputName, bool overwrite)
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);

            var artifacts = new[]
            {
                ($"{tempOutputRootPath}/{outputName}.asset", $"{outputRootPath}/{outputName}.asset"),
                ($"{tempOutputRootPath}/{outputName}{specularCubemapSuffix}",
                    $"{outputRootPath}/{outputName}{specularCubemapSuffix}"),
                ($"{tempOutputRootPath}/{outputName}{dfgLutSuffix}", $"{outputRootPath}/{outputName}{dfgLutSuffix}")
            };

            ValidateCanWriteGeneratedArtifacts(artifacts[0].Item2, artifacts[1].Item2, artifacts[2].Item2, overwrite);
            var backupRootPath = $"Assets/{temporaryAssetRootPrefix}ArtifactBackup_{Guid.NewGuid():N}";
            var backups = new List<(string originalPath, string backupPath)>();
            var movedArtifacts = new List<string>();

            try
            {
                if (overwrite)
                {
                    EnsureAssetDirectory(backupRootPath);
                    foreach (var artifact in artifacts)
                    {
                        if (!AssetExists(artifact.Item2))
                        {
                            continue;
                        }

                        var fullPath = AssetPathToFullPath(artifact.Item2);
                        if (Directory.Exists(fullPath) || AssetDatabase.IsValidFolder(artifact.Item2))
                        {
                            throw new InvalidOperationException(
                                $"Refusing to overwrite generated file target because it is a directory: {artifact.Item2}");
                        }

                        var backupPath = $"{backupRootPath}/{Path.GetFileName(artifact.Item2)}";
                        MoveAssetOrThrow(artifact.Item2, backupPath);
                        backups.Add((artifact.Item2, backupPath));
                    }
                }

                foreach (var artifact in artifacts)
                {
                    MoveAssetOrThrow(artifact.Item1, artifact.Item2);
                    movedArtifacts.Add(artifact.Item2);
                }
            }
            catch
            {
                foreach (var movedArtifact in movedArtifacts)
                {
                    DeleteGeneratedArtifactFile(movedArtifact);
                }

                foreach (var backup in backups)
                {
                    if (AssetExists(backup.backupPath))
                    {
                        MoveAssetOrThrow(backup.backupPath, backup.originalPath);
                    }
                }

                DeleteAssetOrPath(backupRootPath);
                throw;
            }

            DeleteAssetOrPath(backupRootPath);
            DeleteAssetOrPath(tempOutputRootPath);
            AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
        }

        private static void MoveAssetOrThrow(string sourceAssetPath, string destinationAssetPath)
        {
            var moveError = AssetDatabase.MoveAsset(sourceAssetPath, destinationAssetPath);
            if (!string.IsNullOrEmpty(moveError))
            {
                throw new InvalidOperationException(
                    $"Failed to move asset '{sourceAssetPath}' to '{destinationAssetPath}': {moveError}");
            }
        }

        private static void ValidateCanWriteGeneratedArtifacts(string metadataPath, string specularCubemapPath,
            string dfgLutPath, bool overwrite)
        {
            if (overwrite)
            {
                return;
            }

            foreach (var artifactPath in new[] { metadataPath, specularCubemapPath, dfgLutPath })
            {
                if (AssetExists(artifactPath))
                {
                    throw new InvalidOperationException(
                        $"Generated artifact already exists and overwrite is disabled: {artifactPath}");
                }
            }
        }

        private static bool AssetExists(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath) || !IsAssetPath(assetPath))
            {
                return false;
            }

            var fullPath = AssetPathToFullPath(assetPath);
            return File.Exists(fullPath) || Directory.Exists(fullPath) ||
                   File.Exists(fullPath + ".meta") || AssetDatabase.LoadMainAssetAtPath(assetPath) != null;
        }

        private static void DeleteGeneratedArtifactFile(string assetPath)
        {
            var fullPath = AssetPathToFullPath(assetPath);
            if (Directory.Exists(fullPath) || AssetDatabase.IsValidFolder(assetPath))
            {
                throw new InvalidOperationException(
                    $"Refusing to overwrite generated file target because it is a directory: {assetPath}");
            }

            if (AssetDatabase.DeleteAsset(assetPath))
            {
                return;
            }

            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
            }

            var metaPath = fullPath + ".meta";
            if (File.Exists(metaPath))
            {
                File.Delete(metaPath);
            }
        }

        private static void DeleteAssetOrPath(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath) || !IsAssetPath(assetPath))
            {
                return;
            }

            if (AssetDatabase.DeleteAsset(assetPath))
            {
                return;
            }

            var fullPath = AssetPathToFullPath(assetPath);
            if (Directory.Exists(fullPath))
            {
                Directory.Delete(fullPath, true);
            }
            else if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
            }

            var metaPath = fullPath + ".meta";
            if (File.Exists(metaPath))
            {
                File.Delete(metaPath);
            }
        }

        private static void ValidateRetainedOutput(string outputRootPath, string metadataPath,
            string specularCubemapPath, string dfgLutPath)
        {
            var metadata = AssetDatabase.LoadAssetAtPath<YutrelIBLAsset>(metadataPath);
            if (metadata == null || !metadata.HasCompleteData)
            {
                throw new InvalidOperationException("Generated IBL metadata is incomplete.");
            }

            if (AssetDatabase.LoadAssetAtPath<Cubemap>(specularCubemapPath) == null)
            {
                throw new InvalidOperationException($"Generated specular cubemap is missing: {specularCubemapPath}");
            }

            if (AssetDatabase.LoadAssetAtPath<Texture2D>(dfgLutPath) == null)
            {
                throw new InvalidOperationException($"Generated DFG LUT is missing: {dfgLutPath}");
            }

            var rootFullPath = AssetPathToFullPath(outputRootPath);
            var temporaryFiles = Directory.GetFiles(rootFullPath, "*", SearchOption.AllDirectories)
                .Where(path =>
                {
                    var fileName = Path.GetFileName(path);
                    return fileName.Equals("sh.txt", StringComparison.OrdinalIgnoreCase) ||
                           fileName.Equals("sh.txt.meta", StringComparison.OrdinalIgnoreCase) ||
                           Regex.IsMatch(fileName, @"^m\d+_(px|nx|py|ny|pz|nz)\." + outputFormat + @"(\.meta)?$",
                               RegexOptions.IgnoreCase);
                })
                .ToArray();

            if (temporaryFiles.Length > 0)
            {
                throw new InvalidOperationException("Generated output still contains temporary cmgen face or SH files.");
            }
        }

        private static void ConfigureGeneratedImporters(string dfgPath, string specularAssetDirectory, int dfgLutSize)
        {
            var dfgImporter = AssetImporter.GetAtPath(dfgPath) as TextureImporter;
            if (dfgImporter != null)
            {
                dfgImporter.textureType = TextureImporterType.Default;
                dfgImporter.textureShape = TextureImporterShape.Texture2D;
                dfgImporter.sRGBTexture = false;
                dfgImporter.mipmapEnabled = false;
                dfgImporter.isReadable = false;
                dfgImporter.filterMode = FilterMode.Bilinear;
                dfgImporter.wrapMode = TextureWrapMode.Clamp;
                dfgImporter.npotScale = TextureImporterNPOTScale.None;
                dfgImporter.maxTextureSize = dfgLutSize;
                dfgImporter.textureCompression = TextureImporterCompression.Uncompressed;
                ForcePlatformTextureFormat(dfgImporter, TextureImporterFormat.RGBAHalf);
                dfgImporter.SaveAndReimport();
            }

            foreach (var facePath in GetSpecularFacePaths(specularAssetDirectory))
            {
                var importer = AssetImporter.GetAtPath(facePath) as TextureImporter;
                if (importer == null)
                {
                    continue;
                }

                importer.textureType = TextureImporterType.Default;
                importer.textureShape = TextureImporterShape.Texture2D;
                importer.sRGBTexture = false;
                importer.isReadable = true;
                importer.mipmapEnabled = false;
                importer.filterMode = FilterMode.Bilinear;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.npotScale = TextureImporterNPOTScale.None;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                ForcePlatformTextureFormat(importer, TextureImporterFormat.RGBAHalf);
                importer.SaveAndReimport();
            }
        }

        private static void ForcePlatformTextureFormat(TextureImporter importer, TextureImporterFormat format)
        {
            SetPlatformTextureFormat(importer, importer.GetDefaultPlatformTextureSettings(), format);
            SetPlatformTextureFormat(importer, importer.GetPlatformTextureSettings("Standalone"), format);
        }

        private static void SetPlatformTextureFormat(TextureImporter importer,
            TextureImporterPlatformSettings settings, TextureImporterFormat format)
        {
            settings.overridden = true;
            settings.format = format;
            settings.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SetPlatformTextureSettings(settings);
        }

        private static Cubemap CreateSpecularCubemapAsset(
            string specularAssetDirectory,
            string cubemapPath,
            int cubemapSize,
            out int mipCount)
        {
            var mipFacePaths = GetSpecularMipFacePaths(specularAssetDirectory);
            if (mipFacePaths.Count == 0)
            {
                throw new InvalidOperationException("No cmgen specular mip faces were imported.");
            }

            var mipLevels = mipFacePaths.Keys.OrderBy(level => level).ToArray();
            for (var i = 0; i < mipLevels.Length; i++)
            {
                if (mipLevels[i] != i)
                {
                    throw new InvalidOperationException(
                        $"cmgen specular mip levels are not contiguous. Missing mip {i}.");
                }
            }

            mipCount = mipLevels.Length;
            var cubemap = new Cubemap(cubemapSize, TextureFormat.RGBAFloat, mipCount, true)
            {
                name = Path.GetFileNameWithoutExtension(cubemapPath),
                filterMode = FilterMode.Trilinear,
                wrapMode = TextureWrapMode.Clamp,
                anisoLevel = 0
            };

            foreach (var mipLevel in mipLevels)
            {
                var expectedSize = Mathf.Max(1, cubemapSize >> mipLevel);
                foreach (var face in faceMappings)
                {
                    if (!mipFacePaths[mipLevel].TryGetValue(face.suffix, out var facePath))
                    {
                        throw new InvalidOperationException(
                            $"cmgen specular output is missing mip {mipLevel} face {face.suffix}.");
                    }

                    var faceTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(facePath);
                    if (faceTexture == null)
                    {
                        throw new InvalidOperationException($"Unity did not import generated face texture at {facePath}.");
                    }

                    if (faceTexture.width != expectedSize || faceTexture.height != expectedSize)
                    {
                        throw new InvalidOperationException(
                            $"Generated face {facePath} is {faceTexture.width}x{faceTexture.height}, expected {expectedSize}x{expectedSize}.");
                    }

                    cubemap.SetPixels(GetPixelsForCubemapFace(faceTexture, face.flipVertically), face.face, mipLevel);
                }
            }

            cubemap.Apply(false, false);
            AssetDatabase.CreateAsset(cubemap, cubemapPath);
            AssetDatabase.ImportAsset(cubemapPath, ImportAssetOptions.ForceUpdate);
            var importedCubemap = AssetDatabase.LoadAssetAtPath<Cubemap>(cubemapPath);
            if (importedCubemap == null)
            {
                throw new InvalidOperationException($"Unity did not create generated cubemap asset at {cubemapPath}.");
            }

            return importedCubemap;
        }

        private static Color[] GetPixelsForCubemapFace(Texture2D faceTexture, bool flipVertically)
        {
            var pixels = faceTexture.GetPixels(0);
            if (!flipVertically)
            {
                return pixels;
            }

            var width = faceTexture.width;
            var height = faceTexture.height;
            var flipped = new Color[pixels.Length];
            for (var y = 0; y < height; y++)
            {
                Array.Copy(pixels, y * width, flipped, (height - 1 - y) * width, width);
            }

            return flipped;
        }

        private static IEnumerable<string> GetSpecularFacePaths(string rootPath)
        {
            var rootFullPath = AssetPathToFullPath(rootPath);
            if (!Directory.Exists(rootFullPath))
            {
                return Array.Empty<string>();
            }

            return Directory.GetFiles(rootFullPath, "*." + outputFormat, SearchOption.AllDirectories)
                .Select(FullPathToAssetPath)
                .Where(IsAssetPath)
                .OrderBy(path => path, StringComparer.Ordinal);
        }

        private static SortedDictionary<int, Dictionary<string, string>> GetSpecularMipFacePaths(
            string specularAssetDirectory)
        {
            var result = new SortedDictionary<int, Dictionary<string, string>>();
            var rootFullPath = AssetPathToFullPath(specularAssetDirectory);
            if (!Directory.Exists(rootFullPath))
            {
                return result;
            }

            var regex = new Regex(@"^m(\d+)_(px|nx|py|ny|pz|nz)\." + outputFormat + "$",
                RegexOptions.IgnoreCase);
            foreach (var file in Directory.GetFiles(rootFullPath, "*." + outputFormat, SearchOption.TopDirectoryOnly))
            {
                var match = regex.Match(Path.GetFileName(file));
                if (!match.Success)
                {
                    continue;
                }

                var mipLevel = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
                var suffix = match.Groups[2].Value.ToLowerInvariant();
                if (!result.TryGetValue(mipLevel, out var faces))
                {
                    faces = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    result.Add(mipLevel, faces);
                }

                faces[suffix] = FullPathToAssetPath(file);
            }

            return result;
        }

        private static Vector3[] ParseDiffuseSh(string text)
        {
            var matches = Regex.Matches(text,
                @"\(\s*([-+]?(?:\d+\.?\d*|\.\d+)(?:[eE][-+]?\d+)?)\s*,\s*([-+]?(?:\d+\.?\d*|\.\d+)(?:[eE][-+]?\d+)?)\s*,\s*([-+]?(?:\d+\.?\d*|\.\d+)(?:[eE][-+]?\d+)?)\s*\)");
            if (matches.Count < 9)
            {
                throw new InvalidOperationException("cmgen SH output did not contain 9 coefficient rows.");
            }

            var result = new Vector3[9];
            for (var i = 0; i < result.Length; i++)
            {
                var match = matches[i];
                result[i] = new Vector3(
                    float.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture),
                    float.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture),
                    float.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture));
            }

            return result;
        }

        private static string ProjectRoot
        {
            get
            {
                var dataPath = Path.GetFullPath(Application.dataPath);
                var parent = Directory.GetParent(dataPath);
                if (parent == null)
                {
                    throw new InvalidOperationException("Could not resolve Unity project root.");
                }

                return parent.FullName;
            }
        }

        private static string AssetPathToFullPath(string assetPath)
        {
            return Path.GetFullPath(Path.Combine(ProjectRoot, NormalizeAssetPath(assetPath)));
        }

        private static string FullPathToAssetPath(string fullPath)
        {
            var projectRoot = Path.GetFullPath(ProjectRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var normalizedFullPath = Path.GetFullPath(fullPath);

            if (!normalizedFullPath.StartsWith(projectRoot, StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            var relative = normalizedFullPath.Substring(projectRoot.Length).TrimStart(Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);
            return NormalizeAssetPath(relative);
        }

        private static bool IsAssetPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            var normalized = NormalizeAssetPath(path);
            return normalized == "Assets" || normalized.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeAssetPath(string path)
        {
            return path.Replace("\\", "/").TrimEnd('/');
        }

        private static string MakeSafeFileName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return "IBL";
            }

            foreach (var invalid in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(invalid, '_');
            }

            return string.IsNullOrWhiteSpace(name) ? "IBL" : name.Trim();
        }

        private static string BuildMainOutputName(Scene targetScene, Texture sourceTexture, string sourceAssetPath)
        {
            var sceneName = targetScene.IsValid() && !string.IsNullOrWhiteSpace(targetScene.name)
                ? targetScene.name
                : "Scene";
            var sourceName = sourceTexture != null && !string.IsNullOrWhiteSpace(sourceTexture.name)
                ? sourceTexture.name
                : Path.GetFileNameWithoutExtension(sourceAssetPath);

            return MakeSafeFileName($"{sceneName}_{sourceName}_IBL");
        }

        private static bool IsPowerOfTwo(int value)
        {
            return value > 0 && (value & (value - 1)) == 0;
        }
    }
}
