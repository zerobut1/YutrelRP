using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Serialization;

namespace YutrelRP
{
    public sealed class YutrelRPAsset : RenderPipelineAsset<YutrelRP>
    {
        internal const int CurrentSerializedVersion = 1;

        [SerializeField] private int serializedVersion;
        [SerializeField] private bool useSRPBatcher = true;
        [SerializeField] private YutrelRendererData[] rendererDataList = Array.Empty<YutrelRendererData>();
        [SerializeField] private int defaultRendererIndex;

#pragma warning disable 618
        [SerializeField, HideInInspector, FormerlySerializedAs("settings")]
        private YutrelRPSettings legacySettings;
#pragma warning restore 618

        [NonSerialized] private YutrelRenderer[] renderers;
        [NonSerialized] private YutrelDeferredRendererData legacyRendererData;
        [NonSerialized] private readonly HashSet<int> warnedCameraIds = new();

        public bool UseSRPBatcher => NeedsMigration && legacySettings != null
            ? legacySettings.useSRPBatcher
            : useSRPBatcher;

        public IReadOnlyList<YutrelRendererData> RendererDataList => rendererDataList;
        public int DefaultRendererIndex => defaultRendererIndex;

        [Obsolete("Use the Settings property on YutrelDeferredRendererData.")]
        public YutrelDeferredRendererSettings Settings
        {
            get
            {
                var data = GetRendererData(defaultRendererIndex) as YutrelDeferredRendererData;
                return data != null ? data.Settings : GetLegacyRendererSettings();
            }
        }

        public override string renderPipelineShaderTag => YutrelRP.ShaderTagName;

        internal bool NeedsMigration => legacySettings != null &&
                                        (serializedVersion < CurrentSerializedVersion ||
                                         rendererDataList == null ||
                                         rendererDataList.Length == 0);

        protected override bool requiresCompatibleRenderPipelineGlobalSettings => true;

        protected override void EnsureGlobalSettings()
        {
            base.EnsureGlobalSettings();
#if UNITY_EDITOR
            YutrelRPGlobalSettings.Ensure();
#endif
        }

        protected override RenderPipeline CreatePipeline()
        {
            if (GetRendererData(defaultRendererIndex) == null)
            {
                Debug.LogError("YutrelRP Asset has no valid default Renderer Data.", this);
                return null;
            }

            return new YutrelRP(this);
        }

        public bool ValidateRendererData(int index)
        {
            if (index == YutrelAdditionalCameraData.DefaultRendererIndex)
            {
                index = defaultRendererIndex;
            }

            return GetRendererData(index) != null;
        }

        public YutrelRenderer GetRenderer(int index)
        {
            var requestedIndex = index;
            if (index == YutrelAdditionalCameraData.DefaultRendererIndex)
            {
                index = defaultRendererIndex;
            }

            var data = GetRendererData(index);
            if (data == null)
            {
                index = defaultRendererIndex;
                data = GetRendererData(index);
                if (data == null)
                {
                    return null;
                }

                if (requestedIndex != YutrelAdditionalCameraData.DefaultRendererIndex)
                {
                    Debug.LogWarning(
                        $"Renderer at index {requestedIndex} is invalid. Falling back to default Renderer {index}.",
                        this);
                }
            }

            EnsureRendererCache();
            if (data.IsInvalidated || renderers[index] == null)
            {
                renderers[index]?.Dispose();
                renderers[index] = data.InternalCreateRenderer();
            }

            return renderers[index];
        }

