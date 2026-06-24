using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace YutrelRP
{
    public class RayTracingResources : ContextItem
    {
        public RayTracingAccelerationStructure ddgi_acceleration_structure;
        public RayTracingAccelerationStructureHandle ddgi_acceleration_structure_handle;
        public bool has_ddgi_acceleration_structure;
        public bool ddgi_acceleration_structure_build_scheduled;
        public int ddgi_contributor_count;
        public string diagnostic;

        public override void Reset()
        {
            ddgi_acceleration_structure = null;
            ddgi_acceleration_structure_handle = RayTracingAccelerationStructureHandle.nullHandle;
            has_ddgi_acceleration_structure = false;
            ddgi_acceleration_structure_build_scheduled = false;
            ddgi_contributor_count = 0;
            diagnostic = null;
        }

        internal void SetDDGIAccelerationStructure(RayTracingAccelerationStructure accelerationStructure,
            RayTracingAccelerationStructureHandle accelerationStructureHandle, int contributorCount)
        {
            ddgi_acceleration_structure = accelerationStructure;
            ddgi_acceleration_structure_handle = accelerationStructureHandle;
            ddgi_acceleration_structure_build_scheduled = false;
            ddgi_contributor_count = contributorCount;
            has_ddgi_acceleration_structure = accelerationStructure != null &&
                                              accelerationStructureHandle.IsValid() &&
                                              contributorCount > 0;
            diagnostic = has_ddgi_acceleration_structure ? null : "DDGI RTAS has no valid contributors.";
        }

        internal void MarkDDGIAccelerationStructureBuildScheduled()
        {
            ddgi_acceleration_structure_build_scheduled = has_ddgi_acceleration_structure &&
                                                         ddgi_acceleration_structure_handle.IsValid();
            if (!ddgi_acceleration_structure_build_scheduled)
            {
                diagnostic = "DDGI RTAS build pass was not recorded.";
            }
        }

        internal void SetDDGIDiagnostic(string message)
        {
            ddgi_acceleration_structure = null;
            ddgi_acceleration_structure_handle = RayTracingAccelerationStructureHandle.nullHandle;
            has_ddgi_acceleration_structure = false;
            ddgi_acceleration_structure_build_scheduled = false;
            ddgi_contributor_count = 0;
            diagnostic = message;
        }
    }
}
