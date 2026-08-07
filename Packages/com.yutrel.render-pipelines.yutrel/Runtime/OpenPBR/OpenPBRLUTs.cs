using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace YutrelRP
{
    /// <summary>
    /// Owns the four OpenPBR energy-compensation LUT textures used by the deferred
    /// directional, environment, and DDGI lighting passes.
    ///
    /// Data comes from <see cref="OpenPBRLUTData"/> (copied from YutrelRender, originally
    /// Adobe openpbr-bsdf commit 8a20d6f9, Apache-2.0). The arrays are uploaded without
    /// any transposition: Unity's 3D texture linear order (x fastest) matches the
    /// reference [ior][alpha][cos_theta] flattening, and 2D order matches [x][y].
    ///
    /// Sampling convention (must match OpenPBR.hlsl):
    ///   - OpaqueDielectricEnergyComplement: 3D 32^3, uvw = (cos_theta, alpha, ior) remapped to texel centers
    ///   - OpaqueDielectricAverageEnergyComplement: 2D 32^2, uv = (alpha, ior)
    ///   - IdealMetalEnergyComplement: 2D 32^2, uv = (alpha, cos_theta)
    ///   - IdealMetalAverageEnergyComplement: 2D 32x1, uv = (alpha, 0.5)
    /// All textures: R16_UNorm, Bilinear, Clamp.
    /// </summary>
    internal static class OpenPBRLUTs
    {
        private const int TableSize = 32;

        private static readonly int
            opaque_dielectric_energy_ID = Shader.PropertyToID("_OpenPBR_OpaqueDielectricEnergy"),
            opaque_dielectric_average_ID = Shader.PropertyToID("_OpenPBR_OpaqueDielectricAverage"),
            ideal_metal_energy_ID = Shader.PropertyToID("_OpenPBR_IdealMetalEnergy"),
            ideal_metal_average_ID = Shader.PropertyToID("_OpenPBR_IdealMetalAverage");

        private static RTHandle s_opaque_dielectric_energy;
        private static RTHandle s_opaque_dielectric_average;
        private static RTHandle s_ideal_metal_energy;
        private static RTHandle s_ideal_metal_average;

        /// <summary>Idempotent; safe to call every frame from SetupPass.Record.</summary>
        public static void EnsureCreated()
        {
            if (HasValidTextures())
            {
                return;
            }

            Cleanup();

            if (!SystemInfo.SupportsTextureFormat(TextureFormat.R16))
            {
                // PC (DX11+) supports R16 including Texture3D. If a target ever lacks it,
                // fall back to RG16 or R32Float by duplicating/padding the data here.
                Debug.LogError("YutrelRP: OpenPBR LUTs require TextureFormat.R16 support.");
                return;
            }

            s_opaque_dielectric_energy = RTHandles.Alloc(CreateTexture3D(
                OpenPBRLUTData.OpaqueDielectricEnergyComplement,
                "OpenPBR_OpaqueDielectricEnergy"));
            s_opaque_dielectric_average = RTHandles.Alloc(CreateTexture2D(
                OpenPBRLUTData.OpaqueDielectricAverageEnergyComplement, TableSize, TableSize,
                "OpenPBR_OpaqueDielectricAverage"));
            s_ideal_metal_energy = RTHandles.Alloc(CreateTexture2D(
                OpenPBRLUTData.IdealMetalEnergyComplement, TableSize, TableSize,
                "OpenPBR_IdealMetalEnergy"));
            s_ideal_metal_average = RTHandles.Alloc(CreateTexture2D(
                OpenPBRLUTData.IdealMetalAverageEnergyComplement, TableSize, 1,
                "OpenPBR_IdealMetalAverage"));
        }

        /// <summary>
        /// Registers the LUTs every frame because loading RenderDoc recreates the graphics
        /// device and clears global shader bindings without reloading managed statics.
        /// </summary>
        public static void RegisterGlobals(RenderGraph render_graph, IBaseRenderGraphBuilder builder)
        {
            if (!HasValidTextures())
            {
                return;
            }

            builder.SetGlobalTextureAfterPass(
                render_graph.ImportTexture(s_opaque_dielectric_energy), opaque_dielectric_energy_ID);
            builder.SetGlobalTextureAfterPass(
                render_graph.ImportTexture(s_opaque_dielectric_average), opaque_dielectric_average_ID);
            builder.SetGlobalTextureAfterPass(
                render_graph.ImportTexture(s_ideal_metal_energy), ideal_metal_energy_ID);
            builder.SetGlobalTextureAfterPass(
                render_graph.ImportTexture(s_ideal_metal_average), ideal_metal_average_ID);
        }

        public static void UseGlobals(IBaseRenderGraphBuilder builder)
        {
            if (!HasValidTextures())
            {
                return;
            }

            builder.UseGlobalTexture(opaque_dielectric_energy_ID);
            builder.UseGlobalTexture(opaque_dielectric_average_ID);
            builder.UseGlobalTexture(ideal_metal_energy_ID);
            builder.UseGlobalTexture(ideal_metal_average_ID);
        }

        public static void Cleanup()
        {
            Release(ref s_opaque_dielectric_energy);
            Release(ref s_opaque_dielectric_average);
            Release(ref s_ideal_metal_energy);
            Release(ref s_ideal_metal_average);
        }

        private static bool HasValidTextures()
        {
            return IsValid(s_opaque_dielectric_energy) &&
                   IsValid(s_opaque_dielectric_average) &&
                   IsValid(s_ideal_metal_energy) &&
                   IsValid(s_ideal_metal_average);
        }

        private static bool IsValid(RTHandle handle)
        {
            return handle != null && handle.externalTexture != null;
        }

        private static void Release(ref RTHandle handle)
        {
            if (handle == null)
            {
                return;
            }

            var texture = handle.externalTexture;
            RTHandles.Release(handle);
            CoreUtils.Destroy(texture);
            handle = null;
        }

        private static Texture3D CreateTexture3D(ushort[] data, string name)
        {
            var texture = new Texture3D(TableSize, TableSize, TableSize, TextureFormat.R16, false)
            {
                name = name,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };
            texture.SetPixelData(data, 0);
            // Keep the tiny CPU copy so Unity can restore this procedural texture
            // after RenderDoc causes a graphics-device recreation.
            texture.Apply(false, false);
            return texture;
        }

        private static Texture2D CreateTexture2D(ushort[] data, int width, int height, string name)
        {
            var texture = new Texture2D(width, height, TextureFormat.R16, false, true)
            {
                name = name,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };
            texture.SetPixelData(data, 0);
            texture.Apply(false, false);
            return texture;
        }
    }
}
