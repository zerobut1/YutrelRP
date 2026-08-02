using System;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace YutrelRP.Tests
{
    public sealed class YutrelRPPackageTests
    {
        private const string PackageRoot = "Packages/com.yutrel.render-pipelines.yutrel/";
        private const string GlobalSettingsPath = "Assets/Settings/YutrelRPGlobalSettings.asset";
        private const string MigrationTestRoot = "Assets/YutrelRPRendererMigrationTests";

        [Test]
        public void NewPipelineAsset_HasSafeDefaultsAndShaderTag()
        {
            var asset = ScriptableObject.CreateInstance<YutrelRPAsset>();
            var rendererData = ScriptableObject.CreateInstance<YutrelDeferredRendererData>();
            try
            {
                asset.Initialize(true, rendererData);

                Assert.That(rendererData.Settings, Is.Not.Null);
                Assert.That(rendererData.Settings.shadowSettings, Is.Not.Null);
                Assert.That(rendererData.Settings.ambientOcclusionSettings, Is.Not.Null);
                Assert.That(rendererData.Settings.ddgiSettings, Is.Not.Null);
                Assert.That(rendererData.Settings.ddgiSettings.enabled, Is.False);
                Assert.That(asset.RendererDataList.Count, Is.EqualTo(1));
                Assert.That(asset.DefaultRendererIndex, Is.Zero);
                Assert.That(asset.renderPipelineShaderTag, Is.EqualTo(YutrelRP.ShaderTagName));
                Assert.That(asset.renderPipelineShaderTag, Is.EqualTo("YutrelPipeline"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(asset);
                UnityEngine.Object.DestroyImmediate(rendererData);
            }
        }

        [Test]
        public void RendererOutput_AllowsOptionalDepthButRequiresColor()
        {
            var renderGraph = new RenderGraph("Yutrel Renderer Output Test");
            try
            {
                var color = renderGraph.CreateTexture(new TextureDesc(1, 1)
                {
                    colorFormat = GraphicsFormat.R16G16B16A16_SFloat,
                    name = "Test Scene Color"
                });
                var depth = renderGraph.CreateTexture(new TextureDesc(1, 1)
                {
                    colorFormat = GraphicsFormat.D16_UNorm,
                    name = "Test Scene Depth"
                });

                Assert.That(new YutrelRendererOutput(color).isValid, Is.True);
                Assert.That(new YutrelRendererOutput(color, depth).isValid, Is.True);
                Assert.That(new YutrelRendererOutput(default, depth).isValid, Is.False);
            }
            finally
            {
                renderGraph.Cleanup();
            }
        }

        [Test]
        public void Renderer_IsCachedAndRecreatedWhenRendererDataIsDirty()
        {
            var asset = ScriptableObject.CreateInstance<YutrelRPAsset>();
            var rendererData = ScriptableObject.CreateInstance<FakeRendererData>();
            try
            {
                asset.Initialize(true, rendererData);

                var first = (FakeRenderer)asset.GetRenderer(-1);
                Assert.That(asset.GetRenderer(-1), Is.SameAs(first));
                Assert.That(rendererData.CreateCount, Is.EqualTo(1));

                rendererData.SetDirty();
                var second = (FakeRenderer)asset.GetRenderer(-1);

                Assert.That(second, Is.Not.SameAs(first));
                Assert.That(first.IsDisposed, Is.True);
                Assert.That(rendererData.CreateCount, Is.EqualTo(2));

                asset.DestroyRenderers();
                Assert.That(second.IsDisposed, Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(asset);
                UnityEngine.Object.DestroyImmediate(rendererData);
            }
        }

        [Test]
        public void GameCamera_CanOverrideRendererAndInvalidIndexFallsBackToDefault()
        {
            var asset = ScriptableObject.CreateInstance<YutrelRPAsset>();
            var defaultData = ScriptableObject.CreateInstance<FakeRendererData>();
            var overrideData = ScriptableObject.CreateInstance<FakeRendererData>();
            var cameraObject = new GameObject("Yutrel Camera Selection Test");
            var camera = cameraObject.AddComponent<Camera>();
            var cameraData = cameraObject.AddComponent<YutrelAdditionalCameraData>();

            try
            {
                asset.Initialize(true, new YutrelRendererData[] { defaultData, overrideData }, 0);

                cameraData.SetRenderer(1);
                Assert.That(asset.GetRenderer(camera), Is.SameAs(asset.GetRenderer(1)));

                cameraData.SetRenderer(99);
                Assert.That(asset.GetRenderer(camera), Is.SameAs(asset.GetRenderer(0)));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(cameraObject);
                UnityEngine.Object.DestroyImmediate(asset);
                UnityEngine.Object.DestroyImmediate(defaultData);
                UnityEngine.Object.DestroyImmediate(overrideData);
            }
        }

        [Test]
        public void MissingDefaultRenderer_IsRejected()
        {
            var asset = ScriptableObject.CreateInstance<YutrelRPAsset>();
            try
            {
                asset.Initialize(true, new YutrelRendererData[] { null }, 0);
                Assert.That(asset.ValidateRendererData(-1), Is.False);
                Assert.That(asset.GetRenderer(-1), Is.Null);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(asset);
            }
        }

        [Test]
        public void LegacyPipelineAsset_IsMigratedOnceWithoutChangingGuidOrSettings()
        {
            AssetDatabase.DeleteAsset(MigrationTestRoot);
            AssetDatabase.CreateFolder("Assets", "YutrelRPRendererMigrationTests");

            var assetPath = $"{MigrationTestRoot}/LegacyPipeline.asset";
            var asset = ScriptableObject.CreateInstance<YutrelRPAsset>();
#pragma warning disable 618
            var legacySettings = new YutrelRPSettings
            {
                useSRPBatcher = false,
                shadowSettings = new ShadowSettings { max_distance = 37.0f },
                ambientOcclusionSettings = new AmbientOcclusionSettings
                {
                    mode = AmbientOcclusionSettings.Mode.GTAO
                },
                ddgiSettings = new YutrelDeferredRendererSettings.DDGISettings
                {
                    enabled = true
                }
            };
#pragma warning restore 618

            try
            {
                asset.SetLegacySettingsForMigration(legacySettings);
                AssetDatabase.CreateAsset(asset, assetPath);
                AssetDatabase.SaveAssets();
                var guidBefore = AssetDatabase.AssetPathToGUID(assetPath);

                Assert.That(global::YutrelRP.Editor.YutrelRPAssetMigration.UpgradeAsset(asset), Is.True);
                AssetDatabase.SaveAssets();

                Assert.That(AssetDatabase.AssetPathToGUID(assetPath), Is.EqualTo(guidBefore));
                Assert.That(asset.NeedsMigration, Is.False);
                Assert.That(asset.UseSRPBatcher, Is.False);
                Assert.That(asset.RendererDataList.Count, Is.EqualTo(1));
                Assert.That(asset.RendererDataList[0], Is.TypeOf<YutrelDeferredRendererData>());

                var deferredData = (YutrelDeferredRendererData)asset.RendererDataList[0];
                Assert.That(deferredData.Settings.shadowSettings.max_distance, Is.EqualTo(37.0f));
                Assert.That(deferredData.Settings.ambientOcclusionSettings.mode,
                    Is.EqualTo(AmbientOcclusionSettings.Mode.GTAO));
                Assert.That(deferredData.Settings.ddgiSettings.enabled, Is.True);
                Assert.That(global::YutrelRP.Editor.YutrelRPAssetMigration.UpgradeAsset(asset), Is.False);
                Assert.That(AssetDatabase.FindAssets("t:YutrelDeferredRendererData", new[] { MigrationTestRoot }),
                    Has.Length.EqualTo(1));
            }
            finally
            {
                AssetDatabase.DeleteAsset(MigrationTestRoot);
                AssetDatabase.Refresh();
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

        public sealed class FakeRendererData : YutrelRendererData
        {
            public int CreateCount { get; private set; }

            protected override YutrelRenderer CreateRenderer()
            {
                ++CreateCount;
                return new FakeRenderer();
            }
        }

        public sealed class FakeRenderer : YutrelRenderer
        {
            public bool IsDisposed { get; private set; }

            protected override YutrelRendererOutput RecordScene(
                RenderGraph renderGraph,
                in YutrelCameraRenderContext context)
            {
                return new YutrelRendererOutput(default);
            }

            protected override void Dispose(bool disposing)
            {
                IsDisposed = true;
                base.Dispose(disposing);
            }
        }
    }
}
