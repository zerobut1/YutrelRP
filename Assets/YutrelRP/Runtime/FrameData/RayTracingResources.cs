using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace YutrelRP
{
    public class RayTracingResources : ContextItem
    {
        public RayTracingAccelerationStructure ddgi_acceleration_structure;
        public bool has_ddgi_acceleration_structure;
        public int ddgi_contributor_count;
        public string diagnostic;

        public override void Reset()
        {
            ddgi_acceleration_structure = null;
            has_ddgi_acceleration_structure = false;
            ddgi_contributor_count = 0;
            diagnostic = null;
        }

        internal void SetDDGIAccelerationStructure(RayTracingAccelerationStructure accelerationStructure,
            int contributorCount)
        {
            ddgi_acceleration_structure = accelerationStructure;
            ddgi_contributor_count = contributorCount;
            has_ddgi_acceleration_structure = accelerationStructure != null && contributorCount > 0;
            diagnostic = has_ddgi_acceleration_structure ? null : "DDGI RTAS has no valid contributors.";
        }

        internal void SetDDGIDiagnostic(string message)
        {
            ddgi_acceleration_structure = null;
            has_ddgi_acceleration_structure = false;
            ddgi_contributor_count = 0;
            diagnostic = message;
        }
    }
}
