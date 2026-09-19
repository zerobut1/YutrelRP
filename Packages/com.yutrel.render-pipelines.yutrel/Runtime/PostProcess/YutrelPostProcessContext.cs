using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace YutrelRP
{
    /// <summary>
    /// Recording-time inputs. Scene color/depth are from the end of scene rendering;
    /// optional GBuffer attachments are from Base. Never retain handles across frames
    /// or read VolumeStack from an execution callback; snapshot settings into pass data.
    /// Color is already pre-exposed. Output uses the existing linear color/alpha convention;
    /// attachment formats perform sRGB conversion, not an extra shader gamma operation.
    /// </summary>
    public readonly struct YutrelPostProcessContext
    {
        public YutrelCameraRenderContext cameraContext { get; }
        public YutrelRendererOutput resources { get; }
        public ResolvedPostProcessSettings settings { get; }
        public VolumeStack volumeStack { get; }
        public GraphicsFormat outputFormat { get; }
        public Vector2Int targetSize => cameraContext.targetSize;
        public TextureHandle sourceColor => resources.sceneColor;

        public YutrelPostProcessContext(in YutrelCameraRenderContext cameraContext,
            in YutrelRendererOutput resources, in ResolvedPostProcessSettings settings,
            VolumeStack volumeStack, GraphicsFormat outputFormat)
        {
            this.cameraContext = cameraContext;
            this.resources = resources;
            this.settings = settings;
            this.volumeStack = volumeStack;
            this.outputFormat = outputFormat;
        }
    }
}
