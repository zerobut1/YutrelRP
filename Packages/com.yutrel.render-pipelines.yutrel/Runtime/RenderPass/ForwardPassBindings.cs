using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace YutrelRP
{
    // Per-camera snapshot: each forward pass declares and binds everything it reads.
    internal readonly struct ForwardPassBindings
    {
        private static readonly int light_count_ID = Shader.PropertyToID("_DirectionalLightCount");
        private static readonly int pre_exposure_ID = Shader.PropertyToID("_PreExposure");
        private static readonly int inverse_pre_exposure_ID = Shader.PropertyToID("_OneOverPreExposure");

        private readonly int light_count;
        private readonly BufferHandle light_data;
        private readonly TextureHandle shadow_mask, screen_space_ao, white_texture;
        private readonly TextureHandle environment_cube;
        private readonly Vector4 environment_cube_hdr;
        private readonly float ibl_roughness_one_level;
        private readonly bool environment_available;
        private readonly EndfieldShaderGlobals endfield_globals;
        private readonly DirectionalShadowBindings shadows;
        private readonly float pre_exposure;
        private readonly bool ao_available;

        internal ForwardPassBindings(RenderGraph render_graph, RenderTargets textures,
            LightResources lights, DirectionalShadowBindings shadows, EndfieldShaderGlobals endfield_globals, float pre_exposure)
        {
            light_count = lights.directional_light_count;
            light_data = lights.directional_light_data_buffer;
            white_texture = render_graph.defaultResources.whiteTexture;
            shadow_mask = light_count > 0 && textures.shadow_mask.IsValid()
                ? textures.shadow_mask : white_texture;
            ao_available = textures.screen_space_ao.IsValid();
            screen_space_ao = ao_available ? textures.screen_space_ao : white_texture;
            this.shadows = shadows;
            this.endfield_globals = endfield_globals;
            this.pre_exposure = pre_exposure;
            environment_available = lights.has_environment_reflection && lights.environment_reflection_cube.IsValid();
            environment_cube = environment_available ? lights.environment_reflection_cube : white_texture;
            environment_cube_hdr = environment_available ? lights.environment_reflection_cube_hdr : Vector4.zero;
            ibl_roughness_one_level = environment_available ? lights.ibl_roughness_one_level : 0.0f;
        }

        internal void DeclareResources(IBaseRenderGraphBuilder builder, bool use_screen_space_ao, bool transparent = false)
        {
            if (transparent) shadows.DeclareResources(builder);
            // LightResources always allocates this camera's buffer, including zero-light cameras.
            builder.UseBuffer(light_data);
            builder.UseTexture(shadow_mask);
            builder.UseTexture(use_screen_space_ao ? screen_space_ao : white_texture);
            builder.UseTexture(environment_cube);
        }

        private class PreparePass
        {
            public ForwardPassBindings bindings;
            public bool use_screen_space_ao, transparent;
        }

        private static readonly ProfilingSampler opaque_sampler = new("Opaque Forward Bindings");
        private static readonly ProfilingSampler transparent_sampler = new("Transparent Forward Bindings");

        internal void RecordPreparation(RenderGraph render_graph, bool use_screen_space_ao, bool transparent)
        {
            // Global state changes cannot be culled. Keep them outside the renderer-list pass.
            var sampler = transparent ? transparent_sampler : opaque_sampler;
            using var builder = render_graph.AddComputePass<PreparePass>(sampler.name, out var data, sampler);
            data.bindings = this;
            data.use_screen_space_ao = use_screen_space_ao;
            data.transparent = transparent;
            DeclareResources(builder, use_screen_space_ao, transparent);
            builder.AllowGlobalStateModification(true);
            builder.SetRenderFunc<PreparePass>(static (pass, context) =>
                pass.bindings.Bind(context.cmd, pass.use_screen_space_ao, pass.transparent));
        }

        private void Bind(IBaseCommandBuffer cmd, bool use_screen_space_ao, bool transparent)
        {
            if (transparent) shadows.BindGlobals(cmd);
            cmd.SetGlobalInt(light_count_ID, light_count);
            cmd.SetGlobalBuffer(LightResources.directional_light_data_ID, light_data);
            cmd.SetGlobalTexture(RenderTargets.shadow_mask_ID, shadow_mask);
            cmd.SetGlobalTexture(RenderTargets.screen_space_ao_ID,
                use_screen_space_ao ? screen_space_ao : white_texture);
            cmd.SetGlobalTexture(LightResources.environment_reflection_cube_ID, environment_cube);
            cmd.SetGlobalVector(LightResources.environment_reflection_cube_hdr_ID, environment_cube_hdr);
            cmd.SetGlobalFloat(LightResources.ibl_roughness_one_level_ID, ibl_roughness_one_level);
            cmd.SetGlobalFloat(pre_exposure_ID, pre_exposure);
            cmd.SetGlobalFloat(inverse_pre_exposure_ID, 1.0f / pre_exposure);
            endfield_globals.Bind(cmd, use_screen_space_ao && ao_available);
        }
    }
}
