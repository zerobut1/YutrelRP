using System;
using UnityEngine;

namespace YutrelRP
{
    public abstract class YutrelRendererData : ScriptableObject
    {
        [NonSerialized] private bool invalidated = true;

        internal bool IsInvalidated => invalidated;

        public new void SetDirty()
        {
            invalidated = true;
        }

        internal YutrelRenderer InternalCreateRenderer()
        {
            var renderer = CreateRenderer();
            invalidated = false;
            return renderer;
        }

        protected abstract YutrelRenderer CreateRenderer();

        protected virtual void OnValidate()
        {
            SetDirty();
        }
    }
}
