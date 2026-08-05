using UnityEngine;

namespace YutrelRP
{
    /// <summary>
    /// Owns the four OpenPBR energy-compensation LUT textures used by the deferred
    /// lighting passes (directional light, later environment/DDGI).
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

        private static Texture3D s_opaque_dielectric_energy;
        private static Texture2D s_opaque_dielectric_average;
        private static Texture2D s_ideal_metal_energy;
        private static Texture2D s_ideal_metal_average;
        private static bool s_created;

        /// <summary>Idempotent; safe to call every frame from SetupPass.Record.</summary>
        public static void EnsureCreated()
        {
            if (s_created)
            {
                return;
            }

            if (!SystemInfo.SupportsTextureFormat(TextureFormat.R16))
            {
                // PC (DX11+) supports R16 including Texture3D. If a target ever lacks it,
                // fall back to RG16 or R32Float by duplicating/padding the data here.
                Debug.LogError("YutrelRP: OpenPBR LUTs require TextureFormat.R16 support.");
                return;
            }

            s_opaque_dielectric_energy = CreateTexture3D(
                OpenPBRLUTData.OpaqueDielectricEnergyComplement,
                "OpenPBR_OpaqueDielectricEnergy");
            s_opaque_dielectric_average = CreateTexture2D(
                OpenPBRLUTData.OpaqueDielectricAverageEnergyComplement, TableSize, TableSize,
                "OpenPBR_OpaqueDielectricAverage");
            s_ideal_metal_energy = CreateTexture2D(
                OpenPBRLUTData.IdealMetalEnergyComplement, TableSize, TableSize,
                "OpenPBR_IdealMetalEnergy");
            s_ideal_metal_average = CreateTexture2D(
                OpenPBRLUTData.IdealMetalAverageEnergyComplement, TableSize, 1,
                "OpenPBR_IdealMetalAverage");

            Shader.SetGlobalTexture(opaque_dielectric_energy_ID, s_opaque_dielectric_energy);
            Shader.SetGlobalTexture(opaque_dielectric_average_ID, s_opaque_dielectric_average);
            Shader.SetGlobalTexture(ideal_metal_energy_ID, s_ideal_metal_energy);
            Shader.SetGlobalTexture(ideal_metal_average_ID, s_ideal_metal_average);

            s_created = true;
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
            texture.Apply(false, true);
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
            texture.Apply(false, true);
            return texture;
        }
    }
}
