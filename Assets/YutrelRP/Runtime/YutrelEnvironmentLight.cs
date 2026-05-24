using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Serialization;

namespace YutrelRP
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [AddComponentMenu("YutrelRP/Environment Light")]
    public sealed class YutrelEnvironmentLight : MonoBehaviour
    {
        private static readonly Dictionary<ulong, CacheEntry> scene_cache = new();
        private static readonly List<YutrelEnvironmentLight> binding_scratch = new();
        private static readonly List<GameObject> root_scratch = new();

        [SerializeField] private YutrelIBLAsset iblAsset;
        [Min(0.0f)] [SerializeField] private float intensity = 1.0f;

        [FormerlySerializedAs("diffuseIntensity")]
        [Min(0.0f)] [SerializeField] private float diffuseMultiplier = 1.0f;

        [FormerlySerializedAs("specularIntensity")]
        [Min(0.0f)] [SerializeField] private float specularMultiplier = 1.0f;

        public YutrelIBLAsset IblAsset
        {
            get => iblAsset;
            set
            {
                if (iblAsset == value)
                {
                    return;
                }

                iblAsset = value;
                InvalidateScene(gameObject.scene);
            }
        }

        public float Intensity
        {
            get => intensity;
            set => intensity = Mathf.Max(0.0f, value);
        }

        public float DiffuseMultiplier
        {
            get => diffuseMultiplier;
            set => diffuseMultiplier = Mathf.Max(0.0f, value);
        }

        public float SpecularMultiplier
        {
            get => specularMultiplier;
            set => specularMultiplier = Mathf.Max(0.0f, value);
        }

        public static bool TryResolve(Scene scene, out YutrelEnvironmentLight binding)
        {
            return TryResolve(scene, out binding, include_inactive: false);
        }

        public static bool TryResolve(Scene scene, out YutrelEnvironmentLight binding, bool include_inactive)
        {
            binding = null;
            if (!scene.IsValid() || !scene.isLoaded)
            {
                return false;
            }

            var scene_handle = scene.handle.GetRawData();
            if (!include_inactive && scene_cache.TryGetValue(scene_handle, out var cached) && cached.IsUsable)
            {
                binding = cached.Binding;
                return binding != null;
            }

            GetEnvironmentLights(scene, binding_scratch, include_inactive);
            binding = binding_scratch.Count > 0 ? binding_scratch[0] : null;

            if (binding_scratch.Count > 1)
            {
                Debug.LogWarning(
                    $"YutrelRP: Scene '{scene.name}' contains {binding_scratch.Count} YutrelEnvironmentLight bindings. " +
                    $"Using '{GetHierarchyPath(binding.transform)}' deterministically.");
            }

            if (!include_inactive)
            {
                scene_cache[scene_handle] = new CacheEntry(binding, binding_scratch.Count);
            }

            binding_scratch.Clear();
            return binding != null;
        }

        public static int GetEnvironmentLights(Scene scene, List<YutrelEnvironmentLight> results,
            bool include_inactive = false)
        {
            if (results == null)
            {
                throw new ArgumentNullException(nameof(results));
            }

            results.Clear();
            if (!scene.IsValid() || !scene.isLoaded)
            {
                return 0;
            }

            root_scratch.Clear();
            scene.GetRootGameObjects(root_scratch);
            foreach (var root in root_scratch)
            {
                root.GetComponentsInChildren(include_inactive, results);
            }

            root_scratch.Clear();
            if (results.Count == 0)
            {
                var scene_handle = scene.handle.GetRawData();
                var inactive_mode = include_inactive
                    ? FindObjectsInactive.Include
                    : FindObjectsInactive.Exclude;
                foreach (var binding in UnityEngine.Object.FindObjectsByType<YutrelEnvironmentLight>(
                             inactive_mode))
                {
                    if (binding != null && binding.gameObject.scene.handle.GetRawData() == scene_handle)
                    {
                        results.Add(binding);
                    }
                }
            }

            results.RemoveAll(static binding => binding == null);
            if (!include_inactive)
            {
                results.RemoveAll(static binding => !binding.isActiveAndEnabled);
            }

            results.Sort(static (left, right) =>
                string.CompareOrdinal(GetHierarchySortKey(left.transform), GetHierarchySortKey(right.transform)));

            return results.Count;
        }

        public static void InvalidateScene(Scene scene)
        {
            if (scene.IsValid())
            {
                scene_cache.Remove(scene.handle.GetRawData());
            }
        }

        private void OnEnable()
        {
            InvalidateScene(gameObject.scene);
        }

        private void OnDisable()
        {
            InvalidateScene(gameObject.scene);
        }

        private void OnDestroy()
        {
            InvalidateScene(gameObject.scene);
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            intensity = Mathf.Max(0.0f, intensity);
            diffuseMultiplier = Mathf.Max(0.0f, diffuseMultiplier);
            specularMultiplier = Mathf.Max(0.0f, specularMultiplier);
            InvalidateScene(gameObject.scene);
        }
#endif

        private static string GetHierarchyPath(Transform transform)
        {
            return string.Join("/", BuildHierarchySegments(transform, include_sibling_indices: false));
        }

        private static string GetHierarchySortKey(Transform transform)
        {
            return string.Join("/", BuildHierarchySegments(transform, include_sibling_indices: true));
        }

        private static IEnumerable<string> BuildHierarchySegments(Transform transform, bool include_sibling_indices)
        {
            var segments = new Stack<string>();
            for (var current = transform; current != null; current = current.parent)
            {
                segments.Push(include_sibling_indices
                    ? $"{current.GetSiblingIndex():D6}:{current.name}"
                    : current.name);
            }

            return segments;
        }

        private readonly struct CacheEntry
        {
            public CacheEntry(YutrelEnvironmentLight binding, int duplicate_count)
            {
                Binding = binding;
                DuplicateCount = duplicate_count;
            }

            public YutrelEnvironmentLight Binding { get; }
            public int DuplicateCount { get; }
            public bool IsUsable => DuplicateCount == 0 || Binding != null;
        }
    }
}