        internal YutrelRenderer GetRenderer(Camera camera)
        {
            var index = YutrelAdditionalCameraData.DefaultRendererIndex;
            if (camera.cameraType == CameraType.Game &&
                camera.TryGetComponent<YutrelAdditionalCameraData>(out var cameraData))
            {
                index = cameraData.RendererIndex;
            }

            if (index != YutrelAdditionalCameraData.DefaultRendererIndex && !ValidateRendererData(index))
            {
                var cameraId = camera.GetEntityId().GetHashCode();
                if (warnedCameraIds.Add(cameraId))
                {
                    Debug.LogWarning(
                        $"Camera '{camera.name}' references invalid Yutrel Renderer index {index}; using the default Renderer.",
                        camera);
                }

                index = YutrelAdditionalCameraData.DefaultRendererIndex;
            }

            return GetRenderer(index);
        }

        internal void DestroyRenderers()
        {
            if (renderers != null)
            {
                foreach (var renderer in renderers)
                {
                    renderer?.Dispose();
                }
            }

            renderers = null;
            warnedCameraIds.Clear();
        }

        internal YutrelDeferredRendererSettings GetLegacyRendererSettings()
        {
            if (legacySettings == null)
            {
                return new YutrelDeferredRendererSettings();
            }

            return new YutrelDeferredRendererSettings
            {
                shadowSettings = legacySettings.shadowSettings,
                ambientOcclusionSettings = legacySettings.ambientOcclusionSettings,
                ddgiSettings = legacySettings.ddgiSettings
            };
        }

        internal void ApplyMigration(YutrelDeferredRendererData rendererData)
        {
            DestroyRenderers();
            DestroyLegacyRendererData();
            useSRPBatcher = legacySettings == null || legacySettings.useSRPBatcher;
            rendererDataList = new YutrelRendererData[] { rendererData };
            defaultRendererIndex = 0;
            serializedVersion = CurrentSerializedVersion;
        }

        internal void Initialize(bool srpBatcher, YutrelRendererData defaultRenderer)
        {
            Initialize(
                srpBatcher,
                defaultRenderer == null ? Array.Empty<YutrelRendererData>() : new[] { defaultRenderer },
                0);
        }

        internal void Initialize(bool srpBatcher, YutrelRendererData[] rendererData, int defaultIndex)
        {
            DestroyRenderers();
            DestroyLegacyRendererData();
            useSRPBatcher = srpBatcher;
            rendererDataList = rendererData ?? Array.Empty<YutrelRendererData>();
            defaultRendererIndex = defaultIndex;
            serializedVersion = CurrentSerializedVersion;
        }

#pragma warning disable 618
        internal void SetLegacySettingsForMigration(YutrelRPSettings value)
        {
            DestroyRenderers();
            DestroyLegacyRendererData();
            legacySettings = value;
            rendererDataList = Array.Empty<YutrelRendererData>();
            defaultRendererIndex = 0;
            serializedVersion = 0;
        }
#pragma warning restore 618

        internal YutrelRendererData GetRendererData(int index)
        {
            if (rendererDataList != null && index >= 0 && index < rendererDataList.Length)
            {
                return rendererDataList[index];
            }

            if (NeedsMigration && index == 0 && legacySettings != null)
            {
                if (legacyRendererData == null)
                {
                    legacyRendererData = CreateInstance<YutrelDeferredRendererData>();
                    legacyRendererData.name = "Legacy Yutrel Deferred Renderer";
                    legacyRendererData.hideFlags = HideFlags.HideAndDontSave;
                    legacyRendererData.SetSettings(GetLegacyRendererSettings());
                }

                return legacyRendererData;
            }

            return null;
        }

        private void EnsureRendererCache()
        {
            var count = rendererDataList != null && rendererDataList.Length > 0
                ? rendererDataList.Length
                : 1;
            if (renderers != null && renderers.Length == count)
            {
                return;
            }

            DestroyRenderers();
            renderers = new YutrelRenderer[count];
        }

        private void DestroyLegacyRendererData()
        {
            if (legacyRendererData == null)
            {
                return;
            }

            CoreUtils.Destroy(legacyRendererData);
            legacyRendererData = null;
        }

        protected override void OnDisable()
        {
            DestroyRenderers();
            DestroyLegacyRendererData();
            base.OnDisable();
        }
    }
}
