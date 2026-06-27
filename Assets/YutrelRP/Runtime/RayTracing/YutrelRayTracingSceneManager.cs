using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.SceneManagement;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace YutrelRP
{
    internal sealed class YutrelRayTracingSceneManager : IDisposable
    {
        private const string DDGIShaderPassName = "DDGIRayTracing";
        private static readonly ProfilingSampler ddgiBuildSampler = new("DDGI RTAS Build");

        private readonly List<RendererRecord> rendererRecords = new();
        private readonly List<ContributorRecord> contributorRecords = new();
        private RayTracingAccelerationStructure ddgiAccelerationStructure;
        private bool sceneScanDirty = true;
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

        public void PrepareDDGI(RenderGraph renderGraph, YutrelRPSettings.DDGISettings settings,
            RayTracingResources resources)
        {
            if (resources == null)
            {
                return;
            }

            if (disposed)
            {
                resources.SetDDGIDiagnostic("DDGI RTAS manager has been disposed.");
                return;
            }

            if (renderGraph == null)
            {
                resources.SetDDGIDiagnostic("DDGI RTAS build requires an active RenderGraph.");
                return;
            }

            if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Direct3D12)
            {
                resources.SetDDGIDiagnostic("DDGI RTAS requires Direct3D12.");
                return;
            }

            if (!SystemInfo.supportsRayTracing)
            {
                resources.SetDDGIDiagnostic("DDGI RTAS requires SystemInfo.supportsRayTracing.");
                return;
            }

            if (sceneScanDirty)
            {
                RebuildSceneSnapshot(settings);
            }
            else if (TryDetectCachedRendererChange())
            {
                RebuildAccelerationStructureFromSnapshot(settings);
            }

            if (ddgiAccelerationStructure == null || contributorRecords.Count == 0)
            {
                resources.SetDDGIDiagnostic("No enabled opaque MeshRenderer with RayTracingMode enabled and a DDGIRayTracing material pass was found.");
                return;
            }

            var handle = renderGraph.ImportRayTracingAccelerationStructure(ddgiAccelerationStructure, "DDGI RTAS");
            resources.SetDDGIAccelerationStructure(ddgiAccelerationStructure, handle, contributorRecords.Count);
            RecordDDGIBuildPass(renderGraph, resources);
        }

        public void ReleaseDDGI()
        {
            ddgiAccelerationStructure?.Dispose();
            ddgiAccelerationStructure = null;
            rendererRecords.Clear();
            contributorRecords.Clear();
            sceneScanDirty = true;
        }

        public void MarkSceneDirty()
        {
            sceneScanDirty = true;
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
            ReleaseDDGI();
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

        private void RebuildSceneSnapshot(YutrelRPSettings.DDGISettings settings)
        {
            rendererRecords.Clear();
            var renderers = UnityEngine.Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include);
            foreach (var renderer in renderers)
            {
                rendererRecords.Add(new RendererRecord(renderer, BuildFingerprint(renderer)));
            }

            sceneScanDirty = false;
            RebuildAccelerationStructureFromSnapshot(settings);
        }

        private bool TryDetectCachedRendererChange()
        {
            for (var index = 0; index < rendererRecords.Count; index++)
            {
                var record = rendererRecords[index];
                if (record.renderer == null)
                {
                    sceneScanDirty = true;
                    return true;
                }

                var fingerprint = BuildFingerprint(record.renderer);
                if (!fingerprint.Equals(record.fingerprint))
                {
                    rendererRecords[index] = new RendererRecord(record.renderer, fingerprint);
                    return true;
                }
            }

            return false;
        }

        private void RebuildAccelerationStructureFromSnapshot(YutrelRPSettings.DDGISettings settings)
        {
            if (sceneScanDirty)
            {
                RebuildSceneSnapshot(settings);
                return;
            }

            contributorRecords.Clear();
            if (ddgiAccelerationStructure == null)
            {
                ddgiAccelerationStructure = new RayTracingAccelerationStructure();
            }
            else
            {
                ddgiAccelerationStructure.ClearInstances();
            }

            var instanceCount = 0u;
            for (var index = 0; index < rendererRecords.Count; index++)
            {
                var renderer = rendererRecords[index].renderer;
                rendererRecords[index] = new RendererRecord(renderer, BuildFingerprint(renderer));
                if (!TryGetEligibleRenderer(renderer, out var reason))
                {
                    LogSkippedRenderer(settings, renderer, reason);
                    continue;
                }

                if (!TryBuildSubMeshFlags(renderer, settings, out var subMeshFlags))
                {
                    LogSkippedRenderer(settings, renderer, "no submesh has a material with DDGIRayTracing pass");
                    continue;
                }

                try
                {
                    var handle = ddgiAccelerationStructure.AddInstance(renderer, subMeshFlags: subMeshFlags,
                        enableTriangleCulling: false, frontTriangleCounterClockwise: false, mask: 0xFF,
                        id: instanceCount);
                    if (handle == 0)
                    {
                        LogSkippedRenderer(settings, renderer, "RTAS AddInstance returned an invalid handle");
                        continue;
                    }

                    contributorRecords.Add(new ContributorRecord(renderer, subMeshFlags, handle));
                    instanceCount++;
                }
                catch (Exception exception)
                {
                    Debug.LogWarning($"YutrelRP DDGI RTAS Manager AddInstance failed for '{renderer.name}': {exception.Message}");
                }
            }
        }

        private static void RecordDDGIBuildPass(RenderGraph renderGraph, RayTracingResources resources)
        {
            if (renderGraph == null ||
                resources == null ||
                !resources.has_ddgi_acceleration_structure ||
                !resources.ddgi_acceleration_structure_handle.IsValid())
            {
                resources?.SetDDGIDiagnostic("DDGI RTAS build pass was not recorded.");
                return;
            }

            using var builder = renderGraph.AddComputePass<DDGIBuildAccelerationStructurePass>(
                ddgiBuildSampler.name, out var pass, ddgiBuildSampler);
            pass.accelerationStructure = resources.ddgi_acceleration_structure;

            // Current compute builder API does not expose public RTAS read/write declarations.
            // This pass is recorded immediately before DDGI consumers and is kept non-cullable and ordered.
            builder.AllowPassCulling(false);
            builder.AllowGlobalStateModification(true);
            builder.SetRenderFunc<DDGIBuildAccelerationStructurePass>(
                static (pass, context) => pass.Render(context));
            resources.MarkDDGIAccelerationStructureBuildScheduled();
        }

        private sealed class DDGIBuildAccelerationStructurePass
        {
            public RayTracingAccelerationStructure accelerationStructure;

            public void Render(ComputeGraphContext context)
            {
                if (accelerationStructure != null)
                {
                    context.cmd.BuildRayTracingAccelerationStructure(accelerationStructure);
                }
            }
        }

        private static Material GetSubMeshMaterial(Renderer renderer, int subMeshIndex)
        {
            var materials = renderer.sharedMaterials;
            if (materials == null || materials.Length == 0)
            {
                return null;
            }

            var materialIndex = Mathf.Clamp(subMeshIndex, 0, materials.Length - 1);
            return materials[materialIndex];
        }

        private static bool TryGetSupportedTraceMaterial(Material material, out string reason)
        {
            reason = null;
            if (material == null)
            {
                reason = "material is missing";
                return false;
            }

            if (material.FindPass(DDGIShaderPassName) < 0)
            {
                var shaderName = material.shader != null ? material.shader.name : "<missing shader>";
                reason = $"material '{material.name}' shader '{shaderName}' has no '{DDGIShaderPassName}' ray tracing pass";
                return false;
            }

            return true;
        }

        private static bool TryBuildSubMeshFlags(MeshRenderer renderer, YutrelRPSettings.DDGISettings settings,
            out RayTracingSubMeshFlags[] subMeshFlags)
        {
            var subMeshCount = GetRendererSubMeshCount(renderer);
            subMeshFlags = new RayTracingSubMeshFlags[Mathf.Max(1, subMeshCount)];
            var supportedSubMeshCount = 0;

            for (var subMesh = 0; subMesh < subMeshFlags.Length; subMesh++)
            {
                var material = GetSubMeshMaterial(renderer, subMesh);
                if (TryGetSupportedTraceMaterial(material, out var reason))
                {
                    subMeshFlags[subMesh] = RayTracingSubMeshFlags.Enabled | RayTracingSubMeshFlags.ClosestHitOnly;
                    supportedSubMeshCount++;
                }
                else
                {
                    subMeshFlags[subMesh] = RayTracingSubMeshFlags.Disabled;
                    LogSkippedSubMesh(settings, renderer, subMesh, reason);
                }
            }

            return supportedSubMeshCount > 0;
        }

        private static bool TryGetEligibleRenderer(MeshRenderer renderer, out string reason)
        {
            reason = null;

            if (renderer == null)
            {
                reason = "renderer is missing";
                return false;
            }

            if (!renderer.enabled || !renderer.gameObject.activeInHierarchy)
            {
                reason = "renderer is disabled or inactive";
                return false;
            }

            if (!renderer.TryGetComponent<MeshFilter>(out var meshFilter) || meshFilter.sharedMesh == null)
            {
                reason = "MeshRenderer has no shared mesh";
                return false;
            }

            if (renderer.rayTracingMode == RayTracingMode.Off)
            {
                reason = "renderer RayTracingMode is Off";
                return false;
            }

            if (HasTransparentMaterial(renderer))
            {
                reason = "transparent materials are not part of first-stage DDGI capture";
                return false;
            }

            return true;
        }

        private static int GetRendererSubMeshCount(MeshRenderer renderer)
        {
            if (renderer == null ||
                !renderer.TryGetComponent<MeshFilter>(out var meshFilter) ||
                meshFilter.sharedMesh == null)
            {
                return 0;
            }

            return Mathf.Max(1, meshFilter.sharedMesh.subMeshCount);
        }

        private static bool HasTransparentMaterial(Renderer renderer)
        {
            var materials = renderer.sharedMaterials;
            if (materials == null)
            {
                return false;
            }

            foreach (var material in materials)
            {
                if (material != null && material.renderQueue >= (int)RenderQueue.Transparent)
                {
                    return true;
                }
            }

            return false;
        }

        private static void LogSkippedRenderer(YutrelRPSettings.DDGISettings settings, Renderer renderer, string reason)
        {
            if (settings == null || !settings.logDiagnostics)
            {
                return;
            }

            var rendererName = renderer != null ? renderer.name : "<missing renderer>";
            Debug.LogWarning($"YutrelRP DDGI RTAS Manager skipped renderer '{rendererName}': {reason}.");
        }

        private static void LogSkippedSubMesh(YutrelRPSettings.DDGISettings settings, Renderer renderer,
            int subMeshIndex, string reason)
        {
            if (settings == null || !settings.logDiagnostics)
            {
                return;
            }

            var rendererName = renderer != null ? renderer.name : "<missing renderer>";
            Debug.LogWarning($"YutrelRP DDGI RTAS Manager skipped submesh {subMeshIndex} on renderer '{rendererName}': {reason}.");
        }

        private static RendererFingerprint BuildFingerprint(MeshRenderer renderer)
        {
            if (renderer == null)
            {
                return default;
            }

            renderer.TryGetComponent<MeshFilter>(out var meshFilter);
            var mesh = meshFilter != null ? meshFilter.sharedMesh : null;
            var hash = GetStableObjectKey(renderer);
            hash = CombineHash(hash, renderer.enabled ? 1 : 0);
            hash = CombineHash(hash, renderer.gameObject.activeInHierarchy ? 1 : 0);
            hash = CombineHash(hash, (int)renderer.rayTracingMode);
            hash = CombineHash(hash, GetStableObjectKey(mesh));
            hash = CombineHash(hash, mesh != null ? mesh.subMeshCount : 0);

            var materials = renderer.sharedMaterials;
            var materialCount = materials != null ? materials.Length : 0;
            hash = CombineHash(hash, materialCount);
            for (var index = 0; index < materialCount; index++)
            {
                var material = materials[index];
                hash = CombineHash(hash, GetStableObjectKey(material));
                hash = CombineHash(hash, material != null ? material.renderQueue : 0);
                hash = CombineHash(hash, material != null ? GetStableObjectKey(material.shader) : 0);
                hash = CombineHash(hash, material != null ? material.FindPass(DDGIShaderPassName) : -1);
            }

            return new RendererFingerprint(hash);
        }

        private static int GetStableObjectKey(UnityEngine.Object target)
        {
            return target != null ? target.GetEntityId().GetHashCode() : 0;
        }

        private static int CombineHash(int hash, int value)
        {
            unchecked
            {
                return (hash * 397) ^ value;
            }
        }

        private readonly struct RendererRecord
        {
            public readonly MeshRenderer renderer;
            public readonly RendererFingerprint fingerprint;

            public RendererRecord(MeshRenderer renderer, RendererFingerprint fingerprint)
            {
                this.renderer = renderer;
                this.fingerprint = fingerprint;
            }
        }

        private readonly struct ContributorRecord
        {
            public readonly MeshRenderer renderer;
            public readonly RayTracingSubMeshFlags[] subMeshFlags;
            public readonly int instanceHandle;

            public ContributorRecord(MeshRenderer renderer, RayTracingSubMeshFlags[] subMeshFlags,
                int instanceHandle)
            {
                this.renderer = renderer;
                this.subMeshFlags = subMeshFlags;
                this.instanceHandle = instanceHandle;
            }
        }

        private readonly struct RendererFingerprint
        {
            private readonly int hash;

            public RendererFingerprint(int hash)
            {
                this.hash = hash;
            }

            public override bool Equals(object obj)
            {
                return obj is RendererFingerprint other && hash == other.hash;
            }

            public override int GetHashCode()
            {
                return hash;
            }
        }
    }
}
