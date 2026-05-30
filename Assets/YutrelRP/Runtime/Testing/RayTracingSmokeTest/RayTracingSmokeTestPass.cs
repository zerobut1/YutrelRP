using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace YutrelRP
{
    internal sealed class RayTracingSmokeTestPass
    {
        private const string RayGenShaderResourcePath = "Shader/RayTracingSmokeTest";
        private const string RTASShaderResourcePath = "Shader/RayTracingSmokeTestRTAS";
        private const string RayGenOnlyName = "RayGenSmokeTest";
        private const string RTASRayGenName = "RayGenSmokeTestRTAS";
        private const string ShaderPassName = "RayTracingSmokeTest";

        private static readonly ProfilingSampler sampler = new("Ray Tracing Smoke Test");
        private static readonly int outputID = Shader.PropertyToID("_RayTracingSmokeTestOutput");
        private static readonly int accelerationStructureID = Shader.PropertyToID("_RaytracingAccelerationStructure");
        private static readonly int invViewProjectionID = Shader.PropertyToID("_RayTracingSmokeTestInvViewProjection");
        private static readonly int cameraPositionWSID = Shader.PropertyToID("_RayTracingSmokeTestCameraPositionWS");
        private static readonly int modeID = Shader.PropertyToID("_RayTracingSmokeTestMode");

        private static RayTracingShader rayGenShader;
        private static RayTracingShader rtasShader;
        private static RayTracingAccelerationStructure accelerationStructure;
        private static string lastStatusKey;

        internal static void Record(RenderGraph renderGraph, Camera camera, ref RenderTargets textures,
            YutrelRPSettings.RayTracingSmokeTestSettings settings, Vector2Int attachmentSize)
        {
            if (settings == null || !settings.enabled) return;
            if (camera.cameraType != CameraType.SceneView && camera.cameraType != CameraType.Game) return;

            var outputDesc = new TextureDesc(attachmentSize.x, attachmentSize.y)
            {
                colorFormat = GraphicsFormatUtility.GetGraphicsFormat(RenderTextureFormat.Default, true),
                enableRandomWrite = true,
                clearBuffer = true,
                clearColor = Color.black,
                name = "Ray Tracing Smoke Test Output"
            };
            var output = renderGraph.CreateTexture(outputDesc);
            textures.final_color = output;

            var capability = ValidateCapability(settings.mode);
            var resourceIssue = capability == SmokeTestIssue.None ? ValidateShaderResource(settings.mode) : SmokeTestIssue.None;
            var issue = capability != SmokeTestIssue.None ? capability : resourceIssue;
            var requestedMode = settings.mode;

            if (requestedMode == YutrelRPSettings.RayTracingSmokeTestMode.RTASHitMiss && issue == SmokeTestIssue.None)
            {
                issue = BuildAccelerationStructure(settings);
            }

            using var builder = renderGraph.AddUnsafePass<RayTracingSmokeTestPass>(sampler.name, out var pass, sampler);
            pass.output = output;
            pass.rayTracingShader = GetShader(requestedMode);
            pass.rayGenName = GetRayGenName(requestedMode);
            pass.mode = requestedMode;
            pass.issue = issue;
            pass.width = attachmentSize.x;
            pass.height = attachmentSize.y;
            pass.rayTracingAccelerationStructure = accelerationStructure;
            pass.cameraPositionWS = camera.transform.position;
            pass.inverseViewProjection = GetInverseViewProjection(camera);

            builder.UseTexture(output, AccessFlags.Write);
            builder.AllowPassCulling(false);
            builder.AllowGlobalStateModification(true);
            builder.SetRenderFunc<RayTracingSmokeTestPass>(static (pass, context) => pass.Render(context));

            LogStatus(issue, requestedMode);
        }

        private TextureHandle output;
        private RayTracingShader rayTracingShader;
        private RayTracingAccelerationStructure rayTracingAccelerationStructure;
        private string rayGenName;
        private YutrelRPSettings.RayTracingSmokeTestMode mode;
        private SmokeTestIssue issue;
        private int width;
        private int height;
        private Vector3 cameraPositionWS;
        private Matrix4x4 inverseViewProjection;

        private void Render(UnsafeGraphContext context)
        {
            var cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);

            if (issue != SmokeTestIssue.None || rayTracingShader == null)
            {
                cmd.SetRenderTarget(output);
                cmd.ClearRenderTarget(false, true, GetIssueColor(issue));
                return;
            }

            cmd.SetRayTracingShaderPass(rayTracingShader, ShaderPassName);
            cmd.SetRayTracingTextureParam(rayTracingShader, outputID, output);
            cmd.SetRayTracingMatrixParam(rayTracingShader, invViewProjectionID, inverseViewProjection);
            cmd.SetRayTracingVectorParam(rayTracingShader, cameraPositionWSID, cameraPositionWS);
            cmd.SetRayTracingIntParam(rayTracingShader, modeID, mode == YutrelRPSettings.RayTracingSmokeTestMode.RTASHitMiss ? 1 : 0);

            if (mode == YutrelRPSettings.RayTracingSmokeTestMode.RTASHitMiss)
            {
                cmd.SetRayTracingAccelerationStructure(rayTracingShader, accelerationStructureID, rayTracingAccelerationStructure);
            }

            cmd.DispatchRays(rayTracingShader, rayGenName, (uint)width, (uint)height, 1, null);
        }

        private static SmokeTestIssue ValidateCapability(YutrelRPSettings.RayTracingSmokeTestMode mode)
        {
            var device = SystemInfo.graphicsDeviceType;
            if (device != GraphicsDeviceType.Direct3D12)
            {
                return SmokeTestIssue.UnsupportedGraphicsAPI;
            }

            if (!SystemInfo.supportsRayTracing)
            {
                return SmokeTestIssue.UnsupportedRayTracing;
            }

            return SmokeTestIssue.None;
        }

        private static RayTracingShader GetShader(YutrelRPSettings.RayTracingSmokeTestMode mode)
        {
            return mode == YutrelRPSettings.RayTracingSmokeTestMode.RTASHitMiss ? rtasShader : rayGenShader;
        }

        private static string GetRayGenName(YutrelRPSettings.RayTracingSmokeTestMode mode)
        {
            return mode == YutrelRPSettings.RayTracingSmokeTestMode.RTASHitMiss ? RTASRayGenName : RayGenOnlyName;
        }

        private static SmokeTestIssue ValidateShaderResource(YutrelRPSettings.RayTracingSmokeTestMode mode)
        {
            if (mode == YutrelRPSettings.RayTracingSmokeTestMode.RTASHitMiss)
            {
                if (rtasShader == null)
                {
                    rtasShader = Resources.Load<RayTracingShader>(RTASShaderResourcePath);
                }

                return rtasShader == null ? SmokeTestIssue.MissingRayTracingShader : SmokeTestIssue.None;
            }

            if (rayGenShader == null)
            {
                rayGenShader = Resources.Load<RayTracingShader>(RayGenShaderResourcePath);
            }

            return rayGenShader == null ? SmokeTestIssue.MissingRayTracingShader : SmokeTestIssue.None;
        }

        private static SmokeTestIssue BuildAccelerationStructure(YutrelRPSettings.RayTracingSmokeTestSettings settings)
        {
            if (accelerationStructure == null)
            {
                accelerationStructure = new RayTracingAccelerationStructure();
            }
            else
            {
                accelerationStructure.ClearInstances();
            }

            var testObjects = Object.FindObjectsByType<RayTracingSmokeTestObject>(FindObjectsSortMode.None);
            var instanceCount = 0;

            foreach (var testObject in testObjects)
            {
                if (testObject == null || !testObject.isActiveAndEnabled) continue;

                if (!testObject.TryGetComponent<Renderer>(out var renderer) || renderer == null || !renderer.enabled)
                {
                    continue;
                }

                try
                {
                    var subMeshCount = GetSubMeshCount(renderer);
                    var subMeshFlags = new RayTracingSubMeshFlags[Mathf.Max(1, subMeshCount)];
                    for (var i = 0; i < subMeshFlags.Length; i++)
                    {
                        subMeshFlags[i] = RayTracingSubMeshFlags.Enabled | RayTracingSubMeshFlags.ClosestHitOnly;
                    }

                    accelerationStructure.AddInstance(renderer, subMeshFlags: subMeshFlags, enableTriangleCulling: true,
                        frontTriangleCounterClockwise: false, mask: 0xFF);
                    instanceCount++;
                }
                catch (System.Exception exception)
                {
                    Debug.LogWarning($"YutrelRP RayTracingSmokeTest RTAS AddInstance failed for '{testObject.name}': {exception.Message}");
                }
            }

            if (instanceCount == 0)
            {
                return SmokeTestIssue.NoTestGeometry;
            }

            accelerationStructure.Build();
            return accelerationStructure.GetInstanceCount() > 0 ? SmokeTestIssue.None : SmokeTestIssue.EmptyAccelerationStructure;
        }

        private static int GetSubMeshCount(Renderer renderer)
        {
            if (renderer is MeshRenderer && renderer.TryGetComponent<MeshFilter>(out var meshFilter) &&
                meshFilter.sharedMesh != null)
            {
                return meshFilter.sharedMesh.subMeshCount;
            }

            if (renderer is SkinnedMeshRenderer skinnedMeshRenderer && skinnedMeshRenderer.sharedMesh != null)
            {
                return skinnedMeshRenderer.sharedMesh.subMeshCount;
            }

            return renderer.sharedMaterials != null ? renderer.sharedMaterials.Length : 1;
        }

        private static Matrix4x4 GetInverseViewProjection(Camera camera)
        {
            var view = camera.worldToCameraMatrix;
            var projection = GL.GetGPUProjectionMatrix(camera.projectionMatrix, true);
            return (projection * view).inverse;
        }

        private static Color GetIssueColor(SmokeTestIssue issue)
        {
            switch (issue)
            {
                case SmokeTestIssue.UnsupportedGraphicsAPI:
                    return new Color(0.45f, 0.0f, 0.0f, 1.0f);
                case SmokeTestIssue.UnsupportedRayTracing:
                    return new Color(0.7f, 0.25f, 0.0f, 1.0f);
                case SmokeTestIssue.MissingRayTracingShader:
                    return Color.magenta;
                case SmokeTestIssue.NoTestGeometry:
                case SmokeTestIssue.EmptyAccelerationStructure:
                    return new Color(0.0f, 0.0f, 0.65f, 1.0f);
                default:
                    return Color.black;
            }
        }

        private static void LogStatus(SmokeTestIssue issue, YutrelRPSettings.RayTracingSmokeTestMode mode)
        {
            var device = SystemInfo.graphicsDeviceType;
            var key = $"{mode}:{issue}:{device}:{SystemInfo.supportsRayTracing}";
            if (key == lastStatusKey) return;
            lastStatusKey = key;

            if (issue == SmokeTestIssue.None)
            {
                Debug.Log($"YutrelRP RayTracingSmokeTest OK: mode={mode}, api={device}. DDGI preflight ray tracing path is available for this manual test.");
                return;
            }

            Debug.LogWarning($"YutrelRP RayTracingSmokeTest failed: category={GetCategory(issue)}, reason={GetReason(issue)}, mode={mode}, api={device}, supportsRayTracing={SystemInfo.supportsRayTracing}.");
        }

        private static string GetCategory(SmokeTestIssue issue)
        {
            switch (issue)
            {
                case SmokeTestIssue.UnsupportedGraphicsAPI:
                case SmokeTestIssue.UnsupportedRayTracing:
                    return "platform/API";
                case SmokeTestIssue.MissingRayTracingShader:
                    return "resource/loading";
                case SmokeTestIssue.NoTestGeometry:
                case SmokeTestIssue.EmptyAccelerationStructure:
                    return "acceleration-structure/geometry";
                default:
                    return "dispatch/output";
            }
        }

        private static string GetReason(SmokeTestIssue issue)
        {
            switch (issue)
            {
                case SmokeTestIssue.UnsupportedGraphicsAPI:
                    return "Unity native ray tracing requires Direct3D12 in this pipeline";
                case SmokeTestIssue.UnsupportedRayTracing:
                    return "SystemInfo.supportsRayTracing is false";
                case SmokeTestIssue.MissingRayTracingShader:
                    return "Resources/Shader/RayTracingSmokeTest or RayTracingSmokeTestRTAS RayTracingShader asset is missing or invalid";
                case SmokeTestIssue.NoTestGeometry:
                    return "no active RayTracingSmokeTestObject with an enabled Renderer was found";
                case SmokeTestIssue.EmptyAccelerationStructure:
                    return "RTAS build produced no instances";
                default:
                    return "unknown failure";
            }
        }

        public static void Cleanup()
        {
            accelerationStructure?.Dispose();
            accelerationStructure = null;
            rayGenShader = null;
            rtasShader = null;
            lastStatusKey = null;
        }

        private enum SmokeTestIssue
        {
            None = 0,
            UnsupportedGraphicsAPI = 1,
            UnsupportedRayTracing = 2,
            MissingRayTracingShader = 3,
            NoTestGeometry = 4,
            EmptyAccelerationStructure = 5
        }
    }
}
