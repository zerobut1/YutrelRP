using System;
using UnityEngine;

namespace YutrelRP
{
    /// <summary>Configuration only. Each renderer owns a separate processor instance.</summary>
    public abstract class YutrelPostProcessAsset : ScriptableObject
    {
        [NonSerialized] private int version;
        public int Version => version;
        public abstract YutrelPostProcessProcessor CreateProcessor();

        /// <summary>Call after changing configuration at runtime.</summary>
        public new void SetDirty() { unchecked { ++version; } }
        protected virtual void OnValidate() => SetDirty();
    }
}
