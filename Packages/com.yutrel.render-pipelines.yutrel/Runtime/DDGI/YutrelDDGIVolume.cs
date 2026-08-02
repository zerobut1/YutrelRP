using UnityEngine;

namespace YutrelRP
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [AddComponentMenu("YutrelRP/DDGI Volume")]
    public sealed class YutrelDDGIVolume : MonoBehaviour
    {
        public const float MinVolumeSize = 0.01f;
        public const int MinProbeCountPerAxis = 2;
        public const int MaxProbeCountPerAxis = 64;
        public const int MinRaysPerProbe = 1;
        public const int MaxRaysPerProbe = 1024;
        public const float MinProbePreviewRadius = 0.01f;
        public const float MinProbeMaxRayDistance = 0.001f;
        public const int DefaultProbeIrradianceInteriorTexels = 6;
        public const int DefaultProbeDistanceInteriorTexels = 14;

        [Header("Bounds")]
        [Tooltip("Local DDGI bounds offset. Rotation is ignored to keep the grid world axis aligned.")]
        [SerializeField] private Vector3 center = Vector3.zero;
        [Tooltip("Local DDGI bounds size. Values are clamped to a positive size.")]
        [SerializeField] private Vector3 size = new(10.0f, 5.0f, 10.0f);
        [Tooltip("Probe grid count per axis. Boundary probes lie on the volume min/max bounds.")]
        [SerializeField] private Vector3Int probeCount = new(4, 3, 4);

        [Header("Trace")]
        [Tooltip("Number of ray samples traced from each probe every update.")]
        [Range(MinRaysPerProbe, MaxRaysPerProbe)]
        [SerializeField] private int raysPerProbe = 64;
        [Tooltip("Maximum world-space distance that probe rays may trace.")]
        [Min(MinProbeMaxRayDistance)]
        [SerializeField] private float probeMaxRayDistance = 100.0f;
        // Persistent atlas identity: probeCount rebuilds DDGI history atlases.
        // Frame-only: raysPerProbe changes ProbeRayData dimensions/metadata without clearing persistent atlas history.
        // Constant-only: max ray distance updates shader constants without clearing atlas history.

        [HideInInspector] [SerializeField] private int probeIrradianceInteriorTexels =
            DefaultProbeIrradianceInteriorTexels;
        [HideInInspector] [SerializeField] private int probeDistanceInteriorTexels =
            DefaultProbeDistanceInteriorTexels;

        [Header("Editor")]
        [Tooltip("Draw probe spheres in the Scene View when this DDGI Volume is selected.")]
        [SerializeField] private bool showProbeGizmos = true;
        [Tooltip("Maximum probe count allowed for Scene View probe sphere drawing.")]
        [Min(0)]
        [SerializeField] private int maxProbeGizmos = 4096;
        [Tooltip("Scene View probe sphere radius in local units.")]
        [Min(MinProbePreviewRadius)]
        [SerializeField] private float probePreviewRadius = 0.1f;

        public Vector3 Center
        {
            get => center;
            set => center = value;
        }

        public Vector3 Size
        {
            get => size;
            set => size = ClampSize(value);
        }

        public Vector3Int ProbeCount
        {
            get => probeCount;
            set => probeCount = ClampProbeCount(value);
        }

        public int RaysPerProbe
        {
            get => raysPerProbe;
            set => raysPerProbe = Mathf.Clamp(value, MinRaysPerProbe, MaxRaysPerProbe);
        }

        public float ProbeMaxRayDistance
        {
            get => probeMaxRayDistance;
            set => probeMaxRayDistance = Mathf.Max(MinProbeMaxRayDistance, value);
        }

        public int ProbeIrradianceInteriorTexels
        {
            get => probeIrradianceInteriorTexels;
            set => probeIrradianceInteriorTexels = DefaultProbeIrradianceInteriorTexels;
        }

        public int ProbeDistanceInteriorTexels
        {
            get => probeDistanceInteriorTexels;
            set => probeDistanceInteriorTexels = DefaultProbeDistanceInteriorTexels;
        }

        public float ProbePreviewRadius
        {
            get => probePreviewRadius;
            set => probePreviewRadius = Mathf.Max(MinProbePreviewRadius, value);
        }

        public bool ShowProbeGizmos => showProbeGizmos;

        public int MaxProbeGizmos => Mathf.Max(0, maxProbeGizmos);

        public Vector3 ProbeSpacing
        {
            get
            {
                var count = ProbeCount;
                var valid_size = Size;
                return new Vector3(
                    valid_size.x / (count.x - 1),
                    valid_size.y / (count.y - 1),
                    valid_size.z / (count.z - 1));
            }
        }

        public int TotalProbeCount
        {
            get
            {
                var count = ProbeCount;
                return count.x * count.y * count.z;
            }
        }

        public Bounds LocalBounds => new(Center, Size);

        public Bounds WorldBounds => new(GetWorldCenter(), GetWorldSize());

        public Vector3 GetWorldCenter()
        {
            return transform.position + Vector3.Scale(Center, transform.lossyScale);
        }

        public Vector3 GetWorldSize()
        {
            return Vector3.Scale(Size, Abs(transform.lossyScale));
        }

        public Vector3 GetWorldProbeSpacing()
        {
            var count = ProbeCount;
            var world_size = GetWorldSize();
            return new Vector3(
                world_size.x / (count.x - 1),
                world_size.y / (count.y - 1),
                world_size.z / (count.z - 1));
        }

        public Vector3 GetProbeWorldPosition(int x, int y, int z)
        {
            var count = ProbeCount;
            var bounds = WorldBounds;
            return new Vector3(
                Mathf.Lerp(bounds.min.x, bounds.max.x, GetNormalizedProbeCoordinate(x, count.x)),
                Mathf.Lerp(bounds.min.y, bounds.max.y, GetNormalizedProbeCoordinate(y, count.y)),
                Mathf.Lerp(bounds.min.z, bounds.max.z, GetNormalizedProbeCoordinate(z, count.z)));
        }

        public float GetWorldProbePreviewRadius()
        {
            var scale = Abs(transform.lossyScale);
            return Mathf.Max(MinProbePreviewRadius, ProbePreviewRadius * Mathf.Max(scale.x, scale.y, scale.z));
        }

        private void Reset()
        {
            Sanitize();
        }

        private void OnEnable()
        {
            Sanitize();
        }

        private void Update()
        {
            EnforceAxisAlignedRotation();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            Sanitize();
        }
#endif

        private void Sanitize()
        {
            size = ClampSize(size);
            probeCount = ClampProbeCount(probeCount);
            raysPerProbe = Mathf.Clamp(raysPerProbe, MinRaysPerProbe, MaxRaysPerProbe);
            probeMaxRayDistance = Mathf.Max(MinProbeMaxRayDistance, probeMaxRayDistance);
            probeIrradianceInteriorTexels = DefaultProbeIrradianceInteriorTexels;
            probeDistanceInteriorTexels = DefaultProbeDistanceInteriorTexels;
            maxProbeGizmos = Mathf.Max(0, maxProbeGizmos);
            probePreviewRadius = Mathf.Max(MinProbePreviewRadius, probePreviewRadius);
            EnforceAxisAlignedRotation();
        }

        private void EnforceAxisAlignedRotation()
        {
            if (transform.localRotation != Quaternion.identity)
            {
                transform.localRotation = Quaternion.identity;
            }
        }

        private static Vector3 ClampSize(Vector3 value)
        {
            return new Vector3(
                Mathf.Max(MinVolumeSize, value.x),
                Mathf.Max(MinVolumeSize, value.y),
                Mathf.Max(MinVolumeSize, value.z));
        }

        private static Vector3Int ClampProbeCount(Vector3Int value)
        {
            return new Vector3Int(
                Mathf.Clamp(value.x, MinProbeCountPerAxis, MaxProbeCountPerAxis),
                Mathf.Clamp(value.y, MinProbeCountPerAxis, MaxProbeCountPerAxis),
                Mathf.Clamp(value.z, MinProbeCountPerAxis, MaxProbeCountPerAxis));
        }

        private static Vector3 Abs(Vector3 value)
        {
            return new Vector3(Mathf.Abs(value.x), Mathf.Abs(value.y), Mathf.Abs(value.z));
        }

        private static float GetNormalizedProbeCoordinate(int index, int count)
        {
            return count <= 1 ? 0.0f : Mathf.Clamp01((float)index / (count - 1));
        }
    }
}
