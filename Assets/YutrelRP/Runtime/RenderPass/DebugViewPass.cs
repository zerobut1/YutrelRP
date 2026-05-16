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
        private static readonly int directional_shadow_cascade_count_ID = ShadowResources.directional_cascade_count_ID;
        private static readonly int directional_shadow_distance_fade_ID = ShadowResources.directional_distance_fade_ID;
        private static MaterialPropertyBlock property_block;
        private static readonly HashSet<string> warned_issues = new();

        private static Material material;

        internal static void Record(RenderGraph render_graph, Camera camera, RenderTargets textures,
            LightResources light_resources, ShadowResources shadow_resources, ShadowSettings shadow_settings,
            YutrelRPSettings.DebugViewMode mode, Vector2Int attachment_size)
        {
            if (mode == YutrelRPSettings.DebugViewMode.Disabled) return;
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
            pass.issue = ValidateSources(mode, light_resources, shadow_resources, shadow_settings, textures);
            pass.reads_GBuffer = pass.issue == Issue.None && IsGBufferMode(mode);
            pass.reads_scene_depth = pass.issue == Issue.None && mode == YutrelRPSettings.DebugViewMode.SceneDepth;
            pass.reads_shadow_mask = pass.issue == Issue.None && mode == YutrelRPSettings.DebugViewMode.ShadowOnly;
            pass.reads_CSM = pass.issue == Issue.None && mode == YutrelRPSettings.DebugViewMode.CSMCascadeLevels;

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

            builder.SetRenderAttachment(debug_color, 0);
            builder.SetRenderFunc<DebugViewPass>(static (pass, context) => { pass.Render(context); });

            textures.final_color = debug_color;
        }

        private static bool IsGBufferMode(YutrelRPSettings.DebugViewMode mode)
        {
            return mode == YutrelRPSettings.DebugViewMode.GBufferBaseColor ||
                   mode == YutrelRPSettings.DebugViewMode.GBufferRoughness ||
                   mode == YutrelRPSettings.DebugViewMode.GBufferMetallic ||
                   mode == YutrelRPSettings.DebugViewMode.GBufferWorldSpaceNormal ||
                   mode == YutrelRPSettings.DebugViewMode.GBufferSpecular;
        }

        private static Issue ValidateSources(YutrelRPSettings.DebugViewMode mode, LightResources light_resources,
            ShadowResources shadow_resources, ShadowSettings shadow_settings, RenderTargets textures)
        {
            Issue issue = Issue.None;

            switch (mode)
            {
                case YutrelRPSettings.DebugViewMode.GBufferBaseColor:
                case YutrelRPSettings.DebugViewMode.GBufferRoughness:
                case YutrelRPSettings.DebugViewMode.GBufferMetallic:
                case YutrelRPSettings.DebugViewMode.GBufferWorldSpaceNormal:
                case YutrelRPSettings.DebugViewMode.GBufferSpecular:
                    if (!textures.GBuffer_A.IsValid() || !textures.GBuffer_B.IsValid() || !textures.GBuffer_C.IsValid())
                    {
                        issue = Issue.MissingGBuffer;
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
        private TextureHandle shadow_mask;
        private BufferHandle directional_shadow_vp_matrices_buffer;
        private BufferHandle directional_shadow_cascade_data_buffer;
        private YutrelRPSettings.DebugViewMode mode;
        private Issue issue;
        private bool reads_GBuffer;
        private bool reads_scene_depth;
        private bool reads_shadow_mask;
        private bool reads_CSM;
        private int directional_shadow_cascade_count;
        private Vector4 directional_shadow_distance_fade;

        private void Render(RasterGraphContext context)
        {
            var cmd = context.cmd;

            property_block.Clear();
            property_block.SetInteger(debug_view_mode_ID, (int)mode);
            property_block.SetInteger(debug_view_issue_ID, (int)issue);
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
            UnsupportedMode = 6
        }
    }
}
#endif
