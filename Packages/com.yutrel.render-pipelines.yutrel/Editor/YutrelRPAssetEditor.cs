using System.IO;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Rendering;

namespace YutrelRP.Editor
{
    internal static class YutrelRPAssetFactory
    {
        private const string PipelineMenu = "Assets/Create/Rendering/YutrelRP Asset (with Deferred Renderer)";
        private const string RendererMenu = "Assets/Create/Rendering/YutrelRP Deferred Renderer";

        [MenuItem(PipelineMenu, priority = 200)]
        private static void CreatePipelineAsset()
        {
            var directory = GetSelectedDirectory();
            var pipelinePath = AssetDatabase.GenerateUniqueAssetPath(
                $"{directory}/YutrelRP.asset");
            var baseName = Path.GetFileNameWithoutExtension(pipelinePath);
            var rendererPath = AssetDatabase.GenerateUniqueAssetPath(
                $"{directory}/{baseName}_DeferredRenderer.asset");

            var rendererData = ScriptableObject.CreateInstance<YutrelDeferredRendererData>();
            rendererData.name = $"{baseName} Deferred Renderer";
            AssetDatabase.CreateAsset(rendererData, rendererPath);

            var pipelineAsset = ScriptableObject.CreateInstance<YutrelRPAsset>();
            pipelineAsset.name = baseName;
            pipelineAsset.Initialize(true, rendererData);
            AssetDatabase.CreateAsset(pipelineAsset, pipelinePath);
            AssetDatabase.SaveAssets();

            Selection.activeObject = pipelineAsset;
            EditorGUIUtility.PingObject(pipelineAsset);
        }

        [MenuItem(RendererMenu, priority = 201)]
        private static void CreateRendererData()
        {
            var directory = GetSelectedDirectory();
            var path = AssetDatabase.GenerateUniqueAssetPath(
                $"{directory}/YutrelDeferredRenderer.asset");
            var rendererData = ScriptableObject.CreateInstance<YutrelDeferredRendererData>();
            AssetDatabase.CreateAsset(rendererData, path);
            AssetDatabase.SaveAssets();
            Selection.activeObject = rendererData;
            EditorGUIUtility.PingObject(rendererData);
        }

        private static string GetSelectedDirectory()
        {
            var selectedPath = AssetDatabase.GetAssetPath(Selection.activeObject);
            if (string.IsNullOrEmpty(selectedPath))
            {
                return "Assets";
            }

            if (!AssetDatabase.IsValidFolder(selectedPath))
            {
                selectedPath = Path.GetDirectoryName(selectedPath)?.Replace('\\', '/');
            }

            return string.IsNullOrEmpty(selectedPath) ? "Assets" : selectedPath;
        }
    }

    [InitializeOnLoad]
    internal static class YutrelRPAssetMigration
    {
        static YutrelRPAssetMigration()
        {
            EditorApplication.delayCall += UpgradeAllAssets;
        }

        public static void UpgradeAllAssets()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating ||
                AssetDatabase.IsAssetImportWorkerProcess())
            {
                EditorApplication.delayCall += UpgradeAllAssets;
                return;
            }

            var changed = false;
            foreach (var guid in AssetDatabase.FindAssets("t:YutrelRPAsset"))
            {
                var pipelinePath = AssetDatabase.GUIDToAssetPath(guid);
                var pipelineAsset = AssetDatabase.LoadAssetAtPath<YutrelRPAsset>(pipelinePath);
                if (pipelineAsset == null || !UpgradeAsset(pipelineAsset))
                {
                    continue;
                }
                changed = true;
            }

