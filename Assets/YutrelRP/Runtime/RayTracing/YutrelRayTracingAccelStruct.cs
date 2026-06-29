using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.UnifiedRayTracing;

namespace YutrelRP
{
    internal sealed class YutrelRayTracingAccelStruct : IDisposable
    {
        private readonly IRayTracingAccelStruct acceleration_structure;
        private readonly Dictionary<EntityId, int[]> object_instances = new();
        private readonly List<Vector3> vertex_positions = new();
        private readonly List<uint> indices = new();
        private readonly List<GeometryInstanceData> instance_data = new();
        private readonly List<Vector3> mesh_vertices_cache = new();
        private GraphicsBuffer build_scratch_buffer;
        private GraphicsBuffer vertex_positions_buffer;
        private GraphicsBuffer indices_buffer;
        private GraphicsBuffer instance_data_buffer;
        private bool build_dirty = true;
        private uint next_instance_id;

        private static readonly int vertex_positions_ID = Shader.PropertyToID("_DDGIVertexPositions");
        private static readonly int indices_ID = Shader.PropertyToID("_DDGIIndices");
        private static readonly int instance_data_ID = Shader.PropertyToID("_DDGIInstanceData");

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
            vertex_positions.Clear();
            indices.Clear();
            instance_data.Clear();
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
            var vertex_offset = vertex_positions.Count;
            mesh.GetVertices(mesh_vertices_cache);
            vertex_positions.AddRange(mesh_vertices_cache);
            mesh_vertices_cache.Clear();

            for (var sub_mesh_index = 0; sub_mesh_index < sub_mesh_count; sub_mesh_index++)
            {
                var instance_id = next_instance_id++;
                var index_offset = indices.Count;
                var sub_mesh_indices = mesh.GetIndices(sub_mesh_index, true);
                for (var index = 0; index < sub_mesh_indices.Length; index++)
                {
                    indices.Add((uint)(vertex_offset + sub_mesh_indices[index]));
                }

                instance_data.Add(new GeometryInstanceData(local_to_world, (uint)vertex_offset, (uint)index_offset));
                var desc = new MeshInstanceDesc(mesh, sub_mesh_index)
                {
                    localToWorldMatrix = local_to_world,
                    instanceID = instance_id,
                    mask = mask,
                    opaqueGeometry = true,
                    enableTriangleCulling = false
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
            UploadGeometryBuffers();
            acceleration_structure.Build(cmd, build_scratch_buffer);
            build_dirty = false;
        }

        public void Bind(CommandBuffer cmd, string name, IRayTracingShader shader)
        {
            shader.SetAccelerationStructure(cmd, name, acceleration_structure);
            shader.SetBufferParam(cmd, vertex_positions_ID, vertex_positions_buffer);
            shader.SetBufferParam(cmd, indices_ID, indices_buffer);
            shader.SetBufferParam(cmd, instance_data_ID, instance_data_buffer);
        }

        public void Dispose()
        {
            build_scratch_buffer?.Dispose();
            vertex_positions_buffer?.Dispose();
            indices_buffer?.Dispose();
            instance_data_buffer?.Dispose();
            acceleration_structure.Dispose();
            object_instances.Clear();
            vertex_positions.Clear();
            indices.Clear();
            instance_data.Clear();
            mesh_vertices_cache.Clear();
        }

        private void UploadGeometryBuffers()
        {
            EnsureStructuredBuffer(ref vertex_positions_buffer, Math.Max(vertex_positions.Count, 1), 3 * sizeof(float));
            EnsureStructuredBuffer(ref indices_buffer, Math.Max(indices.Count, 1), sizeof(uint));
            EnsureStructuredBuffer(ref instance_data_buffer, Math.Max(instance_data.Count, 1), GeometryInstanceData.stride);

            if (vertex_positions.Count > 0)
            {
                vertex_positions_buffer.SetData(vertex_positions.ToArray());
            }

            if (indices.Count > 0)
            {
                indices_buffer.SetData(indices.ToArray());
            }

            if (instance_data.Count > 0)
            {
                instance_data_buffer.SetData(instance_data.ToArray());
            }
        }

        private static void EnsureStructuredBuffer(ref GraphicsBuffer buffer, int count, int stride)
        {
            if (buffer != null && buffer.count >= count && buffer.stride == stride)
            {
                return;
            }

            buffer?.Dispose();
            buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, count, stride);
        }

        [StructLayout(LayoutKind.Sequential)]
        private readonly struct GeometryInstanceData
        {
            public const int stride = 4 * 4 * 3 + 4 * 2 + 4 * 2;

            private readonly Vector4 local_to_world_row0;
            private readonly Vector4 local_to_world_row1;
            private readonly Vector4 local_to_world_row2;
            private readonly uint vertex_offset;
            private readonly uint index_offset;
            private readonly Vector2 padding;

            public GeometryInstanceData(Matrix4x4 local_to_world, uint vertex_offset, uint index_offset)
            {
                local_to_world_row0 = local_to_world.GetRow(0);
                local_to_world_row1 = local_to_world.GetRow(1);
                local_to_world_row2 = local_to_world.GetRow(2);
                this.vertex_offset = vertex_offset;
                this.index_offset = index_offset;
                padding = Vector2.zero;
            }
        }
    }
}
