using System;
using UnityEngine;
using UnityEngine.SceneManagement;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace YutrelRP
{
    internal sealed class YutrelRayTracingWorld : IDisposable
    {
        private bool scene_dirty = true;
        private bool initialized;
        private bool disposed;
        private YutrelRayTracingAccelStruct scene_accel_struct;

        public YutrelRayTracingWorld()
        {
            SceneManager.sceneLoaded += OnSceneChanged;
            SceneManager.sceneUnloaded += OnSceneChanged;
            SceneManager.activeSceneChanged += OnActiveSceneChanged;
#if UNITY_EDITOR
            EditorApplication.hierarchyChanged += MarkSceneDirty;
#endif
        }

        public bool SceneDirty => scene_dirty;
        public YutrelRayTracingAccelStruct SceneAccelStruct => scene_accel_struct;

        public bool EnsureInitialized()
        {
            if (initialized)
            {
                return true;
            }

            if (!SystemInfo.supportsRayTracing)
            {
                return false;
            }

            scene_accel_struct = new YutrelRayTracingAccelStruct();
            initialized = true;
            MarkSceneDirty();
            return true;
        }

        public void MarkSceneDirty()
        {
            scene_dirty = true;
        }

        public void SyncSceneIfNeeded(uint mask)
        {
            if (!initialized || !scene_dirty)
            {
                return;
            }

            scene_accel_struct.Clear();
            var renderers = UnityEngine.Object.FindObjectsByType<MeshRenderer>();
            foreach (var renderer in renderers)
            {
                if (!IsTraceable(renderer))
                {
                    continue;
                }

                var filter = renderer.GetComponent<MeshFilter>();
                var mesh = filter != null ? filter.sharedMesh : null;
                if (mesh == null)
                {
                    continue;
                }

                scene_accel_struct.AddMesh(mesh, renderer.localToWorldMatrix, renderer.sharedMaterials, mask);
            }

            scene_dirty = false;
            scene_accel_struct.MarkBuildDirty();
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            scene_accel_struct?.Dispose();
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

        private static bool IsTraceable(MeshRenderer renderer)
        {
            return renderer != null && renderer.enabled && renderer.gameObject.activeInHierarchy &&
                   !renderer.forceRenderingOff && (int)renderer.rayTracingMode != 0;
        }
    }
}
