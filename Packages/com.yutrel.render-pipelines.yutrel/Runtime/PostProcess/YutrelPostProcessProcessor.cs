using System;
using UnityEngine.Rendering.RenderGraphModule;

namespace YutrelRP
{
    public abstract class YutrelPostProcessProcessor : IDisposable
    {
        private readonly ToneMappingPass defaultPass = new();
        private bool disposed;

        /// <summary>Record any number of Raster/Compute passes and return the final color.</summary>
        public abstract TextureHandle Record(RenderGraph renderGraph, in YutrelPostProcessContext context);

        /// <summary>Apply default None/ACES to this source. Not called automatically afterwards.</summary>
        protected TextureHandle RecordDefault(RenderGraph renderGraph,
            in YutrelPostProcessContext context, TextureHandle sourceColor)
        {
            if (disposed) throw new ObjectDisposedException(GetType().Name);
            return defaultPass.Record(renderGraph, sourceColor, context.targetSize,
                context.settings, context.outputFormat);
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            try { Dispose(true); }
            finally { defaultPass.Dispose(); }
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing) { }
    }

    internal sealed class DefaultPostProcessProcessor : YutrelPostProcessProcessor
    {
        public override TextureHandle Record(RenderGraph renderGraph, in YutrelPostProcessContext context)
            => RecordDefault(renderGraph, context, context.sourceColor);
    }

    /// <summary>Owns configuration changes independently of scene-renderer resources.</summary>
    internal sealed class YutrelPostProcessState : IDisposable
    {
        private YutrelPostProcessAsset asset;
        private int version;
        private YutrelPostProcessProcessor processor;
        private bool disposed;

        internal YutrelPostProcessProcessor GetProcessor(YutrelPostProcessAsset requestedAsset)
        {
            if (disposed) throw new ObjectDisposedException(nameof(YutrelPostProcessState));
            // Destroyed Unity objects also switch back to the default.
            if (requestedAsset == null) requestedAsset = null;
            var requestedVersion = requestedAsset != null ? requestedAsset.Version : 0;
            if (processor != null && ReferenceEquals(asset, requestedAsset) && version == requestedVersion)
                return processor;
            var previous = processor;
            processor = null;
            previous?.Dispose();
            var next = requestedAsset != null ? requestedAsset.CreateProcessor() : new DefaultPostProcessProcessor();
            if (next == null) throw new InvalidOperationException("Post-process asset returned a null processor.");
            asset = requestedAsset;
            version = requestedVersion;
            processor = next;
            return processor;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            var previous = processor;
            processor = null;
            asset = null;
            previous?.Dispose();
        }
    }
}
