using UnityEngine;

namespace YutrelRP
{
    public sealed class YutrelDeferredRendererData : YutrelRendererData
    {
        [SerializeField] private YutrelDeferredRendererSettings settings = new();

        public YutrelDeferredRendererSettings Settings => settings ??= new YutrelDeferredRendererSettings();

        protected override YutrelRenderer CreateRenderer()
        {
            return new YutrelDeferredRenderer(Settings);
        }

        internal void SetSettings(YutrelDeferredRendererSettings value)
        {
            settings = value ?? new YutrelDeferredRendererSettings();
            SetDirty();
        }
    }
}
