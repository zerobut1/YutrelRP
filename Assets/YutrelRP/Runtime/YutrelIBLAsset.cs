using UnityEngine;
using UnityEngine.Rendering;

namespace YutrelRP
{
    [CreateAssetMenu(menuName = "YutrelRP/IBL Asset")]
    public class YutrelIBLAsset : ScriptableObject
    {
        public const int diffuseIrradianceShCoefficientCount = 9;

        public Texture sourceEnvironmentTexture;
        public string sourceEnvironmentTexturePath;

        public string outputRootPath;
        public string specularDirectoryPath;
        public string specularCubemapPath;
        public string diffuseShPath;
        public string dfgLutPath;

        public Cubemap specularCubemap;
        [HideInInspector]
        public Texture2D[] specularFaceTextures;
        [HideInInspector]
        public string[] specularFacePaths;
        [HideInInspector]
        public TextAsset diffuseShText;
        public Vector3[] diffuseIrradianceSh;
        public Texture2D dfgLut;

        public int cubemapSize;
        public int specularMipCount;
        public int sampleCount;
        public int dfgLutSize;
        public string outputFormat;
        public string dfgMode;
        public string dfgFormat;
        public string shConvention;
        public string specularConvention;
        public float iblRoughnessOneLevel;
        public string iblIntensityConvention;

        public string cmgenExecutablePath;
        public string generatedAtUtc;

        public bool HasCompleteData =>
            specularCubemap != null &&
            dfgLut != null &&
            diffuseIrradianceSh != null &&
            diffuseIrradianceSh.Length >= diffuseIrradianceShCoefficientCount;

        public float IblRoughnessOneLevel =>
            iblRoughnessOneLevel > 0.0f ? iblRoughnessOneLevel : Mathf.Max(0, specularMipCount - 1);

        public bool TryGetDiffuseIrradianceSh(out SphericalHarmonicsL2 sh)
        {
            sh = default;
            if (diffuseIrradianceSh == null ||
                diffuseIrradianceSh.Length < diffuseIrradianceShCoefficientCount)
            {
                return false;
            }

            for (var coefficient = 0; coefficient < diffuseIrradianceShCoefficientCount; coefficient++)
            {
                var value = diffuseIrradianceSh[coefficient];
                sh[0, coefficient] = value.x;
                sh[1, coefficient] = value.y;
                sh[2, coefficient] = value.z;
            }

            return true;
        }
    }
}
