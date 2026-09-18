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
        public void ExposureCompensation_PositiveValueBrightensOneStop()
        {
            var baseline = ExposureSettings.Default;
            var brighter = baseline;
            brighter.exposureCompensation = 1.0f;

            Assert.That(baseline.pre_exposure, Is.EqualTo(1.0f / (1.2f * 16384.0f)).Within(1e-9f));
            Assert.That(brighter.pre_exposure, Is.EqualTo(baseline.pre_exposure * 2.0f).Within(1e-9f));
        }

        [Test]
        public void PhotometricColor_NormalizesLinearSrgbToUnitLuminance()
        {
            var normalized = PhotometricColor.NormalizeLinearSrgb(new Color(1.0f, 0.0f, 0.0f));
            var luminance = Vector3.Dot(normalized, new Vector3(0.2126f, 0.7152f, 0.0722f));

            Assert.That(luminance, Is.EqualTo(1.0f).Within(1e-5f));
            Assert.That(PhotometricColor.NormalizeLinearSrgb(Color.black), Is.EqualTo(Vector3.zero));
        }

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
        public void NteRendererSettings_DefaultToPackageShaderAndNoDepthPrepass()
        {
            var settings = new YutrelDeferredRendererSettings();

            Assert.That(settings.enableDepthPrepass, Is.False);
            Assert.That(settings.directionalLightShaderOverride, Is.Null);
            Assert.That(settings.toneMappingShaderOverride, Is.Null);
        }

        [Test]
        public void NteDefaults_MatchNanallyPaletteAndExposureConvention()
        {
            var settings = ResolvedNteSettings.Default;
            var palettes = new[]
            {
                settings.palette0, settings.palette1, settings.palette2,
                settings.palette3, settings.palette4
            };

            Assert.That(palettes, Has.Length.EqualTo(5));
            Assert.That(palettes[0].at_one, Is.EqualTo(new Color(0.527845f, 0.498751f, 0.63f, 1.0f)));
            Assert.That(palettes[4].at_zero, Is.EqualTo(new Color(0.55053f, 0.586263f, 0.713542f, 1.0f)));
            Assert.That(settings.common_endpoint, Is.EqualTo(new Color(1.01f, 1.01f, 1.01f, 1.0f)));
            Assert.That(settings.rim_color_at_one,
                Is.EqualTo(new Color(0.018112f, 0.020736f, 0.03125f, 1.0f)));
            Assert.That(settings.rim_color_at_zero, Is.EqualTo(Color.black));
            Assert.That(settings.rim_width, Is.EqualTo(2.5f));
            Assert.That(settings.character_grading_enabled, Is.False);
            Assert.That(settings.character_grading_lut, Is.Null);
            Assert.That(ResolvedNteSettings.NativeExposureInput, Is.EqualTo(1.0f));
            Assert.That(settings.GetOutputScale(ResolvedNteSettings.ReferencePreExposure),
                Is.EqualTo(1.0f).Within(1e-6f));
            Assert.That(settings.GetOutputScale(ResolvedNteSettings.ReferencePreExposure * 2.0f),
                Is.EqualTo(2.0f).Within(1e-6f));
        }

        [Test]
        public void NteVolumeResolve_UsesBlendedValuesAndProducesIndependentSnapshots()
        {
            var first = ScriptableObject.CreateInstance<NteVolumeSettings>();
            var second = ScriptableObject.CreateInstance<NteVolumeSettings>();
            var invalid_lut = new Texture3D(4, 4, 4, TextureFormat.RGBA32, false);
            try
            {
                first.palette2AtZero.value = new Color(2.0f, 3.0f, 4.0f, 1.0f);
                first.rimWidth.value = 3.25f;
                first.outputMultiplier.value = 0.5f;
                second.palette2AtZero.value = new Color(7.0f, 8.0f, 9.0f, 1.0f);
                second.rimWidth.value = 6.5f;
                second.outputMultiplier.value = 2.0f;
                first.characterGradingEnabled.value = true;
                first.characterGradingLut.value = invalid_lut;

                var first_snapshot = first.Resolve();
                var second_snapshot = second.Resolve();

                Assert.That(first_snapshot.palette2.at_zero,
                    Is.EqualTo(new Color(2.0f, 3.0f, 4.0f, 1.0f)));
                Assert.That(second_snapshot.palette2.at_zero,
                    Is.EqualTo(new Color(7.0f, 8.0f, 9.0f, 1.0f)));
                Assert.That(first_snapshot.rim_width, Is.EqualTo(3.25f));
                Assert.That(second_snapshot.rim_width, Is.EqualTo(6.5f));
                Assert.That(first_snapshot.GetOutputScale(ResolvedNteSettings.ReferencePreExposure),
                    Is.EqualTo(0.5f).Within(1e-6f));
                Assert.That(second_snapshot.GetOutputScale(ResolvedNteSettings.ReferencePreExposure),
                    Is.EqualTo(2.0f).Within(1e-6f));
                Assert.That(first_snapshot.character_grading_enabled, Is.False);
                Assert.That(first_snapshot.character_grading_lut, Is.Null);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(invalid_lut);
                UnityEngine.Object.DestroyImmediate(first);
                UnityEngine.Object.DestroyImmediate(second);
            }
        }

        [Test]
        public void NteCharacterGrading_AcceptsSampleableRgba16Fallback()
        {
            var lut = new Texture3D(
                ResolvedNteSettings.CharacterGradingLutSize,
                ResolvedNteSettings.CharacterGradingLutSize,
                ResolvedNteSettings.CharacterGradingLutSize,
                GraphicsFormat.R16G16B16A16_UNorm,
                TextureCreationFlags.DontInitializePixels,
                1);
            try
            {
                Assert.That(ResolvedNteSettings.IsValidCharacterGradingLut(lut), Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(lut);
            }
        }

        [Test]
        public void SceneViewRendererOverride_ReusesCachedRenderer()
        {
            var asset = ScriptableObject.CreateInstance<YutrelRPAsset>();
            var defaultData = ScriptableObject.CreateInstance<FakeRendererData>();
            var overrideData = ScriptableObject.CreateInstance<FakeRendererData>();
            try
            {
                asset.Initialize(true, new YutrelRendererData[] { defaultData, overrideData }, 0);

                asset.SetSceneViewRenderer(1);
                var first = (FakeRenderer)asset.GetRenderer(asset.SceneViewRendererIndex);

                asset.SetSceneViewRenderer(0);
                Assert.That(asset.GetRenderer(asset.SceneViewRendererIndex), Is.Not.SameAs(first));

                asset.SetSceneViewRenderer(1);
                Assert.That(asset.GetRenderer(asset.SceneViewRendererIndex), Is.SameAs(first));
                Assert.That(first.IsDisposed, Is.False);
                Assert.That(overrideData.CreateCount, Is.EqualTo(1));
            }
            finally
            {
                asset.DestroyRenderers();
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

        [Test]
        public void DDGIProbeTrace_OnlyIncludesMaterialsWithExplicitPass()
        {
            var defaultLit = CreateMaterial("YutrelRP/DefaultLit");
            var openPbr = CreateMaterial("YutrelRP/OpenPBR");
            try
            {
                Assert.That(YutrelRayTracingAccelStruct.SupportsDDGIProbeTrace(defaultLit), Is.True);
                Assert.That(YutrelRayTracingAccelStruct.SupportsDDGIProbeTrace(openPbr), Is.False);
                Assert.That(YutrelRayTracingAccelStruct.SupportsDDGIProbeTrace(null), Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(defaultLit);
                UnityEngine.Object.DestroyImmediate(openPbr);
            }
        }

        private static Material CreateMaterial(string shaderName)
        {
            var shader = Shader.Find(shaderName);
            Assert.That(shader, Is.Not.Null, $"Shader '{shaderName}' was not found.");
            return new Material(shader);
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
