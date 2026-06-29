using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.UnifiedRayTracing;

namespace YutrelRP
{
    internal sealed class YutrelRayTracingAccelStruct : IDisposable
    {
        private readonly IRayTracingAccelStruct acceleration_structure;
        private readonly Dictionary<EntityId, int[]> object_instances = new();
        private GraphicsBuffer build_scratch_buffer;
        private bool build_dirty = true;
        private uint next_instance_id;

        public YutrelRayTracingAccelStruct(RayTracingContext context)
        {
            acceleration_structure = context.CreateAccelerationStructure(new AccelerationStructureOptions
            {
                buildFlags = BuildFlags.PreferFastTrace
            });
        }

        public void Clear()
        {
            acceleration_structure.ClearInstances();
            object_instances.Clear();
            next_instance_id = 0;
            MarkBuildDirty();
        }

        public void AddMesh(EntityId object_id, Mesh mesh, Matrix4x4 local_to_world, uint mask)
        {
            if (mesh == null)
            {
                return;
            }

            var sub_mesh_count = mesh.subMeshCount;
            var handles = new int[sub_mesh_count];
            for (var sub_mesh_index = 0; sub_mesh_index < sub_mesh_count; sub_mesh_index++)
            {
                var desc = new MeshInstanceDesc(mesh, sub_mesh_index)
                {
                    localToWorldMatrix = local_to_world,
                    instanceID = next_instance_id++,
                    mask = mask,
                    opaqueGeometry = true
                };
                handles[sub_mesh_index] = acceleration_structure.AddInstance(desc);
            }

            object_instances[object_id] = handles;
            MarkBuildDirty();
        }

        public void MarkBuildDirty()
        {
            build_dirty = true;
        }

        public void BuildIfNeeded(CommandBuffer cmd)
        {
            if (!build_dirty)
            {
                return;
            }

            RayTracingHelper.ResizeScratchBufferForBuild(acceleration_structure, ref build_scratch_buffer);
            acceleration_structure.Build(cmd, build_scratch_buffer);
            build_dirty = false;
        }

        public void Bind(CommandBuffer cmd, string name, IRayTracingShader shader)
        {
            shader.SetAccelerationStructure(cmd, name, acceleration_structure);
        }

        public void Dispose()
        {
            build_scratch_buffer?.Dispose();
            acceleration_structure.Dispose();
            object_instances.Clear();
        }
    }
}
