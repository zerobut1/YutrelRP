using System;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering.RenderGraphModule;

namespace YutrelRP.Tests
{
    public sealed class PostProcessLifecycleTests
    {
        private sealed class Asset : YutrelPostProcessAsset
        {
            internal Processor last;
            internal bool returnNull;
            public override YutrelPostProcessProcessor CreateProcessor() => returnNull ? null : last = new Processor();
        }

        private sealed class Processor : YutrelPostProcessProcessor
        {
            internal int disposeCount;
            public override TextureHandle Record(RenderGraph graph, in YutrelPostProcessContext context) => default;
            protected override void Dispose(bool disposing) => ++disposeCount;
        }

        private sealed class RendererData : YutrelRendererData
        {
            protected override YutrelRenderer CreateRenderer() => new Renderer();
        }

        private sealed class Renderer : YutrelRenderer
        {
            protected override YutrelRendererOutput RecordScene(RenderGraph graph, in YutrelCameraRenderContext context) => default;
            // Intentionally no base call: ownership must still be released by public Dispose.
            protected override void Dispose(bool disposing) { }
        }

        [Test]
        public void Cache_IsPerRenderer_AndRebuildsOnVersionOrReferenceChanges()
        {
            var a = ScriptableObject.CreateInstance<Asset>();
            var b = ScriptableObject.CreateInstance<Asset>();
            using var first = new YutrelPostProcessState();
            using var second = new YutrelPostProcessState();
            try
            {
                var initial = (Processor)first.GetProcessor(a);
                Assert.That(first.GetProcessor(a), Is.SameAs(initial));
                Assert.That(second.GetProcessor(a), Is.Not.SameAs(initial));
                a.SetDirty();
                var updated = (Processor)first.GetProcessor(a);
                Assert.That(updated, Is.Not.SameAs(initial));
                Assert.That(initial.disposeCount, Is.EqualTo(1));
                var replaced = (Processor)first.GetProcessor(b);
                Assert.That(updated.disposeCount, Is.EqualTo(1));
                Assert.That(first.GetProcessor(null), Is.TypeOf<DefaultPostProcessProcessor>());
                Assert.That(replaced.disposeCount, Is.EqualTo(1));
            }
            finally { UnityEngine.Object.DestroyImmediate(a); UnityEngine.Object.DestroyImmediate(b); }
        }

        [Test]
        public void InvalidFactory_DoesNotReuseDisposedProcessor_AndCanRetry()
        {
            var asset = ScriptableObject.CreateInstance<Asset>();
            using var state = new YutrelPostProcessState();
            try
            {
                var previous = (Processor)state.GetProcessor(asset);
                asset.returnNull = true;
                asset.SetDirty();
                Assert.Throws<InvalidOperationException>(() => state.GetProcessor(asset));
                Assert.That(previous.disposeCount, Is.EqualTo(1));
                asset.returnNull = false;
                Assert.That(state.GetProcessor(asset), Is.Not.SameAs(previous));
                state.Dispose();
                state.Dispose();
                Assert.That(asset.last.disposeCount, Is.EqualTo(1));
                Assert.Throws<ObjectDisposedException>(() => state.GetProcessor(asset));
            }
            finally { UnityEngine.Object.DestroyImmediate(asset); }
        }

        [Test]
        public void InvalidOutput_IsRejected_AndDerivedRendererCannotSkipCleanup()
        {
            var asset = ScriptableObject.CreateInstance<Asset>();
            var data = ScriptableObject.CreateInstance<RendererData>();
            data.PostProcess = asset;
            var renderer = data.InternalCreateRenderer();
            try
            {
                Assert.Throws<InvalidOperationException>(() => renderer.RecordPostProcessing(null, default));
                renderer.Dispose();
                renderer.Dispose();
                Assert.That(asset.last.disposeCount, Is.EqualTo(1));
            }
            finally
            {
                renderer.Dispose();
                UnityEngine.Object.DestroyImmediate(data);
                UnityEngine.Object.DestroyImmediate(asset);
            }
        }
    }
}
