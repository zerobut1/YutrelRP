using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace YutrelRP
{
    internal sealed class YutrelRayTracingAccelStruct : IDisposable
    {
        private readonly RayTracingAccelerationStructure acceleration_structure;
        private bool build_dirty = true;

        public YutrelRayTracingAccelStruct()
        {
            acceleration_structure = new RayTracingAccelerationStructure();
        }

        public RayTracingAccelerationStructure AccelerationStructure => acceleration_structure;

        public void Clear()
        {
            acceleration_structure.ClearInstances();
            MarkBuildDirty();
        }

        public void AddMesh(Mesh mesh, Matrix4x4 local_to_world, Material[] materials, uint mask)
        {
            if (mesh == null)
            {
                return;
            }

            for (var sub_mesh_index = 0; sub_mesh_index < mesh.subMeshCount; sub_mesh_index++)
            {
                var material = GetSubMeshMaterial(materials, sub_mesh_index);
                if (material == null)
                {
                    continue;
                }

                var instance_config = new RayTracingMeshInstanceConfig(mesh, (uint)sub_mesh_index, material)
                {
                    mask = mask,
                    subMeshFlags = GetSubMeshFlags(material),
                    enableTriangleCulling = false,
                    frontTriangleCounterClockwise = false
                };

                acceleration_structure.AddInstance(instance_config, local_to_world);
            }

            MarkBuildDirty();
        }

        public void MarkBuildDirty()
        {
            build_dirty = true;
        }

        public void BuildIfNeeded(IComputeCommandBuffer cmd)
        {
            if (!build_dirty || cmd == null)
            {
                return;
            }

            cmd.BuildRayTracingAccelerationStructure(acceleration_structure);
            build_dirty = false;
        }

        public void Dispose()
        {
            acceleration_structure.Dispose();
        }

        private static Material GetSubMeshMaterial(Material[] materials, int sub_mesh_index)
        {
            if (materials == null || materials.Length == 0)
            {
                return null;
            }

            var material_index = Mathf.Min(sub_mesh_index, materials.Length - 1);
            return materials[material_index];
        }

        private static RayTracingSubMeshFlags GetSubMeshFlags(Material material)
        {
            var alpha_clip = material != null &&
                             material.HasProperty("_UseAlphaClip") &&
                             material.GetFloat("_UseAlphaClip") > 0.5f;
            return alpha_clip
                ? RayTracingSubMeshFlags.Enabled | RayTracingSubMeshFlags.UniqueAnyHitCalls
                : RayTracingSubMeshFlags.Enabled | RayTracingSubMeshFlags.ClosestHitOnly;
        }
    }
}
