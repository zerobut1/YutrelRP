using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;
using YutrelRP;

namespace YutrelRP.Editor
{
    [CustomEditor(typeof(YutrelDDGIVolume))]
    public sealed class YutrelDDGIVolumeEditor : UnityEditor.Editor
    {
        private static readonly Color selected_bounds_color = new(0.15f, 0.75f, 1.0f, 0.9f);
        private static readonly Color unselected_bounds_color = new(0.15f, 0.75f, 1.0f, 0.35f);
        private static readonly Color probe_color = new(1.0f, 0.62f, 0.18f, 0.85f);
        private static readonly GUIContent edit_bounds_label = new("Edit Bounds",
            "Edit the DDGI Volume bounds directly in the Scene View.");
        private static readonly GUIContent center_label = new("Center",
            "Local DDGI bounds offset. Rotation is ignored to keep the grid world axis aligned.");
        private static readonly GUIContent size_label = new("Size",
            "Local DDGI bounds size. Values are clamped to a positive size.");
        private static readonly GUIContent probe_count_label = new("Probe Count",
            "Probe grid count per axis. Each axis is clamped to [2, 64].");
        private static readonly GUIContent rays_per_probe_label = new("Rays Per Probe",
            "Volume-owned ProbeRayData width and probe trace dispatch X dimension.");
        private static readonly GUIContent probe_ray_data_format_label = new("Probe Ray Data Format",
            "RTXGI ProbeRayData storage format. F32x2 packs radiance into R and stores signed distance in G; F32x4 stores radiance in RGB and signed distance in A for trace debugging.");
        private static readonly GUIContent probe_max_ray_distance_label = new("Probe Max Ray Distance",
            "Volume-owned ray TMax for first-stage DDGI probe tracing.");
        private static readonly GUIContent probe_radius_label = new("Probe Preview Radius",
            "Scene View sphere radius in local units.");
        private static readonly GUIContent dump_textures_label = new("Dump DDGI Textures",
            "Export currently available DDGI textures as DDS files for offline RenderDoc inspection.");

        private static YutrelDDGIVolume editing_volume;

        private readonly BoxBoundsHandle bounds_handle = new();

        private SerializedProperty center_property;
        private SerializedProperty size_property;
        private SerializedProperty probe_count_property;
        private SerializedProperty rays_per_probe_property;
        private SerializedProperty probe_ray_data_format_property;
        private SerializedProperty probe_max_ray_distance_property;
        private SerializedProperty probe_preview_radius_property;

        private void OnEnable()
        {
            center_property = serializedObject.FindProperty("center");
            size_property = serializedObject.FindProperty("size");
            probe_count_property = serializedObject.FindProperty("probeCount");
            rays_per_probe_property = serializedObject.FindProperty("raysPerProbe");
            probe_ray_data_format_property = serializedObject.FindProperty("probeRayDataFormat");
            probe_max_ray_distance_property = serializedObject.FindProperty("probeMaxRayDistance");
            probe_preview_radius_property = serializedObject.FindProperty("probePreviewRadius");
            Selection.selectionChanged += OnSelectionChanged;
        }

        private void OnDisable()
        {
            Selection.selectionChanged -= OnSelectionChanged;
            if (editing_volume == target)
            {
                SetEditingVolume(null);
            }
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.PropertyField(center_property, center_label);
            EditorGUILayout.PropertyField(size_property, size_label);
            EditorGUILayout.PropertyField(probe_count_property, probe_count_label);
            EditorGUILayout.PropertyField(rays_per_probe_property, rays_per_probe_label);
            EditorGUILayout.PropertyField(probe_ray_data_format_property, probe_ray_data_format_label);
            EditorGUILayout.PropertyField(probe_max_ray_distance_property, probe_max_ray_distance_label);
            EditorGUILayout.PropertyField(probe_preview_radius_property, probe_radius_label);

            if (serializedObject.ApplyModifiedProperties())
            {
                SceneView.RepaintAll();
            }

            if (targets.Length == 1)
            {
                DrawSingleVolumeInspector((YutrelDDGIVolume)target);
            }
            else
            {
                EditorGUILayout.HelpBox("Bounds editing is available for one DDGI Volume at a time.",
                    MessageType.Info);
            }
        }

