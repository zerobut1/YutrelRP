using UnityEngine;

namespace YutrelRP
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    public sealed class YutrelAdditionalCameraData : MonoBehaviour
    {
        public const int DefaultRendererIndex = -1;

        [SerializeField] private int rendererIndex = DefaultRendererIndex;

        public int RendererIndex => rendererIndex;

        public void SetRenderer(int index)
        {
            rendererIndex = index;
        }
    }
}
