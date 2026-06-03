#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace YutrelRP
{
    internal class DebugViewPass
    {
        private const string shader_name = "YutrelRP/DebugView";

        private static readonly ProfilingSampler sampler = new("Debug View Pass");
        private static readonly int debug_view_issue_ID = Shader.PropertyToID("_DebugViewIssue");
        private static readonly int debug_view_mode_ID = Shader.PropertyToID("_DebugViewMode");
        private static readonly int ddgi_probe_count_ID = Shader.PropertyToID("_DDGIProbeCount");
        private static readonly int ddgi_probe_ray_data_ID = DDGIResources.probe_ray_data_ID;
        private static readonly int ddgi_probe_ray_data_dimensions_ID = DDGIResources.probe_ray_data_dimensions_ID;
        private static readonly int ddgi_probe_ray_data_debug_slice_ID = DDGIResources.probe_ray_data_debug_slice_ID;
        private static readonly int ddgi_probe_ray_data_max_distance_ID = DDGIResources.probe_ray_data_max_distance_ID;
        private static readonly int ddgi_probe_irradiance_ID = DDGIResources.probe_irradiance_ID;
        private static readonly int ddgi_probe_irradiance_dimensions_ID = DDGIResources.probe_irradiance_dimensions_ID;
        private static readonly int ddgi_probe_irradiance_debug_slice_ID = DDGIResources.probe_irradiance_debug_slice_ID;
        private static readonly int ddgi_probe_distance_ID = DDGIResources.probe_distance_ID;
        private static readonly int ddgi_probe_distance_dimensions_ID = DDGIResources.probe_distance_dimensions_ID;
        private static readonly int ddgi_probe_distance_debug_slice_ID = DDGIResources.probe_distance_debug_slice_ID;
        private static readonly int ddgi_probe_data_ID = DDGIResources.probe_data_ID;
        private static readonly int ddgi_probe_data_dimensions_ID = DDGIResources.probe_data_dimensions_ID;
        private static readonly int ddgi_probe_data_debug_slice_ID = DDGIResources.probe_data_debug_slice_ID;
        private static readonly int ddgi_volume_min_ws_ID = DDGIResources.volume_min_ws_ID;
        private static readonly int ddgi_volume_max_ws_ID = DDGIResources.volume_max_ws_ID;
        private static readonly int ddgi_probe_spacing_ws_ID = DDGIResources.probe_spacing_ws_ID;
        private static readonly int ddgi_gather_valid_ID = DDGIResources.gather_valid_ID;
        private static readonly int ddgi_gather_fade_distance_ID = DDGIResources.gather_fade_distance_ID;
        private static readonly int ddgi_diffuse_intensity_ID = DDGIResources.diffuse_intensity_ID;
        private static readonly int directional_shadow_cascade_count_ID = ShadowResources.directional_cascade_count_ID;
        private static readonly int directional_shadow_distance_fade_ID = ShadowResources.directional_distance_fade_ID;
        private static MaterialPropertyBlock property_block;
        private static readonly HashSet<string> warned_issues = new();

        private static Material material;

        internal static void Record(RenderGraph render_graph, Camera camera, RenderTargets textures,
            LightResources light_resources, ShadowResources shadow_resources, ShadowSettings shadow_settings,
            DDGIResources ddgi_resources, YutrelRPSettings.DebugViewMode mode,
            YutrelRPSettings.DDGISettings ddgi_settings, Vector2Int attachment_size)
        {
            if (mode == YutrelRPSettings.DebugViewMode.Disabled || IsRayTracingSmokeTestMode(mode)) return;
            if (camera.cameraType != CameraType.SceneView && camera.cameraType != CameraType.Game) return;

            if (material == null) material = CoreUtils.CreateEngineMaterial(Shader.Find(shader_name));
            if (property_block == null) property_block = new MaterialPropertyBlock();

            using var builder = render_graph.AddRasterRenderPass<DebugViewPass>(sampler.name, out var pass, sampler);

            var debug_color_desc = new TextureDesc(attachment_size.x, attachment_size.y)
            {
                colorFormat = GraphicsFormatUtility.GetGraphicsFormat(RenderTextureFormat.Default, true),
                clearBuffer = true,
                clearColor = Color.black,
                name = "Debug View Color"
            };

            var debug_color = render_graph.CreateTexture(debug_color_desc);

            pass.mode = mode;
            pass.issue = ValidateSources(mode, light_resources, shadow_resources, shadow_settings, textures, ddgi_resources);
            pass.ddgi_probe_count = ddgi_resources != null ? ddgi_resources.probe_count : Vector3Int.one;
            pass.reads_GBuffer = pass.issue == Issue.None && IsGBufferMode(mode);
            pass.reads_scene_depth = pass.issue == Issue.None &&
                                     (mode == YutrelRPSettings.DebugViewMode.SceneDepth ||
                                      mode == YutrelRPSettings.DebugViewMode.AmbientOcclusion);
            pass.reads_screen_space_ao = pass.issue == Issue.None && mode == YutrelRPSettings.DebugViewMode.AmbientOcclusion;
            pass.reads_shadow_mask = pass.issue == Issue.None && mode == YutrelRPSettings.DebugViewMode.ShadowOnly;
            pass.reads_CSM = pass.issue == Issue.None && mode == YutrelRPSettings.DebugViewMode.CSMCascadeLevels;
            pass.reads_DDGI_probe_ray_data = pass.issue == Issue.None && mode == YutrelRPSettings.DebugViewMode.DDGIProbeRayData;
            pass.reads_DDGI_probe_irradiance = pass.issue == Issue.None && mode == YutrelRPSettings.DebugViewMode.DDGIProbeIrradianceAtlas;
            pass.reads_DDGI_probe_distance = pass.issue == Issue.None && mode == YutrelRPSettings.DebugViewMode.DDGIProbeDistanceAtlas;
            pass.reads_DDGI_probe_data = pass.issue == Issue.None && mode == YutrelRPSettings.DebugViewMode.DDGIProbeData;
            pass.reads_DDGI_gather = pass.issue == Issue.None &&
                                     (mode == YutrelRPSettings.DebugViewMode.DDGIDiffuseOnly ||
                                      mode == YutrelRPSettings.DebugViewMode.DDGICoverage);

            if (pass.reads_GBuffer)
            {
                pass.GBuffer_A = textures.GBuffer_A;
                pass.GBuffer_B = textures.GBuffer_B;
                pass.GBuffer_C = textures.GBuffer_C;
                builder.UseTexture(pass.GBuffer_A);
                builder.UseTexture(pass.GBuffer_B);
                builder.UseTexture(pass.GBuffer_C);
            }

            if (pass.reads_scene_depth)
            {
                pass.scene_depth = textures.scene_depth;
                builder.UseTexture(pass.scene_depth);
            }

            if (pass.reads_screen_space_ao)
            {
                pass.screen_space_ao = textures.screen_space_ao.IsValid()
                    ? textures.screen_space_ao
                    : render_graph.defaultResources.whiteTexture;
                builder.UseTexture(pass.screen_space_ao);
            }

            if (pass.reads_shadow_mask)
            {
                pass.shadow_mask = textures.shadow_mask;
                builder.UseTexture(pass.shadow_mask);
            }

            if (pass.reads_CSM)
            {
                pass.scene_depth = textures.scene_depth;
                pass.directional_shadow_vp_matrices_buffer = shadow_resources.directional_vp_matrices_buffer;
                pass.directional_shadow_cascade_data_buffer = shadow_resources.directional_cascade_data_buffer;
                pass.directional_shadow_cascade_count = shadow_settings.directional.cascade_count;
                pass.directional_shadow_distance_fade = new Vector4(
                    1.0f / shadow_settings.max_distance,
                    1.0f / shadow_settings.distance_fade,
                    1.0f / shadow_settings.directional.cascade_fade,
                    0.0f);

                builder.UseTexture(pass.scene_depth);
                builder.UseBuffer(pass.directional_shadow_vp_matrices_buffer);
                builder.UseBuffer(pass.directional_shadow_cascade_data_buffer);
            }

            if (pass.reads_DDGI_probe_ray_data)
            {
                pass.ddgi_probe_ray_data = ddgi_resources.probe_ray_data;
                pass.ddgi_probe_ray_data_dimensions = ddgi_resources.ProbeRayDataDimensions;
                pass.ddgi_probe_ray_data_debug_slice = Mathf.Clamp(
                    ddgi_settings != null ? ddgi_settings.debugProbeRayDataSlice : 0,
                    0,
                    Mathf.Max(0, ddgi_resources.probe_count.y - 1));
                pass.ddgi_probe_ray_data_max_distance = Mathf.Max(0.001f, ddgi_resources.probe_max_ray_distance);
                builder.UseTexture(pass.ddgi_probe_ray_data);
            }

            if (pass.reads_DDGI_probe_irradiance)
            {
                pass.ddgi_probe_irradiance = ddgi_resources.probe_irradiance;
                pass.ddgi_probe_irradiance_dimensions = ddgi_resources.ProbeIrradianceDimensions;
                pass.ddgi_probe_irradiance_debug_slice = Mathf.Clamp(
                    ddgi_settings != null ? ddgi_settings.debugProbeIrradianceAtlasSlice : 0,
                    0,
                    Mathf.Max(0, ddgi_resources.probe_count.y - 1));
                builder.UseTexture(pass.ddgi_probe_irradiance);
            }

            if (pass.reads_DDGI_probe_distance)
            {
                pass.ddgi_probe_distance = ddgi_resources.probe_distance;
                pass.ddgi_probe_distance_dimensions = ddgi_resources.ProbeDistanceDimensions;
                pass.ddgi_probe_distance_debug_slice = Mathf.Clamp(
                    ddgi_settings != null ? ddgi_settings.debugProbeDistanceAtlasSlice : 0,
                    0,
                    Mathf.Max(0, ddgi_resources.probe_count.y - 1));
                builder.UseTexture(pass.ddgi_probe_distance);
            }

            if (pass.reads_DDGI_probe_data)
            {
                pass.ddgi_probe_data = ddgi_resources.probe_data;
                pass.ddgi_probe_data_dimensions = ddgi_resources.ProbeDataDimensions;
                pass.ddgi_probe_data_debug_slice = Mathf.Clamp(
                    ddgi_settings != null ? ddgi_settings.debugProbeDataSlice : 0,
                    0,
                    Mathf.Max(0, ddgi_resources.probe_count.y - 1));
                builder.UseTexture(pass.ddgi_probe_data);
            }

            if (pass.reads_DDGI_gather)
            {
                pass.GBuffer_A = textures.GBuffer_A;
                pass.GBuffer_B = textures.GBuffer_B;
                pass.GBuffer_C = textures.GBuffer_C;
                pass.scene_depth = textures.scene_depth;
                pass.screen_space_ao = textures.screen_space_ao.IsValid()
                    ? textures.screen_space_ao
                    : render_graph.defaultResources.whiteTexture;
                pass.ddgi_probe_irradiance = ddgi_resources.probe_irradiance;
                pass.ddgi_probe_count = ddgi_resources.probe_count;
                pass.ddgi_probe_irradiance_dimensions = ddgi_resources.ProbeIrradianceDimensions;
                pass.ddgi_volume_min_ws = ddgi_resources.volume_min_ws;
                pass.ddgi_volume_max_ws = ddgi_resources.volume_max_ws;
                pass.ddgi_probe_spacing_ws = ddgi_resources.probe_spacing_ws;
                pass.ddgi_gather_fade_distance = ddgi_resources.gather_fade_distance;
                pass.ddgi_diffuse_intensity = Mathf.Max(0.0f, ddgi_settings != null ? ddgi_settings.diffuseIntensity : 1.0f);

                builder.UseTexture(pass.GBuffer_A);
                builder.UseTexture(pass.GBuffer_B);
                builder.UseTexture(pass.GBuffer_C);
                builder.UseTexture(pass.scene_depth);
                builder.UseTexture(pass.screen_space_ao);
                builder.UseTexture(pass.ddgi_probe_irradiance);
            }

            builder.SetRenderAttachment(debug_color, 0);
            builder.SetRenderFunc<DebugViewPass>(static (pass, context) => { pass.Render(context); });

            textures.final_color = debug_color;
        }

        private static bool IsRayTracingSmokeTestMode(YutrelRPSettings.DebugViewMode mode)
        {
            return mode == YutrelRPSettings.DebugViewMode.RayTracingSmokeTestRayGen ||
                   mode == YutrelRPSettings.DebugViewMode.RayTracingSmokeTestRTASHitMiss;
        }

        private static bool IsGBufferMode(YutrelRPSettings.DebugViewMode mode)
        {
            return mode == YutrelRPSettings.DebugViewMode.GBufferBaseColor ||
                   mode == YutrelRPSettings.DebugViewMode.GBufferRoughness ||
                   mode == YutrelRPSettings.DebugViewMode.GBufferMetallic ||
                   mode == YutrelRPSettings.DebugViewMode.GBufferWorldSpaceNormal ||
                   mode == YutrelRPSettings.DebugViewMode.GBufferSpecular ||
                   mode == YutrelRPSettings.DebugViewMode.AmbientOcclusion;
        }

        private static Issue ValidateSources(YutrelRPSettings.DebugViewMode mode, LightResources light_resources,
            ShadowResources shadow_resources, ShadowSettings shadow_settings, RenderTargets textures,
            DDGIResources ddgi_resources)
        {
            Issue issue = Issue.None;

            switch (mode)
            {
                case YutrelRPSettings.DebugViewMode.GBufferBaseColor:
                case YutrelRPSettings.DebugViewMode.GBufferRoughness:
                case YutrelRPSettings.DebugViewMode.GBufferMetallic:
                case YutrelRPSettings.DebugViewMode.GBufferWorldSpaceNormal:
                case YutrelRPSettings.DebugViewMode.GBufferSpecular:
                case YutrelRPSettings.DebugViewMode.AmbientOcclusion:
                    if (!textures.GBuffer_A.IsValid() || !textures.GBuffer_B.IsValid() || !textures.GBuffer_C.IsValid())
                    {
                        issue = Issue.MissingGBuffer;
                    }

                    if (issue == Issue.None && mode == YutrelRPSettings.DebugViewMode.AmbientOcclusion &&
                        !textures.scene_depth.IsValid())
                    {
                        issue = Issue.MissingSceneDepth;
                    }

                    break;
                case YutrelRPSettings.DebugViewMode.SceneDepth:
                    if (!textures.scene_depth.IsValid())
                    {
                        issue = Issue.MissingSceneDepth;
                    }

                    break;
                case YutrelRPSettings.DebugViewMode.ShadowOnly:
                    if (!textures.shadow_mask.IsValid())
                    {
                        issue = Issue.MissingShadowMask;
                    }

                    break;
                case YutrelRPSettings.DebugViewMode.CSMCascadeLevels:
                    if (light_resources.directional_light_count == 0 || shadow_resources.shadowed_directional_light_count == 0 ||
                        shadow_settings.directional.cascade_count <= 0)
                    {
                        issue = Issue.UnsupportedCSMSource;
                    }
                    else if (!textures.scene_depth.IsValid() || !shadow_resources.directional_vp_matrices_buffer.IsValid() ||
                             !shadow_resources.directional_cascade_data_buffer.IsValid())
                    {
                        issue = Issue.MissingCSMSource;
                    }

                    break;
                case YutrelRPSettings.DebugViewMode.DDGIProbeRayData:
                    if (ddgi_resources == null || !ddgi_resources.probe_ray_data.IsValid())
                    {
                        issue = Issue.MissingDDGIProbeRayData;
                    }

                    break;
                case YutrelRPSettings.DebugViewMode.DDGIProbeIrradianceAtlas:
                    if (ddgi_resources == null || !ddgi_resources.probe_irradiance.IsValid())
                    {
                        issue = Issue.MissingDDGIProbeIrradiance;
                    }

                    break;
                case YutrelRPSettings.DebugViewMode.DDGIProbeDistanceAtlas:
                    if (ddgi_resources == null || !ddgi_resources.probe_distance.IsValid())
                    {
                        issue = Issue.MissingDDGIProbeDistance;
                    }

                    break;
                case YutrelRPSettings.DebugViewMode.DDGIProbeData:
                    if (ddgi_resources == null || !ddgi_resources.probe_data.IsValid())
                    {
                        issue = Issue.MissingDDGIProbeData;
                    }

                    break;
                case YutrelRPSettings.DebugViewMode.DDGIDiffuseOnly:
                case YutrelRPSettings.DebugViewMode.DDGICoverage:
                    if (!textures.GBuffer_A.IsValid() || !textures.GBuffer_B.IsValid() ||
                        !textures.GBuffer_C.IsValid() || !textures.scene_depth.IsValid())
                    {
                        issue = Issue.MissingGBuffer;
                    }
                    else if (ddgi_resources == null || !ddgi_resources.has_gather_data ||
                             !ddgi_resources.probe_irradiance.IsValid())
                    {
                        issue = Issue.MissingDDGIProbeIrradiance;
                    }

                    break;
                default:
                    issue = Issue.UnsupportedMode;
                    break;
            }

            WarnOnce(mode, issue);
            return issue;
        }

        private static void WarnOnce(YutrelRPSettings.DebugViewMode mode, Issue issue)
        {
            if (issue == Issue.None) return;

            var key = $"{mode}:{issue}";
            if (!warned_issues.Add(key)) return;

            Debug.LogWarning($"YutrelRP DebugView '{mode}' cannot display because {GetIssueMessage(issue)}. Rendering magenta.");
        }

        private static string GetIssueMessage(Issue issue)
        {
            switch (issue)
            {
                case Issue.MissingGBuffer:
                    return "the GBuffer source is missing";
                case Issue.MissingSceneDepth:
                    return "the scene depth source is missing";
                case Issue.MissingDDGIProbeRayData:
                    return "DDGI ProbeRayData is missing; enable DDGI, add one active DDGI Volume, and enable Contribute GI on eligible opaque MeshRenderers";
                case Issue.MissingDDGIProbeIrradiance:
                    return "DDGI ProbeIrradiance atlas is missing; enable DDGI and use a valid active DDGI Volume";
                case Issue.MissingDDGIProbeDistance:
                    return "DDGI ProbeDistance atlas is missing; enable DDGI and use a valid active DDGI Volume";
                case Issue.MissingDDGIProbeData:
                    return "DDGI ProbeData atlas is missing; enable DDGI and use a valid active DDGI Volume";
                case Issue.MissingShadowMask:
                    return "the shadow mask source is missing";
                case Issue.UnsupportedCSMSource:
                    return "there is no shadowed directional light/CSM source";
                case Issue.MissingCSMSource:
                    return "CSM depth or cascade buffers are missing";
                default:
                    return "an unsupported source is selected";
            }
        }

        private TextureHandle GBuffer_A;
        private TextureHandle GBuffer_B;
        private TextureHandle GBuffer_C;
        private TextureHandle scene_depth;
        private TextureHandle screen_space_ao;
        private TextureHandle shadow_mask;
        private TextureHandle ddgi_probe_ray_data;
        private TextureHandle ddgi_probe_irradiance;
        private TextureHandle ddgi_probe_distance;
        private TextureHandle ddgi_probe_data;
        private BufferHandle directional_shadow_vp_matrices_buffer;
        private BufferHandle directional_shadow_cascade_data_buffer;
        private YutrelRPSettings.DebugViewMode mode;
        private Issue issue;
        private bool reads_GBuffer;
        private bool reads_scene_depth;
        private bool reads_screen_space_ao;
        private bool reads_shadow_mask;
        private bool reads_CSM;
        private bool reads_DDGI_probe_ray_data;
        private bool reads_DDGI_probe_irradiance;
        private bool reads_DDGI_probe_distance;
        private bool reads_DDGI_probe_data;
        private bool reads_DDGI_gather;
        private int directional_shadow_cascade_count;
        private Vector4 directional_shadow_distance_fade;
        private Vector4 ddgi_probe_ray_data_dimensions;
        private int ddgi_probe_ray_data_debug_slice;
        private float ddgi_probe_ray_data_max_distance;
        private Vector3Int ddgi_probe_count;
        private Vector4 ddgi_probe_irradiance_dimensions;
        private Vector4 ddgi_probe_distance_dimensions;
        private Vector4 ddgi_probe_data_dimensions;
        private int ddgi_probe_irradiance_debug_slice;
        private int ddgi_probe_distance_debug_slice;
        private int ddgi_probe_data_debug_slice;
        private Vector3 ddgi_volume_min_ws;
        private Vector3 ddgi_volume_max_ws;
        private Vector3 ddgi_probe_spacing_ws;
        private float ddgi_gather_fade_distance;
        private float ddgi_diffuse_intensity;

        private void Render(RasterGraphContext context)
        {
            var cmd = context.cmd;

            property_block.Clear();
            property_block.SetInteger(debug_view_mode_ID, (int)mode);
            property_block.SetInteger(debug_view_issue_ID, (int)issue);
            property_block.SetVector(ddgi_probe_count_ID, new Vector4(ddgi_probe_count.x, ddgi_probe_count.y, ddgi_probe_count.z, 0.0f));
            property_block.SetVector(ddgi_probe_ray_data_dimensions_ID, ddgi_probe_ray_data_dimensions);
            property_block.SetInteger(ddgi_probe_ray_data_debug_slice_ID, ddgi_probe_ray_data_debug_slice);
            property_block.SetFloat(ddgi_probe_ray_data_max_distance_ID, ddgi_probe_ray_data_max_distance);
            property_block.SetVector(ddgi_probe_irradiance_dimensions_ID, ddgi_probe_irradiance_dimensions);
            property_block.SetInteger(ddgi_probe_irradiance_debug_slice_ID, ddgi_probe_irradiance_debug_slice);
            property_block.SetVector(ddgi_probe_distance_dimensions_ID, ddgi_probe_distance_dimensions);
            property_block.SetInteger(ddgi_probe_distance_debug_slice_ID, ddgi_probe_distance_debug_slice);
            property_block.SetVector(ddgi_probe_data_dimensions_ID, ddgi_probe_data_dimensions);
            property_block.SetInteger(ddgi_probe_data_debug_slice_ID, ddgi_probe_data_debug_slice);
            property_block.SetVector(ddgi_volume_min_ws_ID, ddgi_volume_min_ws);
            property_block.SetVector(ddgi_volume_max_ws_ID, ddgi_volume_max_ws);
            property_block.SetVector(ddgi_probe_spacing_ws_ID, ddgi_probe_spacing_ws);
            property_block.SetFloat(ddgi_gather_valid_ID, reads_DDGI_gather ? 1.0f : 0.0f);
            property_block.SetFloat(ddgi_gather_fade_distance_ID, ddgi_gather_fade_distance);
            property_block.SetFloat(ddgi_diffuse_intensity_ID, ddgi_diffuse_intensity);
            property_block.SetInteger(directional_shadow_cascade_count_ID, directional_shadow_cascade_count);
            property_block.SetVector(directional_shadow_distance_fade_ID, directional_shadow_distance_fade);

            if (reads_GBuffer)
            {
                property_block.SetTexture(RenderTargets.GBuffer_A_ID, GBuffer_A);
                property_block.SetTexture(RenderTargets.GBuffer_B_ID, GBuffer_B);
                property_block.SetTexture(RenderTargets.GBuffer_C_ID, GBuffer_C);
            }

            if (reads_scene_depth)
            {
                property_block.SetTexture(RenderTargets.scene_depth_ID, scene_depth);
            }

            if (reads_screen_space_ao)
            {
                property_block.SetTexture(RenderTargets.screen_space_ao_ID, screen_space_ao);
            }

            if (reads_shadow_mask)
            {
                property_block.SetTexture(RenderTargets.shadow_mask_ID, shadow_mask);
            }

            if (reads_CSM)
            {
                property_block.SetTexture(RenderTargets.scene_depth_ID, scene_depth);
                property_block.SetBuffer(ShadowResources.directional_vp_matrices_ID, directional_shadow_vp_matrices_buffer);
                property_block.SetBuffer(ShadowResources.directional_cascade_data_ID, directional_shadow_cascade_data_buffer);
            }

            if (reads_DDGI_probe_ray_data)
            {
                property_block.SetTexture(ddgi_probe_ray_data_ID, ddgi_probe_ray_data);
            }

            if (reads_DDGI_probe_irradiance)
            {
                property_block.SetTexture(ddgi_probe_irradiance_ID, ddgi_probe_irradiance);
            }

            if (reads_DDGI_probe_distance)
            {
                property_block.SetTexture(ddgi_probe_distance_ID, ddgi_probe_distance);
            }

            if (reads_DDGI_probe_data)
            {
                property_block.SetTexture(ddgi_probe_data_ID, ddgi_probe_data);
            }

            if (reads_DDGI_gather)
            {
                property_block.SetTexture(RenderTargets.GBuffer_A_ID, GBuffer_A);
                property_block.SetTexture(RenderTargets.GBuffer_B_ID, GBuffer_B);
                property_block.SetTexture(RenderTargets.GBuffer_C_ID, GBuffer_C);
                property_block.SetTexture(RenderTargets.scene_depth_ID, scene_depth);
                property_block.SetTexture(RenderTargets.screen_space_ao_ID, screen_space_ao);
                property_block.SetTexture(ddgi_probe_irradiance_ID, ddgi_probe_irradiance);
            }

            CoreUtils.DrawFullScreen(cmd, material, property_block);
        }

        public static void Cleanup()
        {
            CoreUtils.Destroy(material);
            material = null;
            property_block = null;
            warned_issues.Clear();
        }

        private enum Issue
        {
            None = 0,
            MissingGBuffer = 1,
            MissingShadowMask = 2,
            UnsupportedCSMSource = 3,
            MissingCSMSource = 4,
            MissingSceneDepth = 5,
            UnsupportedMode = 6,
            MissingDDGIProbeRayData = 7,
            MissingDDGIProbeIrradiance = 8,
            MissingDDGIProbeDistance = 9,
            MissingDDGIProbeData = 10
        }
    }
}
#endif
