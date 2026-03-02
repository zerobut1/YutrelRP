using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class GltfCreateEditablePrefab
{
    [MenuItem("Assets/glTF/Create Editable Prefab", true)]
    static bool ValidateCreateEditablePrefab()
    {
        return Selection.assetGUIDs.Any(guid =>
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            return IsGltfPath(path);
        });
    }

    [MenuItem("Assets/glTF/Create Editable Prefab")]
    static void CreateEditablePrefab()
    {
        var gltfPaths = Selection.assetGUIDs
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(IsGltfPath)
            .Distinct()
            .ToArray();

        foreach (var gltfPath in gltfPaths)
        {
            CreateForSingleGltf(gltfPath);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    static void CreateForSingleGltf(string gltfPath)
    {
        var mainObj = AssetDatabase.LoadMainAssetAtPath(gltfPath) as GameObject;
        if (mainObj == null)
        {
            Debug.LogWarning($"[glTF] Skip {gltfPath}: main asset is not a GameObject.");
            return;
        }

        var instance = Object.Instantiate(mainObj);
        instance.name = mainObj.name;

        try
        {
            var parentDir = Path.GetDirectoryName(gltfPath) ?? "Assets";
            var sourceName = Path.GetFileNameWithoutExtension(gltfPath);
            var outputDir = $"{parentDir}/{MakeSafeFileName(sourceName)}";
            EnsureFolder(outputDir);

            var meshMap = CopyMeshes(instance, outputDir, sourceName);
            RebindMeshes(instance, meshMap);

            var textureMap = CopyTextures(instance, outputDir, sourceName);
            var materialMap = CopyMaterials(instance, outputDir, sourceName, textureMap);
            RebindMaterials(instance, materialMap);

            var prefabName = MakeSafeFileName($"PF_{sourceName}");
            var prefabPath = AssetDatabase.GenerateUniqueAssetPath($"{outputDir}/{prefabName}.prefab");
            PrefabUtility.SaveAsPrefabAsset(instance, prefabPath);

            Debug.Log($"[glTF] Created editable prefab: {prefabPath}");
        }
        finally
        {
            Object.DestroyImmediate(instance);
        }
    }

    static Dictionary<Mesh, Mesh> CopyMeshes(GameObject root, string outputDir, string sourceName)
    {
        var map = new Dictionary<Mesh, Mesh>();

        var meshFilters = root.GetComponentsInChildren<MeshFilter>(true);
        foreach (var filter in meshFilters)
        {
            var source = filter.sharedMesh;
            if (source == null || map.ContainsKey(source))
            {
                continue;
            }

            var clone = Object.Instantiate(source);
            clone.name = source.name;
            var path = AssetDatabase.GenerateUniqueAssetPath(
                $"{outputDir}/{MakeSafeFileName(sourceName)}_Mesh_{MakeSafeFileName(source.name)}.asset");
            AssetDatabase.CreateAsset(clone, path);
            map[source] = clone;
        }

        var skinned = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        foreach (var smr in skinned)
        {
            var source = smr.sharedMesh;
            if (source == null || map.ContainsKey(source))
            {
                continue;
            }

            var clone = Object.Instantiate(source);
            clone.name = source.name;
            var path = AssetDatabase.GenerateUniqueAssetPath(
                $"{outputDir}/{MakeSafeFileName(sourceName)}_Mesh_{MakeSafeFileName(source.name)}.asset");
            AssetDatabase.CreateAsset(clone, path);
            map[source] = clone;
        }

        return map;
    }

    static void RebindMeshes(GameObject root, Dictionary<Mesh, Mesh> meshMap)
    {
        var meshFilters = root.GetComponentsInChildren<MeshFilter>(true);
        foreach (var filter in meshFilters)
        {
            if (filter.sharedMesh != null && meshMap.TryGetValue(filter.sharedMesh, out var clone))
            {
                filter.sharedMesh = clone;
            }
        }

        var skinned = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        foreach (var smr in skinned)
        {
            if (smr.sharedMesh != null && meshMap.TryGetValue(smr.sharedMesh, out var clone))
            {
                smr.sharedMesh = clone;
            }
        }
    }

    static Dictionary<Material, Material> CopyMaterials(
        GameObject root,
        string outputDir,
        string sourceName)
    {
        return CopyMaterials(root, outputDir, sourceName, null);
    }

    static Dictionary<Material, Material> CopyMaterials(
        GameObject root,
        string outputDir,
        string sourceName,
        Dictionary<Texture, Texture> textureMap)
    {
        var map = new Dictionary<Material, Material>();

        var renderers = root.GetComponentsInChildren<Renderer>(true);
        foreach (var renderer in renderers)
        {
            var mats = renderer.sharedMaterials;
            for (var i = 0; i < mats.Length; i++)
            {
                var source = mats[i];
                if (source == null || map.ContainsKey(source))
                {
                    continue;
                }

                var clone = Object.Instantiate(source);
                clone.name = source.name;

                if (textureMap != null)
                {
                    var textureProperties = clone.GetTexturePropertyNames();
                    foreach (var prop in textureProperties)
                    {
                        var tex = clone.GetTexture(prop);
                        if (tex != null && textureMap.TryGetValue(tex, out var copiedTex))
                        {
                            clone.SetTexture(prop, copiedTex);
                        }
                    }
                }

                var path = AssetDatabase.GenerateUniqueAssetPath(
                    $"{outputDir}/{MakeSafeFileName(sourceName)}_Mat_{MakeSafeFileName(source.name)}.mat");
                AssetDatabase.CreateAsset(clone, path);
                map[source] = clone;
            }
        }

        return map;
    }

    static Dictionary<Texture, Texture> CopyTextures(GameObject root, string outputDir, string sourceName)
    {
        var map = new Dictionary<Texture, Texture>();
        var renderers = root.GetComponentsInChildren<Renderer>(true);
        foreach (var renderer in renderers)
        {
            var mats = renderer.sharedMaterials;
            for (var i = 0; i < mats.Length; i++)
            {
                var mat = mats[i];
                if (mat == null)
                {
                    continue;
                }

                var textureProperties = mat.GetTexturePropertyNames();
                foreach (var prop in textureProperties)
                {
                    var source = mat.GetTexture(prop);
                    if (source == null || map.ContainsKey(source))
                    {
                        continue;
                    }

                    var copied = TryCopyTextureAsset(source, outputDir, sourceName);
                    if (copied != null)
                    {
                        map[source] = copied;
                    }
                }
            }
        }

        return map;
    }

    static Texture TryCopyTextureAsset(Texture source, string outputDir, string sourceName)
    {
        var sourcePath = AssetDatabase.GetAssetPath(source);
        if (!string.IsNullOrEmpty(sourcePath))
        {
            var main = AssetDatabase.LoadMainAssetAtPath(sourcePath);
            if (main == source)
            {
                var ext = Path.GetExtension(sourcePath);
                if (string.IsNullOrEmpty(ext))
                {
                    ext = ".asset";
                }

                var dstPath = AssetDatabase.GenerateUniqueAssetPath(
                    $"{outputDir}/{MakeSafeFileName(sourceName)}_Tex_{MakeSafeFileName(source.name)}{ext}");

                if (AssetDatabase.CopyAsset(sourcePath, dstPath))
                {
                    var copiedAsset = AssetDatabase.LoadAssetAtPath<Texture>(dstPath);
                    if (copiedAsset != null)
                    {
                        return copiedAsset;
                    }
                }
            }
        }

        var clone = Object.Instantiate(source);
        clone.name = source.name;
        var fallbackPath = AssetDatabase.GenerateUniqueAssetPath(
            $"{outputDir}/{MakeSafeFileName(sourceName)}_Tex_{MakeSafeFileName(source.name)}.asset");
        AssetDatabase.CreateAsset(clone, fallbackPath);
        return AssetDatabase.LoadAssetAtPath<Texture>(fallbackPath);
    }

    static void EnsureFolder(string folderPath)
    {
        if (AssetDatabase.IsValidFolder(folderPath))
        {
            return;
        }

        var parent = Path.GetDirectoryName(folderPath)?.Replace("\\", "/");
        var name = Path.GetFileName(folderPath);
        if (!string.IsNullOrEmpty(parent) && !string.IsNullOrEmpty(name) && AssetDatabase.IsValidFolder(parent))
        {
            AssetDatabase.CreateFolder(parent, name);
        }
    }

    static void RebindMaterials(GameObject root, Dictionary<Material, Material> materialMap)
    {
        var renderers = root.GetComponentsInChildren<Renderer>(true);
        foreach (var renderer in renderers)
        {
            var mats = renderer.sharedMaterials;
            var changed = false;

            for (var i = 0; i < mats.Length; i++)
            {
                var source = mats[i];
                if (source != null && materialMap.TryGetValue(source, out var clone))
                {
                    mats[i] = clone;
                    changed = true;
                }
            }

            if (changed)
            {
                renderer.sharedMaterials = mats;
            }
        }
    }

    static string MakeSafeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        foreach (var c in invalid)
        {
            name = name.Replace(c, '_');
        }
        return string.IsNullOrWhiteSpace(name) ? "Asset" : name;
    }

    static bool IsGltfPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var lower = path.ToLowerInvariant();
        return lower.EndsWith(".gltf") || lower.EndsWith(".glb");
    }
}
