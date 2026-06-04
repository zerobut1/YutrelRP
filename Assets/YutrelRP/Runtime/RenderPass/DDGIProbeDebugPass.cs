#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace YutrelRP
{
    internal class DDGIProbeDebugPass
    {
        private const string shader_name = "YutrelRP/DDGIProbeDebug";

        private static readonly ProfilingSampler sampler = new("DDGI Probe Debug Pass");
        private static readonly int debug_mode_ID = Shader.PropertyToID("_DDGIProbeDebugMode");
        private static readonly int debug_issue_ID = Shader.PropertyToID("_DDGIProbeDebugIssue");
        private static readonly int probe_count_ID = Shader.PropertyToID("_DDGIProbeCount");
        private static readonly int probe_radius_ID = Shader.PropertyToID("_DDGIProbeDebugRadius");
        private static readonly int probe_ray_data_ID = DDGIResources.probe_ray_data_ID;
        private static readonly int probe_ray_data_dimensions_ID = DDGIResources.probe_ray_data_dimensions_ID;
        private static readonly int probe_ray_data_max_distance_ID = DDGIResources.probe_ray_data_max_distance_ID;
        private static readonly int probe_irradiance_ID = DDGIResources.probe_irradiance_ID;
        private static readonly int probe_irradiance_dimensions_ID = DDGIResources.probe_irradiance_dimensions_ID;
        private static readonly int probe_distance_ID = DDGIResources.probe_distance_ID;
        private static readonly int probe_distance_dimensions_ID = DDGIResources.probe_distance_dimensions_ID;
        private static readonly int probe_data_ID = DDGIResources.probe_data_ID;
        private static readonly int probe_data_dimensions_ID = DDGIResources.probe_data_dimensions_ID;
        private static readonly int volume_min_ws_ID = DDGIResources.volume_min_ws_ID;
        private static readonly int probe_spacing_ws_ID = DDGIResources.probe_spacing_ws_ID;
        private static readonly int probe_relocation_enabled_ID = DDGIResources.probe_relocation_enabled_ID;

        private static Material material;
        private static Mesh sphere_mesh;
        private static MaterialPropertyBlock property_block;
        private static readonly HashSet<string> warned_issues = new();

        internal static void Record(RenderGraph render_graph, Camera camera, RenderTargets textures,
            DDGIResources ddgi_resources, YutrelRPSettings.DebugViewMode mode,
            YutrelRPSettings.DDGISettings ddgi_settings)
        {
            if (!IsProbeSceneMode(mode)) return;
            if (camera.cameraType != CameraType.SceneView) return;
            if (!textures.final_color.IsValid() || !textures.scene_depth.IsValid()) return;

            if (material == null) material = CoreUtils.CreateEngineMaterial(Shader.Find(shader_name));
            if (sphere_mesh == null) sphere_mesh = CreateSphereMesh();
            if (property_block == null) property_block = new MaterialPropertyBlock();

            using var builder = render_graph.AddRasterRenderPass<DDGIProbeDebugPass>(sampler.name, out var pass, sampler);

            pass.mode = mode;
            pass.issue = ValidateSources(mode, ddgi_resources);
            pass.probe_count = ddgi_resources != null ? ddgi_resources.probe_count : Vector3Int.zero;
            pass.probe_radius = GetProbeRadius(ddgi_resources);
            pass.probe_ray_data_max_distance = ddgi_resources != null
                ? Mathf.Max(0.001f, ddgi_resources.probe_max_ray_distance)
                : 0.001f;
            pass.probe_ray_data_dimensions = ddgi_resources != null ? ddgi_resources.ProbeRayDataDimensions : Vector4.zero;
            pass.probe_irradiance_dimensions = ddgi_resources != null ? ddgi_resources.ProbeIrradianceDimensions : Vector4.zero;
            pass.probe_distance_dimensions = ddgi_resources != null ? ddgi_resources.ProbeDistanceDimensions : Vector4.zero;
            pass.probe_data_dimensions = ddgi_resources != null ? ddgi_resources.ProbeDataDimensions : Vector4.zero;
            pass.volume_min_ws = ddgi_resources != null ? ddgi_resources.volume_min_ws : Vector3.zero;
            pass.probe_spacing_ws = ddgi_resources != null ? ddgi_resources.probe_spacing_ws : Vector3.zero;
            pass.probe_relocation_enabled =
                ddgi_resources != null && ddgi_resources.probe_relocation_enabled &&
                ddgi_settings != null && ddgi_settings.probeRelocationEnabled
                    ? 1.0f
                    : 0.0f;
            pass.instance_count = pass.probe_count.x * pass.probe_count.y * pass.probe_count.z;
            pass.reads_probe_ray_data = pass.issue == Issue.None && mode == YutrelRPSettings.DebugViewMode.DDGIProbeRayDataQualityScene;
            pass.reads_probe_irradiance = pass.issue == Issue.None && mode == YutrelRPSettings.DebugViewMode.DDGIProbeIrradianceScene;
            pass.reads_probe_distance = pass.issue == Issue.None && mode == YutrelRPSettings.DebugViewMode.DDGIProbeDistanceScene;
            pass.reads_probe_data = pass.issue == Issue.None;

            if (pass.reads_probe_data)
            {
                pass.probe_data = ddgi_resources.probe_data;
                builder.UseTexture(pass.probe_data);
            }

            if (pass.reads_probe_ray_data)
            {
                pass.probe_ray_data = ddgi_resources.probe_ray_data;
                builder.UseTexture(pass.probe_ray_data);
            }

            if (pass.reads_probe_irradiance)
            {
                pass.probe_irradiance = ddgi_resources.probe_irradiance;
                builder.UseTexture(pass.probe_irradiance);
            }

            if (pass.reads_probe_distance)
            {
                pass.probe_distance = ddgi_resources.probe_distance;
                builder.UseTexture(pass.probe_distance);
            }

            builder.SetRenderAttachment(textures.final_color, 0, AccessFlags.ReadWrite);
            builder.SetRenderAttachmentDepth(textures.scene_depth, AccessFlags.Read);
            builder.AllowPassCulling(false);
            builder.SetRenderFunc<DDGIProbeDebugPass>(static (pass, context) => pass.Render(context));
        }

        private static bool IsProbeSceneMode(YutrelRPSettings.DebugViewMode mode)
        {
            return mode == YutrelRPSettings.DebugViewMode.DDGIProbeIrradianceScene ||
                   mode == YutrelRPSettings.DebugViewMode.DDGIProbeRayDataQualityScene ||
                   mode == YutrelRPSettings.DebugViewMode.DDGIProbeDistanceScene;
        }

        private static Issue ValidateSources(YutrelRPSettings.DebugViewMode mode, DDGIResources ddgi_resources)
        {
            Issue issue = Issue.None;
            if (ddgi_resources == null ||
                ddgi_resources.probe_count.x <= 0 ||
                ddgi_resources.probe_count.y <= 0 ||
                ddgi_resources.probe_count.z <= 0)
            {
                issue = Issue.MissingVolume;
            }
            else if (mode == YutrelRPSettings.DebugViewMode.DDGIProbeIrradianceScene &&
                     !ddgi_resources.probe_irradiance.IsValid())
            {
                issue = Issue.MissingIrradiance;
            }
            else if (mode == YutrelRPSettings.DebugViewMode.DDGIProbeDistanceScene &&
                     !ddgi_resources.probe_distance.IsValid())
            {
                issue = Issue.MissingDistance;
            }
            else if (mode == YutrelRPSettings.DebugViewMode.DDGIProbeRayDataQualityScene &&
                     !ddgi_resources.probe_ray_data.IsValid())
            {
                issue = Issue.MissingRayData;
            }
            else if (!ddgi_resources.probe_data.IsValid())
            {
                issue = Issue.MissingProbeData;
            }

            WarnOnce(mode, issue);
            return issue;
        }

        private static float GetProbeRadius(DDGIResources resources)
        {
            if (resources == null) return 0.05f;
            var spacing = resources.probe_spacing_ws;
            var min_spacing = Mathf.Min(Mathf.Abs(spacing.x), Mathf.Abs(spacing.y), Mathf.Abs(spacing.z));
            return Mathf.Max(0.02f, min_spacing * 0.08f);
        }

        private static void WarnOnce(YutrelRPSettings.DebugViewMode mode, Issue issue)
        {
            if (issue == Issue.None) return;

            var key = $"{mode}:{issue}";
            if (!warned_issues.Add(key)) return;

            Debug.LogWarning($"YutrelRP DDGI probe debug '{mode}' cannot display because {GetIssueMessage(issue)}.");
        }

        private static string GetIssueMessage(Issue issue)
        {
            switch (issue)
            {
                case Issue.MissingVolume:
                    return "there is no valid active DDGI Volume or DDGI probe metadata";
                case Issue.MissingIrradiance:
                    return "DDGI ProbeIrradiance atlas is missing";
                case Issue.MissingDistance:
                    return "DDGI ProbeDistance atlas is missing";
                case Issue.MissingRayData:
                    return "DDGI ProbeRayData is missing";
                case Issue.MissingProbeData:
                    return "DDGI ProbeData atlas is missing";
                default:
                    return "the selected source is unavailable";
            }
        }

        private TextureHandle probe_ray_data;
        private TextureHandle probe_irradiance;
        private TextureHandle probe_distance;
        private TextureHandle probe_data;
        private YutrelRPSettings.DebugViewMode mode;
        private Issue issue;
        private bool reads_probe_ray_data;
        private bool reads_probe_irradiance;
        private bool reads_probe_distance;
        private bool reads_probe_data;
        private int instance_count;
        private Vector3Int probe_count;
        private float probe_radius;
        private float probe_ray_data_max_distance;
        private Vector4 probe_ray_data_dimensions;
        private Vector4 probe_irradiance_dimensions;
        private Vector4 probe_distance_dimensions;
        private Vector4 probe_data_dimensions;
        private Vector3 volume_min_ws;
        private Vector3 probe_spacing_ws;
        private float probe_relocation_enabled;

        private void Render(RasterGraphContext context)
        {
            if (material == null) return;

            property_block.Clear();
            property_block.SetInteger(debug_mode_ID, GetShaderDebugMode(mode));
            property_block.SetInteger(debug_issue_ID, (int)issue);
            property_block.SetVector(probe_count_ID, new Vector4(probe_count.x, probe_count.y, probe_count.z, 0.0f));
            property_block.SetFloat(probe_radius_ID, probe_radius);
            property_block.SetFloat(probe_ray_data_max_distance_ID, probe_ray_data_max_distance);
            property_block.SetVector(probe_ray_data_dimensions_ID, probe_ray_data_dimensions);
            property_block.SetVector(probe_irradiance_dimensions_ID, probe_irradiance_dimensions);
            property_block.SetVector(probe_distance_dimensions_ID, probe_distance_dimensions);
            property_block.SetVector(probe_data_dimensions_ID, probe_data_dimensions);
            property_block.SetVector(volume_min_ws_ID, volume_min_ws);
            property_block.SetVector(probe_spacing_ws_ID, probe_spacing_ws);
            property_block.SetFloat(probe_relocation_enabled_ID, probe_relocation_enabled);

            if (reads_probe_ray_data)
            {
                property_block.SetTexture(probe_ray_data_ID, probe_ray_data);
            }

            if (reads_probe_irradiance)
            {
                property_block.SetTexture(probe_irradiance_ID, probe_irradiance);
            }

            if (reads_probe_distance)
            {
                property_block.SetTexture(probe_distance_ID, probe_distance);
            }

            if (reads_probe_data)
            {
                property_block.SetTexture(probe_data_ID, probe_data);
            }

            if (issue != Issue.None)
            {
                context.cmd.DrawProcedural(Matrix4x4.identity, material, 1, MeshTopology.Triangles, 3, 1, property_block);
                return;
            }

            if (sphere_mesh == null || instance_count <= 0) return;
            context.cmd.DrawMeshInstancedProcedural(sphere_mesh, 0, material, 0, instance_count, property_block);
        }

        private static int GetShaderDebugMode(YutrelRPSettings.DebugViewMode mode)
        {
            switch (mode)
            {
                case YutrelRPSettings.DebugViewMode.DDGIProbeIrradianceScene:
                    return 1;
                case YutrelRPSettings.DebugViewMode.DDGIProbeRayDataQualityScene:
                    return 2;
                case YutrelRPSettings.DebugViewMode.DDGIProbeDistanceScene:
                    return 3;
                default:
                    return 0;
            }
        }

        private static Mesh CreateSphereMesh()
        {
            const int longitude_segments = 16;
            const int latitude_segments = 8;
            var vertices = new Vector3[(longitude_segments + 1) * (latitude_segments + 1)];
            var normals = new Vector3[vertices.Length];
            var triangles = new int[longitude_segments * latitude_segments * 6];

            var vertex_index = 0;
            for (var latitude = 0; latitude <= latitude_segments; latitude++)
            {
                var v = (float)latitude / latitude_segments;
                var theta = v * Mathf.PI;
                var sin_theta = Mathf.Sin(theta);
                var cos_theta = Mathf.Cos(theta);

                for (var longitude = 0; longitude <= longitude_segments; longitude++)
                {
                    var u = (float)longitude / longitude_segments;
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
            var row_stride = longitude_segments + 1;
            for (var latitude = 0; latitude < latitude_segments; latitude++)
            {
                for (var longitude = 0; longitude < longitude_segments; longitude++)
                {
                    var current = latitude * row_stride + longitude;
                    var next = current + row_stride;

                    triangles[triangle_index++] = current;
                    triangles[triangle_index++] = current + 1;
                    triangles[triangle_index++] = next;
                    triangles[triangle_index++] = current + 1;
                    triangles[triangle_index++] = next + 1;
                    triangles[triangle_index++] = next;
                }
            }

            var mesh = new Mesh
            {
                name = "Yutrel DDGI Probe Debug Sphere",
                vertices = vertices,
                normals = normals,
                triangles = triangles
            };
            mesh.RecalculateBounds();
            mesh.UploadMeshData(true);
            return mesh;
        }

        public static void Cleanup()
        {
            CoreUtils.Destroy(material);
            CoreUtils.Destroy(sphere_mesh);
            material = null;
            sphere_mesh = null;
            property_block = null;
            warned_issues.Clear();
        }

        private enum Issue
        {
            None = 0,
            MissingVolume = 1,
            MissingIrradiance = 2,
            MissingDistance = 3,
            MissingRayData = 4,
            MissingProbeData = 5
        }
    }
}
#endif
