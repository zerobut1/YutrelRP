using System;
using UnityEngine.SceneManagement;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace YutrelRP
{
    internal sealed class YutrelRayTracingSceneManager : IDisposable
    {
        private bool scene_dirty = true;
        private bool disposed;

        public YutrelRayTracingSceneManager()
        {
            SceneManager.sceneLoaded += OnSceneChanged;
            SceneManager.sceneUnloaded += OnSceneChanged;
            SceneManager.activeSceneChanged += OnActiveSceneChanged;
#if UNITY_EDITOR
            EditorApplication.hierarchyChanged += MarkSceneDirty;
#endif
        }

        public bool SceneDirty => scene_dirty;

        public void MarkSceneDirty()
        {
            scene_dirty = true;
        }

        public void ClearSceneDirty()
        {
            scene_dirty = false;
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            SceneManager.sceneLoaded -= OnSceneChanged;
            SceneManager.sceneUnloaded -= OnSceneChanged;
            SceneManager.activeSceneChanged -= OnActiveSceneChanged;
#if UNITY_EDITOR
            EditorApplication.hierarchyChanged -= MarkSceneDirty;
#endif
        }

        private void OnSceneChanged(Scene scene, LoadSceneMode mode)
        {
            MarkSceneDirty();
        }

        private void OnSceneChanged(Scene scene)
        {
            MarkSceneDirty();
        }

        private void OnActiveSceneChanged(Scene previousScene, Scene nextScene)
        {
            MarkSceneDirty();
        }
    }
}
