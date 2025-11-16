using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace YutrelRP
{
    public class YutrelRP : RenderPipeline
    {
        private readonly RenderGraph render_graph = new("Yutrel Render Graph");
        private readonly YutrelRenderer renderer;
        private readonly YutrelRPSettings settings;

        public YutrelRP(YutrelRPSettings settings)
        {
            this.settings = settings;
            GraphicsSettings.useScriptableRenderPipelineBatching = settings.useSRPBatcher;
            renderer = new YutrelRenderer(this.settings);
        }

        protected override void Dispose(bool is_disposing)
        {
            base.Dispose(is_disposing);
            renderer.Dispose();
            render_graph.Cleanup();
        }

        protected override void Render(ScriptableRenderContext context, List<Camera> cameras)
        {
            foreach (var camera in cameras) renderer.Render(render_graph, context, camera);

            render_graph.EndFrame();
        }
    }
}