        private void OnSceneGUI()
        {
            var volume = (YutrelDDGIVolume)target;
            if (editing_volume != volume)
            {
                return;
            }

            if (!CanEditVolume(volume))
            {
                SetEditingVolume(null);
                return;
            }

            var bounds = volume.WorldBounds;
            bounds_handle.center = bounds.center;
            bounds_handle.size = bounds.size;
            bounds_handle.handleColor = selected_bounds_color;
            bounds_handle.wireframeColor = selected_bounds_color;

            EditorGUI.BeginChangeCheck();
            using (new Handles.DrawingScope(Matrix4x4.identity))
            {
                bounds_handle.DrawHandle();
            }

            if (!EditorGUI.EndChangeCheck())
            {
                return;
            }

            Undo.RecordObject(volume, "Edit DDGI Volume Bounds");
            volume.Center = WorldPointToLocalVolumeOffset(volume.transform, bounds_handle.center);
            volume.Size = WorldSizeToLocalVolumeSize(volume.transform, bounds_handle.size);
            EditorUtility.SetDirty(volume);
            PrefabUtility.RecordPrefabInstancePropertyModifications(volume);
            SceneView.RepaintAll();
        }

        [DrawGizmo(GizmoType.Active | GizmoType.Selected)]
        private static void DrawDDGIVolumeGizmo(YutrelDDGIVolume volume, GizmoType gizmo_type)
        {
            if (volume == null || !volume.isActiveAndEnabled)
            {
                return;
            }

            DrawVolumeBounds(volume, true);
            DrawProbeGrid(volume);
        }

        private void DrawSingleVolumeInspector(YutrelDDGIVolume volume)
        {
            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(!CanEditVolume(volume)))
            {
                var active = editing_volume == volume;
                var next_active = GUILayout.Toggle(active, edit_bounds_label, EditorStyles.miniButton);
                if (next_active != active)
                {
                    SetEditingVolume(next_active ? volume : null);
                }
            }

            if (!volume.isActiveAndEnabled)
            {
                EditorGUILayout.HelpBox("Enable the DDGI Volume to edit its Scene View bounds.",
                    MessageType.Info);
            }

            if (HasNearZeroScale(volume.transform.lossyScale))
            {
                EditorGUILayout.HelpBox("A zero Transform scale axis prevents reliable world/local bounds editing.",
                    MessageType.Warning);
            }

            var spacing = volume.GetWorldProbeSpacing();
            EditorGUILayout.LabelField("Total Probes", volume.TotalProbeCount.ToString());
            EditorGUILayout.LabelField("World Probe Spacing", FormatVector3(spacing));
            EditorGUILayout.LabelField("ProbeRayData Layout",
                $"{volume.RaysPerProbe} x {volume.ProbeCount.x * volume.ProbeCount.z} x {volume.ProbeCount.y}, {volume.RayDataFormat}");
            using (new EditorGUI.DisabledScope(DDGITextureDump.HasPendingRequest))
            {
                if (GUILayout.Button(dump_textures_label))
                {
                    DDGITextureDump.RequestDump();
                    SceneView.RepaintAll();
                }
            }
            EditorGUILayout.HelpBox(
                "DDGI probe grid bounds are the Volume bounds: boundary probes lie on min/max. For indoor scenes, keep boundary probes inside lit air with a small offset from walls, floor, and ceiling; avoid shrinking the volume so receiver surfaces fall outside, and avoid overshooting far outside the building.",
                MessageType.Info);
        }

        [MenuItem("YutrelRP/DDGI/Dump DDGI Textures")]
        private static void DumpDDGITextures()
        {
            DDGITextureDump.RequestDump();
            SceneView.RepaintAll();
        }

