using System;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace YutrelRP.Tests
{
    public sealed class YutrelRPPackageTests
    {
        private const string PackageRoot = "Packages/com.yutrel.render-pipelines.yutrel/";
        private const string GlobalSettingsPath = "Assets/Settings/YutrelRPGlobalSettings.asset";

        [Test]
        public void NewPipelineAsset_HasSafeDefaultsAndShaderTag()
        {
            var asset = ScriptableObject.CreateInstance<YutrelRPAsset>();
            try
            {
                Assert.That(asset.Settings, Is.Not.Null);
                Assert.That(asset.Settings.ddgiSettings, Is.Not.Null);
                Assert.That(asset.Settings.ddgiSettings.enabled, Is.False);
                Assert.That(asset.renderPipelineShaderTag, Is.EqualTo(YutrelRP.ShaderTagName));
                Assert.That(asset.renderPipelineShaderTag, Is.EqualTo("YutrelPipeline"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(asset);
            }
        }

        [Test]
        public void GlobalSettings_AreCreatedRegisteredAndPopulated()
        {
            var globalSettings = YutrelRPGlobalSettings.Ensure();

            Assert.That(globalSettings, Is.Not.Null);
            Assert.That(AssetDatabase.GetAssetPath(globalSettings), Is.EqualTo(GlobalSettingsPath));
            Assert.That(GraphicsSettings.GetSettingsForRenderPipeline<YutrelRP>(), Is.SameAs(globalSettings));

            Assert.That(GraphicsSettings.TryGetRenderPipelineSettings(out YutrelRPRuntimeShaders shaders), Is.True);
            Assert.That(shaders.directional_light_pass, Is.Not.Null);
            Assert.That(shaders.environment_lighting_pass, Is.Not.Null);
            Assert.That(shaders.skybox_pass, Is.Not.Null);
            Assert.That(shaders.shadow_mask_pass, Is.Not.Null);
            Assert.That(shaders.tone_mapping, Is.Not.Null);
            Assert.That(shaders.debug_view, Is.Not.Null);
            Assert.That(shaders.ssao_shader, Is.Not.Null);
            Assert.That(shaders.hbao_shader, Is.Not.Null);
            Assert.That(shaders.gtao_shader, Is.Not.Null);

            Assert.That(GraphicsSettings.TryGetRenderPipelineSettings(out YutrelDDGIShaderResources ddgi), Is.True);
            Assert.That(ddgi.probe_trace_ray_tracing, Is.Not.Null);
            Assert.That(ddgi.fullscreen_trace_radiance, Is.Not.Null);
            Assert.That(ddgi.probe_blending, Is.Not.Null);
            Assert.That(ddgi.probe_relocation, Is.Not.Null);
            Assert.That(ddgi.probe_classification, Is.Not.Null);
            Assert.That(ddgi.debug, Is.Not.Null);
            Assert.That(ddgi.probe_debug, Is.Not.Null);
            Assert.That(ddgi.lighting, Is.Not.Null);

            Assert.That(GraphicsSettings.TryGetRenderPipelineSettings(out YutrelRPRuntimeTextures textures), Is.True);
            Assert.That(textures.dfg_lut, Is.Not.Null);
        }

        [Test]
        public void PackageAssets_DoNotDependOnHostAssets()
        {
            var packageAssets = AssetDatabase.GetAllAssetPaths()
                .Where(path => path.StartsWith(PackageRoot, StringComparison.Ordinal))
                .ToArray();

            Assert.That(packageAssets, Is.Not.Empty);
            foreach (var assetPath in packageAssets)
            {
                foreach (var dependency in AssetDatabase.GetDependencies(assetPath, true))
                {
                    Assert.That(
                        dependency.StartsWith("Assets/", StringComparison.Ordinal),
                        Is.False,
                        $"Package asset '{assetPath}' depends on host asset '{dependency}'.");
                }
            }
        }
    }
}
