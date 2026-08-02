using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace YutrelRP
{
    public readonly struct YutrelRendererOutput
    {
        public TextureHandle sceneColor { get; }
        public TextureHandle sceneDepth { get; }

        public bool isValid => sceneColor.IsValid();

        public YutrelRendererOutput(TextureHandle sceneColor, TextureHandle sceneDepth = default)
        {
            this.sceneColor = sceneColor;
            this.sceneDepth = sceneDepth;
        }
    }

    public readonly struct YutrelCameraRenderContext
    {
        public Camera camera { get; }
        public ScriptableRenderContext renderContext { get; }
        public Vector2Int targetSize { get; }
        public GraphicsFormat sceneColorFormat { get; }
        public int frameIndex { get; }
        public float preExposure { get; }
        public float oneOverPreExposure { get; }

#if UNITY_EDITOR
        internal YutrelRPDebugSettings debugSettings { get; }
#endif

        internal YutrelCameraRenderContext(
            Camera camera,
            ScriptableRenderContext renderContext,
            Vector2Int targetSize,
            GraphicsFormat sceneColorFormat,
            int frameIndex,
            float preExposure,
            float oneOverPreExposure
#if UNITY_EDITOR
            , YutrelRPDebugSettings debugSettings
#endif
        )
        {
            this.camera = camera;
            this.renderContext = renderContext;
            this.targetSize = targetSize;
            this.sceneColorFormat = sceneColorFormat;
            this.frameIndex = frameIndex;
            this.preExposure = preExposure;
            this.oneOverPreExposure = oneOverPreExposure;
#if UNITY_EDITOR
            this.debugSettings = debugSettings;
#endif
        }
    }

    public abstract class YutrelRenderer : IDisposable
    {
        private bool disposed;

        internal void Render(
            RenderGraph renderGraph,
            ScriptableRenderContext renderContext,
            Camera camera
#if UNITY_EDITOR
            , YutrelRPDebugSettings debugSettings
#endif
        )
        {
            if (disposed)
            {
                throw new ObjectDisposedException(GetType().Name);
            }

            VolumeManager.instance.Update(camera.transform, ~0);
            var postProcessSettings = YutrelSceneRenderSettings.Resolve(VolumeManager.instance.stack);
            var targetSize = GetTargetSize(camera);
            if (targetSize.x <= 0 || targetSize.y <= 0)
            {
                return;
            }

            var sceneColorFormat = GraphicsFormat.R16G16B16A16_SFloat;
            var cameraContext = new YutrelCameraRenderContext(
                camera,
                renderContext,
                targetSize,
                sceneColorFormat,
                Time.frameCount,
                postProcessSettings.exposure.pre_exposure,
                postProcessSettings.exposure.one_over_pre_exposure
#if UNITY_EDITOR
                , debugSettings
#endif
            );

            var commandBuffer = CommandBufferPool.Get();
            var executeCommandBuffer = false;
            var parameters = new RenderGraphParameters
            {
                scriptableRenderContext = renderContext,
                commandBuffer = commandBuffer,
                executionId = camera.GetEntityId(),
                generateDebugData = RenderGraph.isRenderGraphViewerActive,
                currentFrameIndex = Time.frameCount
            };

            try
            {
                renderGraph.BeginRecording(parameters);
                using (new RenderGraphProfilingScope(renderGraph, ProfilingSampler.Get(camera.cameraType)))
                {
                    var cameraOutput = ImportCameraTarget(renderGraph, camera);
                    SetupPass.Record(renderGraph, camera, targetSize, postProcessSettings);

#if UNITY_EDITOR
                    if (camera.cameraType == CameraType.SceneView)
                    {
                        ScriptableRenderContext.EmitWorldGeometryForSceneView(camera);
                    }
#endif

                    var output = RecordScene(renderGraph, cameraContext);
                    if (!output.sceneColor.IsValid())
                    {
                        throw new InvalidOperationException($"{GetType().Name} returned an invalid scene color.");
                    }
#if UNITY_EDITOR
                    GizmosPass.Record(
                        renderGraph,
                        camera,
                        output.sceneColor,
                        output.sceneDepth,
                        GizmoSubset.PreImageEffects);
#endif

                    var finalColor = ToneMappingPass.Record(
                        renderGraph,
                        output.sceneColor,
                        targetSize,
                        postProcessSettings);

                    finalColor = RecordAfterPostProcessing(
                        renderGraph,
                        cameraContext,
                        output,
                        finalColor);

                    if (!finalColor.IsValid())
                    {
                        throw new InvalidOperationException($"{GetType().Name} returned an invalid post-processed color.");
                    }

#if UNITY_EDITOR
                    GizmosPass.Record(
                        renderGraph,
                        camera,
                        finalColor,
                        output.sceneDepth,
                        GizmoSubset.PostImageEffects);
#endif

                    FinalPass.Record(renderGraph, camera, finalColor, cameraOutput);
                }

                renderGraph.EndRecordingAndExecute();
                executeCommandBuffer = true;
            }
            catch (Exception exception)
            {
                renderGraph.ResetGraphAndLogException(exception);
            }
            finally
            {
                if (executeCommandBuffer)
                {
                    renderContext.ExecuteCommandBuffer(commandBuffer);
                    renderContext.Submit();
                }

                CommandBufferPool.Release(commandBuffer);
            }
        }

        protected abstract YutrelRendererOutput RecordScene(
            RenderGraph renderGraph,
            in YutrelCameraRenderContext context);

        protected virtual TextureHandle RecordAfterPostProcessing(
            RenderGraph renderGraph,
            in YutrelCameraRenderContext context,
            in YutrelRendererOutput output,
            TextureHandle postProcessedColor)
        {
            return postProcessedColor;
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
        }

        private static Vector2Int GetTargetSize(Camera camera)
        {
            var targetTexture = camera.targetTexture;
            return targetTexture == null
                ? new Vector2Int(camera.pixelWidth, camera.pixelHeight)
                : new Vector2Int(targetTexture.width, targetTexture.height);
        }

        private static TextureHandle ImportCameraTarget(RenderGraph renderGraph, Camera camera)
        {
            var targetTexture = camera.targetTexture;
            var isBackbuffer = targetTexture == null;
            var targetIdentifier = isBackbuffer
                ? new RenderTargetIdentifier(BuiltinRenderTextureType.CameraTarget)
                : new RenderTargetIdentifier(targetTexture);

            var importParameters = new ImportResourceParams
            {
                clearOnFirstUse = false,
                clearColor = camera.clearFlags == CameraClearFlags.Color
                    ? camera.backgroundColor.linear
                    : Color.clear,
                discardOnLastUse = false
            };

            var info = new RenderTargetInfo
            {
                width = isBackbuffer ? Screen.width : targetTexture.width,
                height = isBackbuffer ? Screen.height : targetTexture.height,
                volumeDepth = isBackbuffer ? 1 : targetTexture.volumeDepth,
                msaaSamples = isBackbuffer ? 1 : targetTexture.antiAliasing,
                format = isBackbuffer
                    ? GraphicsFormatUtility.GetGraphicsFormat(
                        RenderTextureFormat.Default,
                        QualitySettings.activeColorSpace == ColorSpace.Linear)
                    : targetTexture.graphicsFormat,
                bindMS = !isBackbuffer && targetTexture.bindTextureMS
            };

            return renderGraph.ImportBackbuffer(targetIdentifier, info, importParameters);
        }
    }
}
