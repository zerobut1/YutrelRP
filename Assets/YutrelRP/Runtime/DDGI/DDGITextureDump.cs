using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace YutrelRP
{
    public static class DDGITextureDump
    {
        private const string OutputRoot = "Dumps";
        private const string CopyArrayKernelName = "CopyRgbaHalfTexture2DArray";
        private const string Copy2DKernelName = "CopyRgbaHalfTexture2D";
        private const string CopyRgArrayKernelName = "CopyRgFloatTexture2DArray";
        private const string CopyProbeRayDataKernelName = "CopyProbeRayDataTexture2DArray";
        private const int ThreadGroupSizeX = 8;
        private const int ThreadGroupSizeY = 8;
        private const uint DdsMagic = 0x20534444u;
        private const uint DdsCapsTexture = 0x1000u;
        private const uint DdsFourCC = 0x4u;
        private const uint DdsResourceDimensionTexture2D = 3u;
        private const uint DdsAlphaModeUnknown = 0u;
        private const uint DxgiFormatR16G16B16A16Float = 10u;
        private const uint DxgiFormatR32G32Float = 16u;

        private static readonly ProfilingSampler sampler = new("DDGI Texture Dump Capture");
        private static readonly int sourceID = Shader.PropertyToID("_DDGITextureDumpSource");
        private static readonly int source2DID = Shader.PropertyToID("_DDGITextureDumpSource2D");
        private static readonly int sourceRgID = Shader.PropertyToID("_DDGITextureDumpSourceRG");
        private static readonly int destinationID = Shader.PropertyToID("_DDGITextureDumpDestination");
        private static readonly int destinationRgID = Shader.PropertyToID("_DDGITextureDumpDestinationRG");
        private static readonly int textureSizeID = Shader.PropertyToID("_DDGITextureDumpSize");

        private static DumpRequest pendingRequest;
        private static ActiveDump activeDump;
        private static RTHandle probeRayDataRawStaging;
        private static RTHandle probeRayDataStaging;
        private static RTHandle traceAlbedoStaging;
        private static RTHandle screenTraceDebugStaging;
        private static int copyArrayKernel = -1;
        private static int copy2DKernel = -1;
        private static int copyRgArrayKernel = -1;
        private static int copyProbeRayDataKernel = -1;

#if UNITY_EDITOR
        public static bool HasPendingRequest => pendingRequest != null || activeDump != null;

        public static void RequestDump()
        {
            if (pendingRequest != null || activeDump != null)
            {
                Debug.LogWarning("YutrelRP DDGI texture dump is already pending or in progress.");
                return;
            }

            pendingRequest = new DumpRequest(CreateUniqueDumpDirectory(DateTime.Now));
            RegisterEditorUpdate();
            Debug.Log($"YutrelRP DDGI texture dump requested. Waiting for the next valid DDGI render frame. Output: {pendingRequest.OutputDirectory}");
        }

        private static void RegisterEditorUpdate()
        {
            EditorApplication.update -= EditorUpdate;
            EditorApplication.update += EditorUpdate;
        }

        private static void UnregisterEditorUpdate()
        {
            EditorApplication.update -= EditorUpdate;
        }

        private static void EditorUpdate()
        {
            Update();
        }
#endif

        internal static void Record(RenderGraph renderGraph, Camera camera, DDGIResources resources,
            YutrelRPSettings.DDGISettings settings, Vector2Int attachmentSize)
        {
            if (pendingRequest == null || activeDump != null)
            {
                return;
            }

            if (camera.cameraType != CameraType.Game && camera.cameraType != CameraType.SceneView)
            {
                return;
            }

            var request = pendingRequest;
            pendingRequest = null;
            activeDump = new ActiveDump(request.OutputDirectory, settings);
            FillMetadata(activeDump.Metadata, camera, resources, settings);

            if (settings == null || !settings.enabled)
            {
                activeDump.Metadata.missingResources.Add(Missing("DDGI", "DDGI settings are disabled."));
                CompleteActiveDump();
                return;
            }

            if (resources == null)
            {
                activeDump.Metadata.missingResources.Add(Missing("DDGIResources", "DDGIResources is null."));
                CompleteActiveDump();
                return;
            }

            var recordedAny = false;
            recordedAny |= TryRecordTransientCapture(renderGraph, resources.probe_ray_data, ref probeRayDataRawStaging,
                "DDGIProbeRayData", "probe-ray-data.dds", resources.ProbeRayDataDimensions,
                "RTXGI F32x2 RayData raw payload: R=asfloat(R10G10B10 packed radiance bits), G=signed distance (miss=1e27, backface=-hitT*0.2), B/A unused",
                CaptureKind.RgFloatArray);
            recordedAny |= TryRecordTransientCapture(renderGraph, resources.probe_ray_data, ref probeRayDataStaging,
                "DDGIProbeRayDataDecoded", "probe-ray-data-decoded.dds", resources.ProbeRayDataDimensions,
                "decoded view of RTXGI F32x2 RayData: RGB=RTXGIUintToFloat3(asuint(raw.R)), A=signed distance",
                CaptureKind.ProbeRayDataDecoded);
            recordedAny |= TryRecordTransientCapture(renderGraph, resources.trace_albedo, ref traceAlbedoStaging,
                "DDGITraceAlbedo", "trace-albedo.dds", resources.ProbeRayDataDimensions,
                "x=rayIndex, y=probeX+probeZ*probeCount.x, slice=probeY, rgba=trace base color/status debug",
                CaptureKind.RgbaHalfArray);
            recordedAny |= TryRecordPersistentCapture(resources.probe_irradiance_texture, "DDGIProbeIrradiance",
                "probe-irradiance.dds", resources.ProbeIrradianceDimensions,
                "octahedral irradiance atlas with 1 texel border per probe tile; slice=probeY");
            recordedAny |= TryRecordPersistentCapture(resources.probe_distance_texture, "DDGIProbeDistance",
                "probe-distance.dds", resources.ProbeDistanceDimensions,
                "octahedral distance atlas with 1 texel border per probe tile; slice=probeY");
            recordedAny |= TryRecordPersistentCapture(resources.probe_data_texture, "DDGIProbeData",
                "probe-data.dds", resources.ProbeDataDimensions,
                "width=probeCount.x, height=probeCount.z, slice=probeY, rgba=offset.xyz/state");

            if (resources.screen_trace_debug.IsValid())
            {
                var screenTraceDimensions = new Vector4(attachmentSize.x, attachmentSize.y, 1.0f, 0.0f);
                recordedAny |= TryRecordTransientCapture(renderGraph, resources.screen_trace_debug,
                    ref screenTraceDebugStaging, "DDGIScreenTraceDebug", "screen-trace-debug.dds",
                    screenTraceDimensions, "screen-space trace debug output, present only for DDGI screen trace debug view",
                    CaptureKind.RgbaHalf2D);
            }
            else
            {
                activeDump.Metadata.missingResources.Add(Missing("DDGIScreenTraceDebug",
                    "Screen trace debug texture was not valid in this frame."));
            }

            if (!recordedAny)
            {
                activeDump.Metadata.missingResources.Add(Missing("DDGITextures",
                    resources.diagnostic ?? "No valid DDGI textures were available in the dump frame."));
                CompleteActiveDump();
            }
        }

        internal static void Update()
        {
            var dump = activeDump;
            if (dump == null || !dump.ReadbackStarted)
            {
                return;
            }

            var allDone = true;
            for (var i = 0; i < dump.Textures.Count; i++)
            {
                var texture = dump.Textures[i];
                if (texture.Status != TextureDumpStatus.PendingReadback)
                {
                    continue;
                }

                if (!texture.Readback.done)
                {
                    allDone = false;
                    continue;
                }

                if (texture.Readback.hasError)
                {
                    texture.Status = TextureDumpStatus.Failed;
                    texture.FailureReason = "AsyncGPUReadback reported an error.";
                    dump.Metadata.missingResources.Add(Missing(texture.Name, texture.FailureReason));
                    Debug.LogWarning($"YutrelRP DDGI texture dump readback failed: {texture.Name}.");
                    continue;
                }

                try
                {
                    var filePath = Path.Combine(dump.OutputDirectory, texture.FileName);
                    WriteDdsTextureArray(filePath, texture.Width, texture.Height, texture.Slices,
                        texture.Format, texture.Readback);
                    texture.Status = TextureDumpStatus.Written;
                    texture.FilePath = filePath;
                    AddTextureMetadata(dump.Metadata, texture);
                    Debug.Log($"YutrelRP DDGI texture dumped: {filePath}");
                }
                catch (Exception exception)
                {
                    texture.Status = TextureDumpStatus.Failed;
                    texture.FailureReason = exception.Message;
                    dump.Metadata.missingResources.Add(Missing(texture.Name,
                        $"Failed to write DDS: {exception.Message}"));
                    Debug.LogWarning($"YutrelRP DDGI texture dump write failed for {texture.Name}: {exception.Message}");
                }
            }

            if (allDone)
            {
                CompleteActiveDump();
            }
        }

        internal static void Cleanup()
        {
            ReleaseStaging(ref probeRayDataRawStaging);
            ReleaseStaging(ref probeRayDataStaging);
            ReleaseStaging(ref traceAlbedoStaging);
            ReleaseStaging(ref screenTraceDebugStaging);
            pendingRequest = null;
            activeDump = null;
            copyArrayKernel = -1;
            copy2DKernel = -1;
            copyRgArrayKernel = -1;
            copyProbeRayDataKernel = -1;
#if UNITY_EDITOR
            UnregisterEditorUpdate();
#endif
        }

        private static bool TryRecordTransientCapture(RenderGraph renderGraph, TextureHandle source,
            ref RTHandle staging, string name, string fileName, Vector4 dimensions, string layout, CaptureKind captureKind)
        {
            if (!source.IsValid())
            {
                activeDump.Metadata.missingResources.Add(Missing(name, "RenderGraph texture handle was invalid."));
                return false;
            }

            var width = (int)dimensions.x;
            var height = (int)dimensions.y;
            var slices = (int)dimensions.z;
            if (!IsValidTextureSize(width, height, slices))
            {
                activeDump.Metadata.missingResources.Add(Missing(name,
                    $"Invalid texture dimensions: {width}x{height}x{slices}."));
                return false;
            }

            var kernel = EnsureCopyShader(captureKind);
            if (kernel < 0)
            {
                activeDump.Metadata.missingResources.Add(Missing(name,
                    "DDGI texture dump copy shader is missing or invalid."));
                return false;
            }

            if (!EnsureStaging(ref staging, width, height, slices, name, DestinationFormat(captureKind)))
            {
                activeDump.Metadata.missingResources.Add(Missing(name,
                    "Failed to allocate persistent staging texture."));
                return false;
            }

            var texture = CreateTextureDump(name, fileName, staging.rt, width, height, slices, layout, true);
            activeDump.Textures.Add(texture);

            using var builder = renderGraph.AddComputePass<CopyPass>(sampler.name + " " + name, out var pass, sampler);
            pass.source = source;
            pass.destination = renderGraph.ImportTexture(staging);
            pass.shader = activeDump.CopyShader;
            pass.kernel = kernel;
            pass.width = width;
            pass.height = height;
            pass.slices = slices;
            pass.captureKind = captureKind;
            builder.UseTexture(pass.source, AccessFlags.Read);
            builder.UseTexture(pass.destination, AccessFlags.Write);
            builder.AllowPassCulling(false);
            builder.SetRenderFunc<CopyPass>(static (pass, context) => pass.Render(context));
            return true;
        }

        private static bool TryRecordPersistentCapture(Texture texture, string name, string fileName,
            Vector4 dimensions, string layout)
        {
            if (texture == null)
            {
                activeDump.Metadata.missingResources.Add(Missing(name, "Persistent texture was null."));
                return false;
            }

            var width = (int)dimensions.x;
            var height = (int)dimensions.y;
            var slices = (int)dimensions.z;
            if (!IsValidTextureSize(width, height, slices))
            {
                activeDump.Metadata.missingResources.Add(Missing(name,
                    $"Invalid texture dimensions: {width}x{height}x{slices}."));
                return false;
            }

            if (texture.width != width || texture.height != height)
            {
                activeDump.Metadata.missingResources.Add(Missing(name,
                    $"Texture size mismatch. Metadata={width}x{height}, texture={texture.width}x{texture.height}."));
                return false;
            }

            if (texture.graphicsFormat != GraphicsFormat.R16G16B16A16_SFloat)
            {
                activeDump.Metadata.missingResources.Add(Missing(name,
                    $"Unsupported DDS dump source format: {texture.graphicsFormat}."));
                return false;
            }

            var dumpTexture = CreateTextureDump(name, fileName, texture, width, height, slices, layout, false);
            activeDump.Textures.Add(dumpTexture);
            return true;
        }

        internal static void StartReadbacks()
        {
            var dump = activeDump;
            if (dump == null || dump.ReadbackStarted)
            {
                return;
            }

            if (dump.Textures.Count == 0)
            {
                CompleteActiveDump();
                return;
            }

            Directory.CreateDirectory(dump.OutputDirectory);
            foreach (var texture in dump.Textures)
            {
                if (!IsReadableFormat(texture))
                {
                    texture.Status = TextureDumpStatus.Failed;
                    texture.FailureReason = $"Unsupported readback format: {texture.Format}.";
                    dump.Metadata.missingResources.Add(Missing(texture.Name, texture.FailureReason));
                    continue;
                }

                try
                {
                    var readbackFormat = texture.Format == GraphicsFormat.R32G32_SFloat
                        ? GraphicsFormat.R32G32_SFloat
                        : GraphicsFormat.R16G16B16A16_SFloat;
                    texture.Readback = AsyncGPUReadback.Request(texture.Texture, 0, 0, texture.Width, 0,
                        texture.Height, 0, texture.Slices, readbackFormat);
                    texture.Status = TextureDumpStatus.PendingReadback;
                }
                catch (Exception exception)
                {
                    texture.Status = TextureDumpStatus.Failed;
                    texture.FailureReason = exception.Message;
                    dump.Metadata.missingResources.Add(Missing(texture.Name,
                        $"AsyncGPUReadback request failed: {exception.Message}"));
                    Debug.LogWarning($"YutrelRP DDGI texture dump readback request failed for {texture.Name}: {exception.Message}");
                }
            }

            dump.ReadbackStarted = true;
            Debug.Log($"YutrelRP DDGI texture dump readback started: {dump.OutputDirectory}");
        }

        private static TextureDump CreateTextureDump(string name, string fileName, Texture texture, int width, int height,
            int slices, string layout, bool capturedFromTransient)
        {
            return new TextureDump
            {
                Name = name,
                FileName = fileName,
                Texture = texture,
                Width = width,
                Height = height,
                Slices = slices,
                MipCount = 1,
                Format = texture.graphicsFormat,
                Layout = layout,
                CapturedFromTransient = capturedFromTransient,
                Status = TextureDumpStatus.WaitingForGpu
            };
        }

        private static int EnsureCopyShader(CaptureKind captureKind)
        {
            if (activeDump.CopyShader == null)
            {
                return -1;
            }

            if (captureKind == CaptureKind.RgbaHalfArray && copyArrayKernel >= 0)
            {
                return copyArrayKernel;
            }

            if (captureKind == CaptureKind.RgbaHalf2D && copy2DKernel >= 0)
            {
                return copy2DKernel;
            }

            if (captureKind == CaptureKind.RgFloatArray && copyRgArrayKernel >= 0)
            {
                return copyRgArrayKernel;
            }

            if (captureKind == CaptureKind.ProbeRayDataDecoded && copyProbeRayDataKernel >= 0)
            {
                return copyProbeRayDataKernel;
            }

            try
            {
                switch (captureKind)
                {
                    case CaptureKind.RgbaHalf2D:
                        copy2DKernel = activeDump.CopyShader.FindKernel(Copy2DKernelName);
                        break;
                    case CaptureKind.RgFloatArray:
                        copyRgArrayKernel = activeDump.CopyShader.FindKernel(CopyRgArrayKernelName);
                        break;
                    case CaptureKind.ProbeRayDataDecoded:
                        copyProbeRayDataKernel = activeDump.CopyShader.FindKernel(CopyProbeRayDataKernelName);
                        break;
                    default:
                        copyArrayKernel = activeDump.CopyShader.FindKernel(CopyArrayKernelName);
                        break;
                }
            }
            catch (Exception)
            {
                switch (captureKind)
                {
                    case CaptureKind.RgbaHalf2D:
                        copy2DKernel = -1;
                        break;
                    case CaptureKind.RgFloatArray:
                        copyRgArrayKernel = -1;
                        break;
                    case CaptureKind.ProbeRayDataDecoded:
                        copyProbeRayDataKernel = -1;
                        break;
                    default:
                        copyArrayKernel = -1;
                        break;
                }
            }

            return captureKind switch
            {
                CaptureKind.RgbaHalf2D => copy2DKernel,
                CaptureKind.RgFloatArray => copyRgArrayKernel,
                CaptureKind.ProbeRayDataDecoded => copyProbeRayDataKernel,
                _ => copyArrayKernel
            };
        }

        private static bool EnsureStaging(ref RTHandle handle, int width, int height, int slices, string name,
            GraphicsFormat format)
        {
            if (handle != null && handle.rt != null &&
                handle.rt.width == width &&
                handle.rt.height == height &&
                handle.rt.volumeDepth == slices &&
                handle.rt.graphicsFormat == format)
            {
                return true;
            }

            ReleaseStaging(ref handle);

            try
            {
                handle = RTHandles.Alloc(width, height, slices: slices, dimension: TextureDimension.Tex2DArray,
                    colorFormat: format, enableRandomWrite: true,
                    filterMode: FilterMode.Point, wrapMode: TextureWrapMode.Clamp,
                    name: "DDGI Texture Dump " + name);
                return handle?.rt != null;
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"YutrelRP DDGI texture dump staging allocation failed for {name}: {exception.Message}");
                ReleaseStaging(ref handle);
                return false;
            }
        }

        private static void ReleaseStaging(ref RTHandle handle)
        {
            if (handle != null)
            {
                RTHandles.Release(handle);
                handle = null;
            }
        }

        private static bool IsReadableFormat(TextureDump texture)
        {
            return texture.Format == GraphicsFormat.R16G16B16A16_SFloat ||
                   texture.Format == GraphicsFormat.R32G32_SFloat;
        }

        private static GraphicsFormat DestinationFormat(CaptureKind captureKind)
        {
            return captureKind == CaptureKind.RgFloatArray
                ? GraphicsFormat.R32G32_SFloat
                : GraphicsFormat.R16G16B16A16_SFloat;
        }

        private static bool IsValidTextureSize(int width, int height, int slices)
        {
            return width > 0 && height > 0 && slices > 0 &&
                   width <= SystemInfo.maxTextureSize &&
                   height <= SystemInfo.maxTextureSize &&
                   slices <= SystemInfo.maxTextureArraySlices;
        }

        private static void CompleteActiveDump()
        {
            var dump = activeDump;
            if (dump == null)
            {
                return;
            }

            try
            {
                Directory.CreateDirectory(dump.OutputDirectory);
                dump.Metadata.completedAt = DateTime.Now.ToString("O", CultureInfo.InvariantCulture);
                dump.Metadata.status = dump.Metadata.textures.Count > 0 ? "completed" : "failed-no-textures";
                WriteMetadata(Path.Combine(dump.OutputDirectory, "metadata.json"), dump.Metadata);
                if (dump.Metadata.textures.Count > 0)
                {
                    Debug.Log($"YutrelRP DDGI texture dump complete: {dump.OutputDirectory}");
                }
                else
                {
                    Debug.LogWarning($"YutrelRP DDGI texture dump produced no DDS files. Metadata: {Path.Combine(dump.OutputDirectory, "metadata.json")}");
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"YutrelRP DDGI texture dump metadata write failed: {exception.Message}");
            }
            finally
            {
                activeDump = null;
#if UNITY_EDITOR
                if (pendingRequest == null)
                {
                    UnregisterEditorUpdate();
                }
#endif
            }
        }

        private static void AddTextureMetadata(DumpMetadata metadata, TextureDump texture)
        {
            metadata.textures.Add(new TextureMetadata
            {
                name = texture.Name,
                fileName = texture.FileName,
                width = texture.Width,
                height = texture.Height,
                arraySlices = texture.Slices,
                mipCount = texture.MipCount,
                sourceGraphicsFormat = texture.Format.ToString(),
                ddsFormat = DdsFormatName(texture.Format),
                dimension = texture.Slices > 1 ? "Texture2DArray" : "Texture2D",
                layout = texture.Layout,
                capturedFromTransientRenderGraphResource = texture.CapturedFromTransient
            });
        }

        private static void FillMetadata(DumpMetadata metadata, Camera camera, DDGIResources resources,
            YutrelRPSettings.DDGISettings settings)
        {
            metadata.createdAt = DateTime.Now.ToString("O", CultureInfo.InvariantCulture);
            metadata.unityFrame = Time.frameCount;
            metadata.cameraName = camera != null ? camera.name : null;
            metadata.graphicsDeviceType = SystemInfo.graphicsDeviceType.ToString();
            metadata.supportsRayTracing = SystemInfo.supportsRayTracing;

            if (resources != null)
            {
                metadata.volume = new VolumeMetadata
                {
                    probeCount = Int3(resources.probe_count),
                    raysPerProbe = resources.rays_per_probe,
                    probeMaxRayDistance = resources.probe_max_ray_distance,
                    volumeMinWS = Float3(resources.volume_min_ws),
                    volumeMaxWS = Float3(resources.volume_max_ws),
                    probeSpacingWS = Float3(resources.probe_spacing_ws),
                    probeNormalBias = resources.probe_normal_bias,
                    probeViewBias = resources.probe_view_bias,
                    probeIrradianceInteriorTexels = resources.probe_irradiance_interior_texels,
                    probeDistanceInteriorTexels = resources.probe_distance_interior_texels,
                    probeIrradianceEncodingGamma = resources.probe_irradiance_encoding_gamma,
                    probeDistanceExponent = resources.probe_distance_exponent,
                    probeRelocationEnabled = resources.probe_relocation_enabled,
                    hasPersistentAtlas = resources.has_persistent_atlas,
                    hasGatherData = resources.has_gather_data,
                    diagnostic = resources.diagnostic
                };
            }

            if (settings != null)
            {
                metadata.debug = new DebugMetadata
                {
                    debugProbeRayDataSlice = settings.debugProbeRayDataSlice,
                    debugProbeIrradianceAtlasSlice = settings.debugProbeIrradianceAtlasSlice,
                    debugProbeDistanceAtlasSlice = settings.debugProbeDistanceAtlasSlice,
                    debugProbeDataSlice = settings.debugProbeDataSlice,
                    diffuseIntensity = settings.diffuseIntensity
                };
            }
        }

        private static MissingResourceMetadata Missing(string name, string reason)
        {
            return new MissingResourceMetadata { name = name, reason = reason };
        }

        private static int[] Int3(Vector3Int value)
        {
            return new[] { value.x, value.y, value.z };
        }

        private static float[] Float3(Vector3 value)
        {
            return new[] { value.x, value.y, value.z };
        }

        private static string CreateUniqueDumpDirectory(DateTime now)
        {
            var root = Path.GetFullPath(Path.Combine(Application.dataPath, "..", OutputRoot));
            var stamp = now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            for (var index = 0; index < 1000; index++)
            {
                var suffix = index == 0 ? "" : "-" + index.ToString("000", CultureInfo.InvariantCulture);
                var candidate = Path.Combine(root, stamp + suffix);
                if (!Directory.Exists(candidate))
                {
                    return candidate;
                }
            }

            return Path.Combine(root, stamp + "-" + Guid.NewGuid().ToString("N"));
        }

        private static void WriteDdsTextureArray(string path, int width, int height, int slices, GraphicsFormat format,
            AsyncGPUReadbackRequest request)
        {
            var sliceBytes = width * height * BytesPerPixel(format);

            using var stream = File.Open(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            using var writer = new BinaryWriter(stream);
            WriteDdsHeader(writer, width, height, slices, format);
            for (var slice = 0; slice < slices; slice++)
            {
                var data = request.GetData<byte>(slice);
                if (data.Length < sliceBytes)
                {
                    throw new InvalidOperationException(
                        $"Readback slice {slice} data is too small. Expected at least {sliceBytes} bytes, got {data.Length}.");
                }

                var buffer = data.ToArray();
                writer.Write(buffer, 0, sliceBytes);
            }
        }

        private static void WriteDdsHeader(BinaryWriter writer, int width, int height, int slices, GraphicsFormat format)
        {
            writer.Write(DdsMagic);
            writer.Write(124u);
            writer.Write(0x0002100Fu);
            writer.Write((uint)height);
            writer.Write((uint)width);
            writer.Write((uint)(width * BytesPerPixel(format)));
            writer.Write(0u);
            writer.Write(1u);
            for (var i = 0; i < 11; i++)
            {
                writer.Write(0u);
            }

            writer.Write(32u);
            writer.Write(DdsFourCC);
            writer.Write(FourCC("DX10"));
            writer.Write(0u);
            writer.Write(0u);
            writer.Write(0u);
            writer.Write(0u);
            writer.Write(0u);
            writer.Write(DdsCapsTexture);
            writer.Write(0u);
            writer.Write(0u);
            writer.Write(0u);
            writer.Write(0u);

            writer.Write(DdsDxgiFormat(format));
            writer.Write(DdsResourceDimensionTexture2D);
            writer.Write(0u);
            writer.Write((uint)slices);
            writer.Write(DdsAlphaModeUnknown);
        }

        private static uint FourCC(string value)
        {
            return (uint)value[0] |
                   ((uint)value[1] << 8) |
                   ((uint)value[2] << 16) |
                   ((uint)value[3] << 24);
        }

        private static int BytesPerPixel(GraphicsFormat format)
        {
            switch (format)
            {
                case GraphicsFormat.R16G16B16A16_SFloat:
                case GraphicsFormat.R32G32_SFloat:
                    return 8;
                default:
                    throw new InvalidOperationException($"Unsupported DDS dump format: {format}.");
            }
        }

        private static uint DdsDxgiFormat(GraphicsFormat format)
        {
            switch (format)
            {
                case GraphicsFormat.R16G16B16A16_SFloat:
                    return DxgiFormatR16G16B16A16Float;
                case GraphicsFormat.R32G32_SFloat:
                    return DxgiFormatR32G32Float;
                default:
                    throw new InvalidOperationException($"Unsupported DDS dump format: {format}.");
            }
        }

        private static string DdsFormatName(GraphicsFormat format)
        {
            switch (format)
            {
                case GraphicsFormat.R16G16B16A16_SFloat:
                    return "DXGI_FORMAT_R16G16B16A16_FLOAT";
                case GraphicsFormat.R32G32_SFloat:
                    return "DXGI_FORMAT_R32G32_FLOAT";
                default:
                    return "unsupported";
            }
        }

        private static void WriteMetadata(string path, DumpMetadata metadata)
        {
            File.WriteAllText(path, JsonUtility.ToJson(metadata, true), Encoding.UTF8);
        }

        private sealed class DumpRequest
        {
            public readonly string OutputDirectory;

            public DumpRequest(string outputDirectory)
            {
                OutputDirectory = outputDirectory;
            }
        }

        private sealed class ActiveDump
        {
            public readonly string OutputDirectory;
            public readonly DumpMetadata Metadata;
            public readonly List<TextureDump> Textures = new();
            public readonly ComputeShader CopyShader;
            public bool ReadbackStarted;

            public ActiveDump(string outputDirectory, YutrelRPSettings.DDGISettings settings)
            {
                OutputDirectory = outputDirectory;
                Metadata = new DumpMetadata { outputDirectory = outputDirectory };
                CopyShader = ResolveCopyShader(settings);
            }
        }

        private static ComputeShader ResolveCopyShader(YutrelRPSettings.DDGISettings settings)
        {
            var configured = settings?.textureDumpCopyShader;
            if (configured != null)
            {
                return configured;
            }

#if UNITY_EDITOR
            return AssetDatabase.LoadAssetAtPath<ComputeShader>(
                "Assets/YutrelRP/Shader/DDGI/DDGITextureDumpCopy.compute");
#else
            return null;
#endif
        }

        private sealed class TextureDump
        {
            public string Name;
            public string FileName;
            public Texture Texture;
            public int Width;
            public int Height;
            public int Slices;
            public int MipCount;
            public GraphicsFormat Format;
            public string Layout;
            public bool CapturedFromTransient;
            public TextureDumpStatus Status;
            public AsyncGPUReadbackRequest Readback;
            public string FilePath;
            public string FailureReason;
        }

        private enum TextureDumpStatus
        {
            WaitingForGpu,
            PendingReadback,
            Written,
            Failed
        }

        private sealed class CopyPass
        {
            public TextureHandle source;
            public TextureHandle destination;
            public ComputeShader shader;
            public int kernel;
            public int width;
            public int height;
            public int slices;
            public CaptureKind captureKind;

            public void Render(ComputeGraphContext context)
            {
                var cmd = context.cmd;
                switch (captureKind)
                {
                    case CaptureKind.RgbaHalf2D:
                        cmd.SetComputeTextureParam(shader, kernel, source2DID, source);
                        cmd.SetComputeTextureParam(shader, kernel, destinationID, destination);
                        break;
                    case CaptureKind.RgFloatArray:
                        cmd.SetComputeTextureParam(shader, kernel, sourceRgID, source);
                        cmd.SetComputeTextureParam(shader, kernel, destinationRgID, destination);
                        break;
                    case CaptureKind.ProbeRayDataDecoded:
                        cmd.SetComputeTextureParam(shader, kernel, sourceRgID, source);
                        cmd.SetComputeTextureParam(shader, kernel, destinationID, destination);
                        break;
                    default:
                        cmd.SetComputeTextureParam(shader, kernel, sourceID, source);
                        cmd.SetComputeTextureParam(shader, kernel, destinationID, destination);
                        break;
                }
                cmd.SetComputeVectorParam(shader, textureSizeID, new Vector4(width, height, slices, 0.0f));
                cmd.DispatchCompute(shader, kernel,
                    DivRoundUp(width, ThreadGroupSizeX),
                    DivRoundUp(height, ThreadGroupSizeY),
                    Mathf.Max(1, slices));
            }
        }

        private enum CaptureKind
        {
            RgbaHalfArray,
            RgbaHalf2D,
            RgFloatArray,
            ProbeRayDataDecoded
        }

        private static int DivRoundUp(int value, int divisor)
        {
            return Mathf.Max(1, (value + divisor - 1) / divisor);
        }

        [Serializable]
        private sealed class DumpMetadata
        {
            public string status;
            public string createdAt;
            public string completedAt;
            public string outputDirectory;
            public int unityFrame;
            public string cameraName;
            public string graphicsDeviceType;
            public bool supportsRayTracing;
            public VolumeMetadata volume;
            public DebugMetadata debug;
            public List<TextureMetadata> textures = new();
            public List<MissingResourceMetadata> missingResources = new();
        }

        [Serializable]
        private sealed class VolumeMetadata
        {
            public int[] probeCount;
            public int raysPerProbe;
            public float probeMaxRayDistance;
            public float[] volumeMinWS;
            public float[] volumeMaxWS;
            public float[] probeSpacingWS;
            public float probeNormalBias;
            public float probeViewBias;
            public int probeIrradianceInteriorTexels;
            public int probeDistanceInteriorTexels;
            public float probeIrradianceEncodingGamma;
            public float probeDistanceExponent;
            public bool probeRelocationEnabled;
            public bool hasPersistentAtlas;
            public bool hasGatherData;
            public string diagnostic;
        }

        [Serializable]
        private sealed class DebugMetadata
        {
            public int debugProbeRayDataSlice;
            public int debugProbeIrradianceAtlasSlice;
            public int debugProbeDistanceAtlasSlice;
            public int debugProbeDataSlice;
            public float diffuseIntensity;
        }

        [Serializable]
        private sealed class TextureMetadata
        {
            public string name;
            public string fileName;
            public int width;
            public int height;
            public int arraySlices;
            public int mipCount;
            public string sourceGraphicsFormat;
            public string ddsFormat;
            public string dimension;
            public string layout;
            public bool capturedFromTransientRenderGraphResource;
        }

        [Serializable]
        private sealed class MissingResourceMetadata
        {
            public string name;
            public string reason;
        }
    }
}
