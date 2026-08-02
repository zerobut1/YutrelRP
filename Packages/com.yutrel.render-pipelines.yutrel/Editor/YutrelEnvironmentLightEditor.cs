using UnityEditor;
using UnityEngine;

namespace YutrelRP.Editor
{
    [CustomEditor(typeof(YutrelEnvironmentLight))]
    [CanEditMultipleObjects]
    internal sealed class YutrelEnvironmentLightEditor : UnityEditor.Editor
    {
        private SerializedProperty ibl_asset;
        private SerializedProperty intensity;
        private SerializedProperty diffuse_multiplier;
        private SerializedProperty specular_multiplier;
        private SerializedProperty render_skybox;
        private SerializedProperty skybox_multiplier;

        private void OnEnable()
        {
            ibl_asset = serializedObject.FindProperty("iblAsset");
            intensity = serializedObject.FindProperty("intensity");
            diffuse_multiplier = serializedObject.FindProperty("diffuseMultiplier");
            specular_multiplier = serializedObject.FindProperty("specularMultiplier");
            render_skybox = serializedObject.FindProperty("renderSkybox");
            skybox_multiplier = serializedObject.FindProperty("skyboxMultiplier");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.PropertyField(ibl_asset, new GUIContent("IBL Asset"));
            EditorGUILayout.PropertyField(intensity, new GUIContent("Environment Intensity"));
            EditorGUILayout.PropertyField(diffuse_multiplier);
            EditorGUILayout.PropertyField(specular_multiplier);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Skybox", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(render_skybox);
            if (render_skybox.boolValue)
            {
                EditorGUILayout.PropertyField(skybox_multiplier);
            }

            serializedObject.ApplyModifiedProperties();

            if (targets.Length != 1)
            {
                return;
            }

            var binding = (YutrelEnvironmentLight)target;
            var asset = binding.IblAsset;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Generated IBL", EditorStyles.boldLabel);
            DrawAssetStatus(asset);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Create IBL From HDRI"))
                {
                    YutrelCmgenIblGeneratorWindow.ShowForEnvironmentLight(binding, regenerate: false);
                }

                using (new EditorGUI.DisabledScope(asset == null || !asset.HasSkyboxData))
                {
                    if (GUILayout.Button("Regenerate IBL"))
                    {
                        YutrelCmgenIblGeneratorWindow.ShowForEnvironmentLight(binding, regenerate: true);
                    }
                }
            }

            using (new EditorGUI.DisabledScope(asset == null))
            {
                if (GUILayout.Button("Select IBL Asset"))
                {
                    Selection.activeObject = asset;
                    EditorGUIUtility.PingObject(asset);
                }
            }
        }

        private static void DrawAssetStatus(YutrelIBLAsset asset)
        {
            if (asset == null)
            {
                EditorGUILayout.HelpBox("Assign or generate an IBL asset.", MessageType.Warning);
                return;
            }

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.ObjectField("Source HDRI", asset.SourceEnvironmentTexture, typeof(Texture2D), false);
                EditorGUILayout.ObjectField("Specular Cubemap", asset.specularCubemap, typeof(Cubemap), false);
            }

            if (!asset.HasSkyboxData)
            {
                EditorGUILayout.HelpBox("The source HDRI required by Skybox is missing.", MessageType.Warning);
            }

            if (!asset.HasLightingData)
            {
                EditorGUILayout.HelpBox("The specular cubemap or diffuse SH data is incomplete.", MessageType.Error);
            }
        }
    }
}
