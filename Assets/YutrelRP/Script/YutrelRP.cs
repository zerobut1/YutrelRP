using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using YutrelRP.FrameData;

namespace YutrelRP
{
    public class YutrelRP : RenderPipeline
    {
        private YutrelRPAsset m_rp_asset;
        private RenderGraph m_render_graph;
        private YutrelRenderGraphRecorder m_render_graph_recorder;
        private ContextContainer m_context_container;

        public YutrelRP(YutrelRPAsset asset)
        {
            m_rp_asset = asset;
            InitRenderGraph();
        }

        protected override void Dispose(bool is_disposing)
        {
            CleanupRenderGraph();
            base.Dispose(is_disposing);
        }

        private void InitRenderGraph()
        {
            RTHandles.Initialize(Screen.width, Screen.height);
            m_render_graph = new RenderGraph("Example Render Graph")
            {
                // nativeRenderPassesEnabled = ExampleRPUtils.IsSupportsNativeRenderPassRenderGraphCompiler(),
                nativeRenderPassesEnabled = true,
            };
            m_render_graph_recorder = new YutrelRenderGraphRecorder();
            m_context_container = new ContextContainer();
        }

        private void CleanupRenderGraph()
        {
            m_context_container?.Dispose();
            m_context_container = null;
            m_render_graph_recorder?.Dispose();
            m_render_graph_recorder = null;
            m_render_graph?.Cleanup();
            m_render_graph = null;
        }

        protected override void Render(ScriptableRenderContext context, Camera[] cameras)
        {
            // donothing
        }

        protected override void Render(ScriptableRenderContext context, List<Camera> cameras)
        {
            BeginContextRendering(context, cameras);

            foreach (var camera in cameras)
            {
                RenderCamera(context, camera);
            }

            m_render_graph.EndFrame();

            EndContextRendering(context, cameras);
        }

        private void RenderCamera(ScriptableRenderContext context, Camera camera)
        {
            BeginCameraRendering(context, camera);

            if (!PrepareFrameData(context, camera))
            {
                return;
            }

            var cmd = CommandBufferPool.Get();

            RecordAndExecuteRenderGraph(context, camera, cmd);

            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();
            CommandBufferPool.Release(cmd);
            context.Submit();
            EndCameraRendering(context, camera);
        }

        private bool PrepareFrameData(ScriptableRenderContext context, Camera camera)
        {
            if (!camera.TryGetCullingParameters(out var culling_parameters))
            {
                return false;
            }

            var culling_results = context.Cull(ref culling_parameters);
            var camera_data = m_context_container.GetOrCreate<CameraData>();
            camera_data.camera = camera;
            camera_data.culling_results = culling_results;

            return true;
        }

        private void RecordAndExecuteRenderGraph(ScriptableRenderContext context, Camera camera, CommandBuffer cmd)
        {
            RenderGraphParameters render_graph_parameters = new RenderGraphParameters()
            {
                executionName = camera.name,
                commandBuffer = cmd,
                scriptableRenderContext = context,
                currentFrameIndex = Time.frameCount,
            };
            m_render_graph.BeginRecording(render_graph_parameters);
            m_render_graph_recorder.RecordRenderGraph(m_render_graph, m_context_container);
            m_render_graph.EndRecordingAndExecute();
        }
    }
}