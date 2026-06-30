using UnityEngine;
using UnityEngine.Rendering;

namespace YutrelRP
{
    internal readonly struct YutrelRayTracingBuildConfig
    {
        public readonly Material override_material;
        public readonly RayTracingSubMeshFlags sub_mesh_flags;
        public readonly uint mask;

        public YutrelRayTracingBuildConfig(Material override_material, RayTracingSubMeshFlags sub_mesh_flags, uint mask)
        {
            this.override_material = override_material;
            this.sub_mesh_flags = sub_mesh_flags;
            this.mask = mask;
        }
    }
}
