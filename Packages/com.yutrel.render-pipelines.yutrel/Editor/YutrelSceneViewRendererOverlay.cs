using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Overlays;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;

namespace YutrelRP.Editor
{
    [InitializeOnLoad]
    internal static class YutrelSceneViewRendererSelection
    {
        private const string SessionKey = "YutrelRP.SceneViewRendererIndex";

        internal static event Action Changed;

        internal static int RendererIndex { get; private set; }

        static YutrelSceneViewRendererSelection()
        {
            RendererIndex = SessionState.GetInt(
                SessionKey,
                YutrelAdditionalCameraData.DefaultRendererIndex);
            RenderPipelineManager.activeRenderPipelineTypeChanged += ScheduleRefresh;
            EditorApplication.projectChanged += ScheduleRefresh;
            EditorApplication.delayCall += ApplyToCurrentPipeline;
        }

        internal static void Select(int index)
        {
            RendererIndex = index;
            SessionState.SetInt(SessionKey, index);
            ApplyToCurrentPipeline();
            Changed?.Invoke();
            EditorApplication.QueuePlayerLoopUpdate();
            SceneView.RepaintAll();
        }

        private static void ScheduleRefresh()
        {
            EditorApplication.delayCall += Refresh;
        }

        private static void Refresh()
        {
            ApplyToCurrentPipeline();
            Changed?.Invoke();
        }

        private static void ApplyToCurrentPipeline()
        {
            if (GraphicsSettings.currentRenderPipeline is YutrelRPAsset asset)
            {
                asset.SetSceneViewRenderer(RendererIndex);
                if (RendererIndex != asset.SceneViewRendererIndex)
                {
                    RendererIndex = asset.SceneViewRendererIndex;
                    SessionState.SetInt(SessionKey, RendererIndex);
                }
            }
        }
    }

    [Overlay(
        typeof(SceneView),
        "YutrelRP.SceneViewRenderer",
        "Yutrel Renderer",
        defaultDisplay = true)]
    internal sealed class YutrelSceneViewRendererOverlay : Overlay
    {
        private readonly List<int> rendererIndices = new();
        private DropdownField rendererDropdown;

        public override void OnCreated()
        {
            base.OnCreated();
            YutrelSceneViewRendererSelection.Changed += RefreshChoices;
        }

        public override void OnWillBeDestroyed()
        {
            YutrelSceneViewRendererSelection.Changed -= RefreshChoices;
            base.OnWillBeDestroyed();
        }

        public override VisualElement CreatePanelContent()
        {
            rendererDropdown = new DropdownField("Renderer");
            rendererDropdown.style.minWidth = 220.0f;
            rendererDropdown.RegisterValueChangedCallback(OnRendererChanged);
            RefreshChoices();
            return rendererDropdown;
        }

        private void RefreshChoices()
        {
            if (rendererDropdown == null)
            {
                return;
            }

            rendererIndices.Clear();
            var choices = new List<string>();
            if (GraphicsSettings.currentRenderPipeline is not YutrelRPAsset asset)
            {
                choices.Add("YutrelRP inactive");
                rendererIndices.Add(YutrelAdditionalCameraData.DefaultRendererIndex);
                rendererDropdown.choices = choices;
                rendererDropdown.SetValueWithoutNotify(choices[0]);
                rendererDropdown.SetEnabled(false);
                return;
            }

            var defaultData = asset.GetRendererData(asset.DefaultRendererIndex);
            choices.Add($"Default: {RendererName(defaultData, asset.DefaultRendererIndex)}");
            rendererIndices.Add(YutrelAdditionalCameraData.DefaultRendererIndex);
            for (var i = 0; i < asset.RendererDataList.Count; i++)
            {
                var data = asset.RendererDataList[i];
                if (data == null)
                {
                    continue;
                }
                choices.Add($"{i}: {data.name}");
                rendererIndices.Add(i);
            }

            rendererDropdown.choices = choices;
            rendererDropdown.SetEnabled(true);
            var selectedIndex = rendererIndices.IndexOf(
                YutrelSceneViewRendererSelection.RendererIndex);
            rendererDropdown.SetValueWithoutNotify(
                choices[selectedIndex >= 0 ? selectedIndex : 0]);
        }

        private void OnRendererChanged(ChangeEvent<string> change)
        {
            var choiceIndex = rendererDropdown.choices.IndexOf(change.newValue);
            if (choiceIndex < 0 || choiceIndex >= rendererIndices.Count)
            {
                return;
            }
            YutrelSceneViewRendererSelection.Select(rendererIndices[choiceIndex]);
        }

        private static string RendererName(YutrelRendererData data, int index)
        {
            return data != null ? data.name : $"Missing Renderer ({index})";
        }
    }
}
