using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace YutrelRP.Editor
{
    internal static class YutrelEnvironmentLightEditorUtility
    {
        private const string bindingObjectName = "Yutrel Environment Light";
        private const string legacyBindingObjectName = "Environment Light";
        private static readonly List<YutrelEnvironmentLight> binding_scratch = new();
        private static readonly List<GameObject> root_scratch = new();

        public static Scene GetDefaultTargetScene()
        {
            var active_scene = SceneManager.GetActiveScene();
            if (active_scene.IsValid() && active_scene.isLoaded)
            {
                return active_scene;
            }

            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (scene.IsValid() && scene.isLoaded)
                {
                    return scene;
                }
            }

            return default;
        }

        public static Scene GetSceneByHandle(ulong handle)
        {
            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (scene.handle.GetRawData() == handle)
                {
                    return scene;
                }
            }

            return default;
        }

        public static void GetLoadedScenes(List<Scene> scenes)
        {
            if (scenes == null)
            {
                throw new ArgumentNullException(nameof(scenes));
            }

            scenes.Clear();
            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (scene.IsValid() && scene.isLoaded)
                {
                    scenes.Add(scene);
                }
            }
        }

        public static string GetSceneDisplayName(Scene scene)
        {
            if (!scene.IsValid())
            {
                return "No Loaded Scene";
            }

            return string.IsNullOrWhiteSpace(scene.path)
                ? $"{scene.name} (Unsaved)"
                : $"{scene.name} - {scene.path}";
        }

        public static YutrelEnvironmentLight GetOrCreateBinding(Scene scene)
        {
            ValidateScene(scene);

            YutrelEnvironmentLight.InvalidateScene(scene);
            YutrelEnvironmentLight.GetEnvironmentLights(scene, binding_scratch, include_inactive: true);
            if (binding_scratch.Count > 0)
            {
                var existing = binding_scratch[0];
                binding_scratch.Clear();
                return existing;
            }

            binding_scratch.Clear();

            var game_object = FindNamedBindingObject(scene);
            if (game_object == null)
            {
                game_object = new GameObject(bindingObjectName);
                Undo.RegisterCreatedObjectUndo(game_object, "Create Yutrel Environment Light");
                SceneManager.MoveGameObjectToScene(game_object, scene);
            }
            else
            {
                Undo.RegisterFullObjectHierarchyUndo(game_object, "Repair Yutrel Environment Light Binding");
            }

            var binding = Undo.AddComponent<YutrelEnvironmentLight>(game_object);
            MarkBindingDirty(binding);
            Selection.activeObject = game_object;
            return binding;
        }

        public static YutrelEnvironmentLight AssignIblAsset(Scene scene, YutrelIBLAsset asset)
        {
            var binding = GetOrCreateBinding(scene);
            Undo.RecordObject(binding, "Assign Yutrel IBL Asset");
            binding.IblAsset = asset;
            MarkBindingDirty(binding);
            return binding;
        }

        public static void MarkBindingDirty(YutrelEnvironmentLight binding)
        {
            if (binding == null)
            {
                return;
            }

            EditorUtility.SetDirty(binding);
            YutrelEnvironmentLight.InvalidateScene(binding.gameObject.scene);
            if (binding.gameObject.scene.IsValid())
            {
                EditorSceneManager.MarkSceneDirty(binding.gameObject.scene);
            }
        }

        private static void ValidateScene(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded)
            {
                throw new InvalidOperationException("Select a loaded scene before editing EnvironmentLight data.");
            }
        }

        private static GameObject FindNamedBindingObject(Scene scene)
        {
            root_scratch.Clear();
            scene.GetRootGameObjects(root_scratch);
            foreach (var root in root_scratch)
            {
                var found = FindNamedBindingObjectInHierarchy(root.transform);
                if (found != null)
                {
                    root_scratch.Clear();
                    return found;
                }
            }

            root_scratch.Clear();
            return null;
        }

        private static GameObject FindNamedBindingObjectInHierarchy(Transform root)
        {
            if (root.name == bindingObjectName || root.name == legacyBindingObjectName)
            {
                return root.gameObject;
            }

            for (var i = 0; i < root.childCount; i++)
            {
                var found = FindNamedBindingObjectInHierarchy(root.GetChild(i));
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }
    }
}
