using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace YutrelRP
{
    public class YutrelRP : RenderPipeline
    {
        private readonly RenderGraph m_render_graph = new("Yutrel Render Graph");
        private readonly YutrelRenderer m_renderer;
        private readonly YutrelRPSettings m_settings;

        public YutrelRP(YutrelRPSettings settings)
        {
            m_settings = settings;
            m_renderer = new YutrelRenderer();
        }

        protected override void Dispose(bool is_disposing)
        {
            base.Dispose(is_disposing);
            m_renderer.Dispose();
            m_render_graph.Cleanup();
        }

        protected override void Render(ScriptableRenderContext context, List<Camera> cameras)
        {
            foreach (var camera in cameras) m_renderer.Render(m_render_graph, context, camera);

            m_render_graph.EndFrame();
        }
    }
}