        private void OnSelectionChanged()
        {
            if (editing_volume == null)
            {
                return;
            }

            if (Selection.activeGameObject != editing_volume.gameObject)
            {
                SetEditingVolume(null);
            }
        }

        private static void SetEditingVolume(YutrelDDGIVolume volume)
        {
            if (editing_volume == volume)
            {
                return;
            }

            editing_volume = volume;
            SceneView.RepaintAll();
        }

        private static void DrawVolumeBounds(YutrelDDGIVolume volume, bool selected)
        {
            var previous_color = Gizmos.color;
            var previous_matrix = Gizmos.matrix;

            Gizmos.matrix = Matrix4x4.identity;
            Gizmos.color = selected ? selected_bounds_color : unselected_bounds_color;
            var bounds = volume.WorldBounds;
            Gizmos.DrawWireCube(bounds.center, bounds.size);

            Gizmos.matrix = previous_matrix;
            Gizmos.color = previous_color;
        }

        private static void DrawProbeGrid(YutrelDDGIVolume volume)
        {
            var previous_color = Gizmos.color;
            var previous_matrix = Gizmos.matrix;

            Gizmos.matrix = Matrix4x4.identity;
            Gizmos.color = probe_color;
            var count = volume.ProbeCount;
            var radius = Mathf.Max(YutrelDDGIVolume.MinProbePreviewRadius,
                volume.GetWorldProbePreviewRadius());

            for (var z = 0; z < count.z; z++)
            {
                for (var y = 0; y < count.y; y++)
                {
                    for (var x = 0; x < count.x; x++)
                    {
                        Gizmos.DrawSphere(volume.GetProbeWorldPosition(x, y, z), radius);
                    }
                }
            }

            Gizmos.matrix = previous_matrix;
            Gizmos.color = previous_color;
        }

        private static bool CanEditVolume(YutrelDDGIVolume volume)
        {
            return volume != null &&
                   volume.isActiveAndEnabled &&
                   !EditorUtility.IsPersistent(volume.gameObject) &&
                   !PrefabUtility.IsPartOfPrefabAsset(volume);
        }

        private static Vector3 WorldPointToLocalVolumeOffset(Transform transform, Vector3 world_point)
        {
            var scale = GetSafeSignedScale(transform.lossyScale);
            var offset = world_point - transform.position;
            return new Vector3(offset.x / scale.x, offset.y / scale.y, offset.z / scale.z);
        }

        private static Vector3 WorldSizeToLocalVolumeSize(Transform transform, Vector3 world_size)
        {
            var scale = Abs(GetSafeSignedScale(transform.lossyScale));
            return new Vector3(world_size.x / scale.x, world_size.y / scale.y, world_size.z / scale.z);
        }

        private static Vector3 GetSafeSignedScale(Vector3 scale)
        {
            return new Vector3(
                GetSafeSignedScaleAxis(scale.x),
                GetSafeSignedScaleAxis(scale.y),
                GetSafeSignedScaleAxis(scale.z));
        }

        private static float GetSafeSignedScaleAxis(float value)
        {
            if (Mathf.Abs(value) >= 0.0001f)
            {
                return value;
            }

            return value < 0.0f ? -1.0f : 1.0f;
        }

        private static Vector3 Abs(Vector3 value)
        {
            return new Vector3(Mathf.Abs(value.x), Mathf.Abs(value.y), Mathf.Abs(value.z));
        }

        private static bool HasNearZeroScale(Vector3 value)
        {
            return Mathf.Abs(value.x) < 0.0001f ||
                   Mathf.Abs(value.y) < 0.0001f ||
                   Mathf.Abs(value.z) < 0.0001f;
        }

        private static string FormatVector3(Vector3 value)
        {
            return $"{value.x:0.###}, {value.y:0.###}, {value.z:0.###}";
        }
    }
}