            if (changed)
            {
                AssetDatabase.SaveAssets();
            }
        }

        internal static bool UpgradeAsset(YutrelRPAsset pipelineAsset)
        {
            if (pipelineAsset == null || !pipelineAsset.NeedsMigration)
            {
                return false;
            }

            var pipelinePath = AssetDatabase.GetAssetPath(pipelineAsset);
            if (string.IsNullOrEmpty(pipelinePath))
            {
                return false;
            }

            var directory = Path.GetDirectoryName(pipelinePath)?.Replace('\\', '/') ?? "Assets";
            var rendererPath = AssetDatabase.GenerateUniqueAssetPath(
                $"{directory}/{pipelineAsset.name}_DeferredRenderer.asset");
            var rendererData = ScriptableObject.CreateInstance<YutrelDeferredRendererData>();
            rendererData.name = $"{pipelineAsset.name} Deferred Renderer";

            var settingsJson = JsonUtility.ToJson(pipelineAsset.GetLegacyRendererSettings());
            var settings = JsonUtility.FromJson<YutrelDeferredRendererSettings>(settingsJson) ??
                           new YutrelDeferredRendererSettings();
            rendererData.SetSettings(settings);

            AssetDatabase.CreateAsset(rendererData, rendererPath);
            pipelineAsset.ApplyMigration(rendererData);
            EditorUtility.SetDirty(pipelineAsset);

            Debug.Log(
                $"Migrated '{pipelinePath}' to the Yutrel Renderer architecture using '{rendererPath}'.",
                pipelineAsset);
            return true;
        }
    }

    [CustomEditor(typeof(YutrelRPAsset))]
    internal sealed class YutrelRPAssetEditor : UnityEditor.Editor
    {
        private SerializedProperty useSrpBatcher;
        private SerializedProperty rendererDataList;
        private SerializedProperty defaultRendererIndex;
        private ReorderableList rendererList;

        private void OnEnable()
        {
            useSrpBatcher = serializedObject.FindProperty("useSRPBatcher");
            rendererDataList = serializedObject.FindProperty("rendererDataList");
            defaultRendererIndex = serializedObject.FindProperty("defaultRendererIndex");

            rendererList = new ReorderableList(serializedObject, rendererDataList, true, true, true, true)
            {
                drawHeaderCallback = rect => EditorGUI.LabelField(rect, "Renderer Data List"),
                elementHeight = EditorGUIUtility.singleLineHeight + 4.0f,
                drawElementCallback = (rect, index, active, focused) =>
                {
                    rect.y += 2.0f;
                    rect.height = EditorGUIUtility.singleLineHeight;
                    EditorGUI.PropertyField(rect, rendererDataList.GetArrayElementAtIndex(index), GUIContent.none);
                }
            };
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.PropertyField(useSrpBatcher);
            EditorGUILayout.Space();
            rendererList.DoLayoutList();

            DrawDefaultRenderer();

            if (serializedObject.ApplyModifiedProperties())
            {
                ((YutrelRPAsset)target).DestroyRenderers();
                EditorUtility.SetDirty(target);
            }
        }

        private void DrawDefaultRenderer()
        {
            var count = rendererDataList.arraySize;
            if (count == 0)
            {
                EditorGUILayout.HelpBox("At least one Renderer Data asset is required.", MessageType.Error);
                defaultRendererIndex.intValue = 0;
                return;
            }

            var names = new GUIContent[count];
            for (var i = 0; i < count; ++i)
            {
                var rendererData = rendererDataList.GetArrayElementAtIndex(i).objectReferenceValue;
                names[i] = new GUIContent(rendererData != null ? rendererData.name : $"Missing Renderer ({i})");
            }

            defaultRendererIndex.intValue = Mathf.Clamp(defaultRendererIndex.intValue, 0, count - 1);
            defaultRendererIndex.intValue = EditorGUILayout.Popup(
                new GUIContent("Default Renderer"),
                defaultRendererIndex.intValue,
                names);
        }
    }

    [CustomEditor(typeof(YutrelAdditionalCameraData))]
    internal sealed class YutrelAdditionalCameraDataEditor : UnityEditor.Editor
    {
        private SerializedProperty rendererIndex;

        private void OnEnable()
        {
            rendererIndex = serializedObject.FindProperty("rendererIndex");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            if (GraphicsSettings.currentRenderPipeline is not YutrelRPAsset asset)
            {
                EditorGUILayout.HelpBox("The active Render Pipeline is not YutrelRP.", MessageType.Info);
                serializedObject.ApplyModifiedProperties();
                return;
            }

            var rendererData = asset.RendererDataList;
            var options = new GUIContent[rendererData.Count + 1];
            options[0] = new GUIContent("Default Renderer");
            for (var i = 0; i < rendererData.Count; ++i)
            {
                options[i + 1] = new GUIContent(
                    rendererData[i] != null ? $"{i}: {rendererData[i].name}" : $"{i}: Missing Renderer");
            }

            var popupIndex = rendererIndex.intValue < 0 ? 0 : rendererIndex.intValue + 1;
            popupIndex = Mathf.Clamp(popupIndex, 0, options.Length - 1);
            popupIndex = EditorGUILayout.Popup(new GUIContent("Renderer"), popupIndex, options);
            rendererIndex.intValue = popupIndex == 0
                ? YutrelAdditionalCameraData.DefaultRendererIndex
                : popupIndex - 1;

            serializedObject.ApplyModifiedProperties();
        }
    }
}
