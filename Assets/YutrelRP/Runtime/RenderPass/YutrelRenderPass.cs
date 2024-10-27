using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace YutrelRP
{
    public abstract class YutrelRenderPass : IRenderGraphRecorder
    {
        public virtual void RecordRenderGraph(RenderGraph graph, ContextContainer frame_data)
        {
            Debug.LogWarning("The render pass" + this.ToString() +
                             "does not have an implementation of the RecordRenderGraph method");
        }
    }
}