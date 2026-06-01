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
        public const float MinProbePreviewRadius = 0.01f;

        [SerializeField] private Vector3 center = Vector3.zero;
        [SerializeField] private Vector3 size = new(10.0f, 5.0f, 10.0f);
        [SerializeField] private Vector3Int probeCount = new(4, 3, 4);
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

        public float ProbePreviewRadius
        {
            get => probePreviewRadius;
            set => probePreviewRadius = Mathf.Max(MinProbePreviewRadius, value);
        }

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
