#if UNITY_EDITOR
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace YutrelRP
{
    internal sealed class DDGIProbeDebugPass
    {
        private const int SpherePassIndex = 0;
        private const int OverlayPassIndex = 1;
        private const int SphereLatitudeSegments = 12;
        private const int SphereLongitudeSegments = 24;

        private static readonly ProfilingSampler sampler = new("DDGI Probe Debug");
        private static readonly int debug_mode_ID = Shader.PropertyToID("_DDGIProbeDebugMode");
        private static readonly int debug_radius_ID = Shader.PropertyToID("_DDGIProbeDebugRadius");
        private static readonly int debug_distance_scale_ID = Shader.PropertyToID("_DDGIProbeDebugDistanceScale");
        private static readonly int scene_depth_ID = RenderTargets.scene_depth_ID;
        private static readonly int probe_bounds_min_ID = Shader.PropertyToID("_DDGIProbeBoundsMin");
        private static readonly int probe_spacing_ID = Shader.PropertyToID("_DDGIProbeSpacing");
        private static readonly int probe_count_ID = Shader.PropertyToID("_DDGIProbeCount");
        private static readonly int probe_irradiance_dimensions_ID =
            Shader.PropertyToID("_DDGIProbeIrradianceDimensions");
        private static readonly int probe_distance_dimensions_ID =
            Shader.PropertyToID("_DDGIProbeDistanceDimensions");
        private static readonly int probe_ray_data_dimensions_ID =
            Shader.PropertyToID("_DDGIProbeRayDataDimensions");
        private static readonly int irradiance_encoding_gamma_ID =
            Shader.PropertyToID("_DDGIIrradianceEncodingGamma");
        private static readonly int probe_irradiance_ID = Shader.PropertyToID("_DDGIProbeIrradiance");
        private static readonly int probe_distance_ID = Shader.PropertyToID("_DDGIProbeDistance");
        private static readonly int probe_ray_data_ID = Shader.PropertyToID("_DDGIProbeRayData");
        private static readonly int probe_data_ID = Shader.PropertyToID("_DDGIProbeData");
        private static readonly int probe_relocation_enabled_ID = Shader.PropertyToID("_DDGIProbeRelocationEnabled");

        private static Material material;
        private static MaterialPropertyBlock property_block;
        private static Mesh sphere_mesh;

        internal static void Record(RenderGraph render_graph, Camera camera, RenderTargets textures,
            DDGIResources resources, YutrelRPDebugSettings debug_settings, Vector2Int attachment_size)
        {
            var mode = debug_settings != null
                ? debug_settings.ddgi_probe_debug_mode
                : YutrelRPDebugSettings.DDGIProbeDebugMode.Disabled;
            if (mode == YutrelRPDebugSettings.DDGIProbeDebugMode.Disabled)
            {
                return;
            }

            if (camera.cameraType != CameraType.SceneView && camera.cameraType != CameraType.Game)
            {
                return;
            }

            if (render_graph == null || textures == null || resources == null || !resources.is_valid ||
                resources.active_volume == null || !textures.scene_color.IsValid())
            {
                return;
            }

            if (!ValidateModeResources(mode, resources))
            {
                return;
            }

            if (IsProbeMode(mode) && !textures.scene_depth.IsValid())
            {
                return;
            }

            if (!TryEnsureMaterial() || !TryEnsureSphereMesh())
            {
                return;
            }

            property_block ??= new MaterialPropertyBlock();

            using var builder = render_graph.AddRasterRenderPass<DDGIProbeDebugPass>(
                sampler.name, out var pass, sampler);

            var volume = resources.active_volume;
            var probe_count = volume.ProbeCount;
            var bounds = volume.WorldBounds;

            pass.mode = mode;
            pass.probe_count = probe_count;
            pass.total_probe_count = volume.TotalProbeCount;
            pass.probe_bounds_min = bounds.min;
            pass.probe_spacing = volume.GetWorldProbeSpacing();
            pass.debug_radius = Mathf.Max(0.001f, debug_settings.ddgi_probe_debug_radius);
            pass.debug_distance_scale = Mathf.Max(0.001f, debug_settings.ddgi_probe_debug_distance_scale);
            pass.irradiance_encoding_gamma = volume.IrradianceEncodingGamma;
            pass.probe_irradiance_dimensions = resources.ProbeIrradianceDimensions;
            pass.probe_distance_dimensions = resources.ProbeDistanceDimensions;
            pass.probe_ray_data_dimensions = new Vector4(
                volume.RaysPerProbe,
                probe_count.x * probe_count.z,
                probe_count.y,
                0.0f);
            pass.probe_irradiance = resources.probe_irradiance;
            pass.probe_distance = resources.probe_distance;
            pass.probe_ray_data = resources.probe_ray_data;
            pass.probe_data = resources.probe_data;
            pass.probe_relocation_enabled = volume.ProbeRelocationEnabled ? 1 : 0;

            builder.SetRenderAttachment(textures.scene_color, 0, AccessFlags.ReadWrite);

            if (IsProbeMode(mode) && textures.scene_depth.IsValid())
            {
                var debug_depth_desc = new TextureDesc(attachment_size.x, attachment_size.y)
                {
                    depthBufferBits = DepthBits.Depth32,
                    clearBuffer = true,
                    name = "DDGI Probe Debug Depth"
                };
                pass.scene_depth = textures.scene_depth;
                var debug_depth = render_graph.CreateTexture(debug_depth_desc);
                builder.UseTexture(pass.scene_depth, AccessFlags.Read);
                builder.SetRenderAttachmentDepth(debug_depth, AccessFlags.ReadWrite);
            }

            if (ReadsIrradiance(mode))
            {
                builder.UseTexture(pass.probe_irradiance, AccessFlags.Read);
            }

            if (ReadsDistance(mode))
            {
                builder.UseTexture(pass.probe_distance, AccessFlags.Read);
            }

            if (ReadsRayData(mode))
            {
                builder.UseTexture(pass.probe_ray_data, AccessFlags.Read);
            }

            if (IsProbeMode(mode))
            {
                builder.UseTexture(pass.probe_data, AccessFlags.Read);
            }

            builder.AllowPassCulling(false);
            builder.SetRenderFunc<DDGIProbeDebugPass>(static (pass, context) => pass.Render(context));
        }

        internal static void Cleanup()
        {
            CoreUtils.Destroy(material);
            CoreUtils.Destroy(sphere_mesh);
            material = null;
            sphere_mesh = null;
            property_block = null;
        }

        private YutrelRPDebugSettings.DDGIProbeDebugMode mode;
        private Vector3Int probe_count;
        private int total_probe_count;
        private Vector3 probe_bounds_min;
        private Vector3 probe_spacing;
        private float debug_radius;
        private float debug_distance_scale;
        private float irradiance_encoding_gamma;
        private Vector4 probe_irradiance_dimensions;
        private Vector4 probe_distance_dimensions;
        private Vector4 probe_ray_data_dimensions;
        private TextureHandle probe_irradiance;
        private TextureHandle probe_distance;
        private TextureHandle probe_ray_data;
        private TextureHandle probe_data;
        private TextureHandle scene_depth;
        private int probe_relocation_enabled;

        private void Render(RasterGraphContext context)
        {
            property_block.Clear();
            property_block.SetInteger(debug_mode_ID, (int)mode);
            property_block.SetFloat(debug_radius_ID, debug_radius);
            property_block.SetFloat(debug_distance_scale_ID, debug_distance_scale);
            property_block.SetVector(probe_bounds_min_ID, probe_bounds_min);
            property_block.SetVector(probe_spacing_ID, probe_spacing);
            property_block.SetVector(probe_count_ID,
                new Vector4(probe_count.x, probe_count.y, probe_count.z, 0.0f));
            property_block.SetVector(probe_irradiance_dimensions_ID, probe_irradiance_dimensions);
            property_block.SetVector(probe_distance_dimensions_ID, probe_distance_dimensions);
            property_block.SetVector(probe_ray_data_dimensions_ID, probe_ray_data_dimensions);
            property_block.SetFloat(irradiance_encoding_gamma_ID, irradiance_encoding_gamma);

            if (ReadsIrradiance(mode))
            {
                property_block.SetTexture(probe_irradiance_ID, probe_irradiance);
            }

            if (ReadsDistance(mode))
            {
                property_block.SetTexture(probe_distance_ID, probe_distance);
            }

            if (ReadsRayData(mode))
            {
                property_block.SetTexture(probe_ray_data_ID, probe_ray_data);
            }

            if (IsProbeMode(mode))
            {
                property_block.SetTexture(probe_data_ID, probe_data);
                property_block.SetInt(probe_relocation_enabled_ID, probe_relocation_enabled);
                property_block.SetTexture(scene_depth_ID, scene_depth);
                context.cmd.DrawMeshInstancedProcedural(
                    sphere_mesh, 0, material, SpherePassIndex, total_probe_count, property_block);
            }
            else
            {
                context.cmd.DrawProcedural(
                    Matrix4x4.identity, material, OverlayPassIndex, MeshTopology.Triangles, 3, 1, property_block);
            }
        }

        private static bool ValidateModeResources(YutrelRPDebugSettings.DDGIProbeDebugMode mode,
            DDGIResources resources)
        {
            if (ReadsIrradiance(mode) && !resources.probe_irradiance.IsValid())
            {
                return false;
            }

            if (ReadsDistance(mode) && !resources.probe_distance.IsValid())
            {
                return false;
            }

            if (ReadsRayData(mode) && !resources.probe_ray_data.IsValid())
            {
                return false;
            }

            return true;
        }

        private static bool IsProbeMode(YutrelRPDebugSettings.DDGIProbeDebugMode mode)
        {
            return mode == YutrelRPDebugSettings.DDGIProbeDebugMode.ProbeIrradiance ||
                   mode == YutrelRPDebugSettings.DDGIProbeDebugMode.ProbeDistance;
        }

        private static bool ReadsIrradiance(YutrelRPDebugSettings.DDGIProbeDebugMode mode)
        {
            return mode == YutrelRPDebugSettings.DDGIProbeDebugMode.ProbeIrradiance ||
                   mode == YutrelRPDebugSettings.DDGIProbeDebugMode.IrradianceAtlas;
        }

        private static bool ReadsDistance(YutrelRPDebugSettings.DDGIProbeDebugMode mode)
        {
            return mode == YutrelRPDebugSettings.DDGIProbeDebugMode.ProbeDistance ||
                   mode == YutrelRPDebugSettings.DDGIProbeDebugMode.DistanceAtlas;
        }

        private static bool ReadsRayData(YutrelRPDebugSettings.DDGIProbeDebugMode mode)
        {
            return mode == YutrelRPDebugSettings.DDGIProbeDebugMode.RayDataRadiance;
        }

        private static bool TryEnsureMaterial()
        {
            if (!GraphicsSettings.TryGetRenderPipelineSettings<YutrelDDGIShaderResources>(out var resources) ||
                resources == null)
            {
                YutrelRPRuntimeShaderUtility.WarnMissingResourceOnce(nameof(YutrelDDGIShaderResources));
                return false;
            }

            return YutrelRPRuntimeShaderUtility.TryCreateMaterial(
                resources.probe_debug,
                nameof(YutrelDDGIShaderResources.probe_debug),
                ref material);
        }

        private static bool TryEnsureSphereMesh()
        {
            if (sphere_mesh != null)
            {
                return true;
            }

            sphere_mesh = CreateSphereMesh(SphereLatitudeSegments, SphereLongitudeSegments);
            return sphere_mesh != null;
        }

        private static Mesh CreateSphereMesh(int latitude_segments, int longitude_segments)
        {
            var vertices = new Vector3[(latitude_segments + 1) * (longitude_segments + 1)];
            var normals = new Vector3[vertices.Length];
            var triangles = new int[latitude_segments * longitude_segments * 6];

            var vertex_index = 0;
            for (var y = 0; y <= latitude_segments; y++)
            {
                var v = (float)y / latitude_segments;
                var theta = v * Mathf.PI;
                var sin_theta = Mathf.Sin(theta);
                var cos_theta = Mathf.Cos(theta);

                for (var x = 0; x <= longitude_segments; x++)
                {
                    var u = (float)x / longitude_segments;
                    var phi = u * Mathf.PI * 2.0f;
                    var normal = new Vector3(
                        Mathf.Cos(phi) * sin_theta,
                        cos_theta,
                        Mathf.Sin(phi) * sin_theta);
                    vertices[vertex_index] = normal;
                    normals[vertex_index] = normal;
                    vertex_index++;
                }
            }

            var triangle_index = 0;
            var row_vertices = longitude_segments + 1;
            for (var y = 0; y < latitude_segments; y++)
            {
                for (var x = 0; x < longitude_segments; x++)
                {
                    var i0 = y * row_vertices + x;
                    var i1 = i0 + 1;
                    var i2 = i0 + row_vertices;
                    var i3 = i2 + 1;

                    triangles[triangle_index++] = i0;
                    triangles[triangle_index++] = i2;
                    triangles[triangle_index++] = i1;
                    triangles[triangle_index++] = i1;
                    triangles[triangle_index++] = i2;
                    triangles[triangle_index++] = i3;
                }
            }

            var mesh = new Mesh
            {
                name = "DDGI Probe Debug Sphere",
                hideFlags = HideFlags.HideAndDontSave
            };
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
#endif
