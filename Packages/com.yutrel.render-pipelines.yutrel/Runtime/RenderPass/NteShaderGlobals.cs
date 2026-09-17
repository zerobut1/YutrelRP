using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace YutrelRP
{
    /// <summary>Immutable per-camera NTE bindings shared by Base, lighting and overlays.</summary>
    internal readonly struct NteShaderGlobals
    {
        private static readonly int[] shape_ids = PaletteIds("Shape");
        private static readonly int[] at_one_ids = PaletteIds("AtOne");
        private static readonly int[] at_zero_ids = PaletteIds("AtZero");
        private static readonly int common_endpoint_id = Shader.PropertyToID("_NtePaletteCommonEndpoint");
        private static readonly int rim_at_one_id = Shader.PropertyToID("_NteRimColorAtOne");
        private static readonly int rim_at_zero_id = Shader.PropertyToID("_NteRimColorAtZero");
        private static readonly int rim_width_id = Shader.PropertyToID("_NteRimWidth");
        private static readonly int output_scale_id = Shader.PropertyToID("_NteOutputScale");
        private static readonly int native_exposure_id = Shader.PropertyToID("_NteNativeExposure");
        private static readonly int main_light_direction_id = Shader.PropertyToID("_NteMainLightDirection");
        private static readonly int dither_phase_id = Shader.PropertyToID("_NteDitherPhase");

        private static readonly ProfilingSampler sampler = new("NTE Camera Globals");

        private readonly ResolvedNteSettings settings;
        private readonly float output_scale;
        private readonly Vector4 main_light_direction;
        private readonly float dither_phase;

        internal NteShaderGlobals(ResolvedNteSettings settings, LightResources lights,
            float current_pre_exposure, int frame_index)
        {
            this.settings = settings;
            output_scale = settings.GetOutputScale(current_pre_exposure);

            var direction = lights.directional_light_count > 0
                ? (Vector3)lights.directional_light_data[0].direction
                : ResolvedNteSettings.DefaultLightDirection;
            if (direction.sqrMagnitude < 1e-8f)
            {
                direction = ResolvedNteSettings.DefaultLightDirection;
            }
            direction.Normalize();
            main_light_direction = new Vector4(direction.x, direction.y, direction.z, 0.0f);
            dither_phase = Mathf.Abs(frame_index % 5);
        }

        internal void RecordPreparation(RenderGraph render_graph)
        {
            using var builder = render_graph.AddComputePass<PreparePass>(sampler.name, out var pass, sampler);
            pass.globals = this;
            builder.AllowPassCulling(false);
            builder.AllowGlobalStateModification(true);
            builder.SetRenderFunc<PreparePass>(static (data, context) => data.globals.Bind(context.cmd));
        }

        internal void Bind(IBaseCommandBuffer cmd)
        {
            SetPalette(cmd, 0, settings.palette0);
            SetPalette(cmd, 1, settings.palette1);
            SetPalette(cmd, 2, settings.palette2);
            SetPalette(cmd, 3, settings.palette3);
            SetPalette(cmd, 4, settings.palette4);
            cmd.SetGlobalVector(common_endpoint_id, settings.common_endpoint);
            cmd.SetGlobalVector(rim_at_one_id, settings.rim_color_at_one);
            cmd.SetGlobalVector(rim_at_zero_id, settings.rim_color_at_zero);
            cmd.SetGlobalFloat(rim_width_id, settings.rim_width);
            cmd.SetGlobalFloat(output_scale_id, output_scale);
            cmd.SetGlobalFloat(native_exposure_id, ResolvedNteSettings.NativeExposureInput);
            cmd.SetGlobalVector(main_light_direction_id, main_light_direction);
            cmd.SetGlobalFloat(dither_phase_id, dither_phase);
        }

        private void SetPalette(IBaseCommandBuffer cmd, int index, ResolvedNtePalette palette)
        {
            cmd.SetGlobalVector(shape_ids[index], palette.shape);
            cmd.SetGlobalVector(at_one_ids[index], palette.at_one);
            cmd.SetGlobalVector(at_zero_ids[index], palette.at_zero);
        }

        private static int[] PaletteIds(string suffix)
        {
            var result = new int[5];
            for (var i = 0; i < result.Length; ++i)
            {
                result[i] = Shader.PropertyToID($"_NtePalette{i}{suffix}");
            }
            return result;
        }

        private sealed class PreparePass
        {
            public NteShaderGlobals globals;
        }
    }
}
