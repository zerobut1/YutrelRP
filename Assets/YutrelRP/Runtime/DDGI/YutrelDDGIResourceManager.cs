using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace YutrelRP
{
    public sealed class YutrelDDGIResourceManager : IDisposable
    {
        private static readonly GraphicsFormat probe_irradiance_format =
            DDGIResources.ProbeIrradianceGraphicsFormat;
        private static readonly GraphicsFormat probe_distance_format =
            DDGIResources.ProbeDistanceGraphicsFormat;
        private static readonly GraphicsFormat probe_data_format =
            DDGIResources.ProbeDataGraphicsFormat;

        private RTHandle probe_irradiance_rt;
        private RTHandle probe_distance_rt;
        private RTHandle probe_data_rt;
        private AllocationIdentity allocation_identity;
        private HistoryIdentity history_identity;
        private bool has_allocation_identity;
        private bool has_history_identity;

        public void Prepare(RenderGraph render_graph, Camera camera, DDGIResources resources,
            ResolvedDDGISettings ddgi_settings)
        {
            Prepare(render_graph, ResolveActiveVolume(camera), resources, ddgi_settings);
        }

        public void Release()
        {
            ReleaseTextures();
        }

        public void Prepare(RenderGraph render_graph, YutrelDDGIVolume volume, DDGIResources resources,
            ResolvedDDGISettings ddgi_settings)
        {
            if (resources == null)
            {
                throw new ArgumentNullException(nameof(resources));
            }

            resources.Reset();
            resources.active_volume = volume;
            if (render_graph == null || volume == null || !volume.isActiveAndEnabled)
            {
                return;
            }

            var next_allocation_identity = new AllocationIdentity(volume);
            var next_history_identity = new HistoryIdentity(volume, ddgi_settings);
            if (!has_allocation_identity || !allocation_identity.Equals(next_allocation_identity) ||
                HasInvalidTextureAllocation(next_allocation_identity))
            {
                ReleaseTextures();
                probe_irradiance_rt = AllocTexture(next_allocation_identity.irradiance_width,
                    next_allocation_identity.irradiance_height, next_allocation_identity.slices, probe_irradiance_format,
                    "DDGI Probe Irradiance", FilterMode.Bilinear);
                probe_distance_rt = AllocTexture(next_allocation_identity.distance_width,
                    next_allocation_identity.distance_height, next_allocation_identity.slices, probe_distance_format,
                    "DDGI Probe Distance", FilterMode.Bilinear);
                probe_data_rt = AllocTexture(next_allocation_identity.probe_data_width,
                    next_allocation_identity.probe_data_height, next_allocation_identity.slices, probe_data_format,
                    "DDGI Probe Data", FilterMode.Point);
                ClearPersistentTextures(ClearFlags.All);
                allocation_identity = next_allocation_identity;
                history_identity = next_history_identity;
                has_allocation_identity = true;
                has_history_identity = true;
            }
            else
            {
                var clear_flags = has_history_identity
                    ? history_identity.GetClearFlags(next_history_identity)
                    : ClearFlags.All;
                ClearPersistentTextures(clear_flags);
                history_identity = next_history_identity;
                has_history_identity = true;
            }

            resources.probe_irradiance = render_graph.ImportTexture(probe_irradiance_rt);
            resources.probe_distance = render_graph.ImportTexture(probe_distance_rt);
            resources.probe_data = render_graph.ImportTexture(probe_data_rt);
            resources.probe_count = volume.ProbeCount;
            resources.probe_irradiance_interior_texels = volume.ProbeIrradianceInteriorTexels;
            resources.probe_distance_interior_texels = volume.ProbeDistanceInteriorTexels;
            resources.is_valid = resources.probe_irradiance.IsValid() &&
                                 resources.probe_distance.IsValid() &&
                                 resources.probe_data.IsValid();
        }

        public void Dispose()
        {
            Release();
        }

        private static RTHandle AllocTexture(int width, int height, int slices, GraphicsFormat format, string name,
            FilterMode filter_mode)
        {
            return RTHandles.Alloc(width, height, slices: slices, dimension: TextureDimension.Tex2DArray,
                colorFormat: format, enableRandomWrite: true, filterMode: filter_mode,
                wrapMode: TextureWrapMode.Clamp, name: name);
        }

        private static void ClearTextureArray(RTHandle handle, Color clear_color)
        {
            if (handle == null || handle.rt == null)
            {
                return;
            }

            var cmd = CommandBufferPool.Get("Clear DDGI Persistent Atlas");
            for (var slice = 0; slice < handle.rt.volumeDepth; slice++)
            {
                cmd.SetRenderTarget(handle, 0, CubemapFace.Unknown, slice);
                cmd.ClearRenderTarget(false, true, clear_color);
            }

            Graphics.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        private void ClearPersistentTextures(ClearFlags flags)
        {
            if ((flags & ClearFlags.Irradiance) != 0)
            {
                ClearTextureArray(probe_irradiance_rt, new Color(0.0f, 0.0f, 0.0f, 1.0f));
            }

            if ((flags & ClearFlags.Distance) != 0)
            {
                ClearTextureArray(probe_distance_rt, Color.black);
            }

            if ((flags & ClearFlags.ProbeData) != 0)
            {
                ClearTextureArray(probe_data_rt, Color.black);
            }
        }

        private bool HasInvalidTextureAllocation(AllocationIdentity next_identity)
        {
            return probe_irradiance_rt == null || probe_irradiance_rt.rt == null ||
                   probe_irradiance_rt.rt.graphicsFormat != next_identity.irradiance_format ||
                   probe_distance_rt == null || probe_distance_rt.rt == null ||
                   probe_distance_rt.rt.graphicsFormat != next_identity.distance_format ||
                   probe_data_rt == null || probe_data_rt.rt == null ||
                   probe_data_rt.rt.graphicsFormat != next_identity.probe_data_format;
        }

        private void ReleaseTextures()
        {
            RTHandles.Release(probe_irradiance_rt);
            RTHandles.Release(probe_distance_rt);
            RTHandles.Release(probe_data_rt);
            probe_irradiance_rt = null;
            probe_distance_rt = null;
            probe_data_rt = null;
            has_allocation_identity = false;
            has_history_identity = false;
        }

        private static YutrelDDGIVolume ResolveActiveVolume(Camera camera)
        {
            var volumes = UnityEngine.Object.FindObjectsByType<YutrelDDGIVolume>();
            YutrelDDGIVolume selected = null;
            foreach (var volume in volumes)
            {
                if (volume == null || !volume.isActiveAndEnabled)
                {
                    continue;
                }

                selected = volume;
                break;
            }

            return selected;
        }

        [Flags]
        private enum ClearFlags
        {
            None = 0,
            Irradiance = 1 << 0,
            Distance = 1 << 1,
            ProbeData = 1 << 2,
            All = Irradiance | Distance | ProbeData
        }

        private readonly struct AllocationIdentity : IEquatable<AllocationIdentity>
        {
            public readonly Vector3Int probe_count;
            public readonly int irradiance_interior_texels;
            public readonly int distance_interior_texels;
            public readonly GraphicsFormat irradiance_format;
            public readonly GraphicsFormat distance_format;
            public readonly GraphicsFormat probe_data_format;
            public readonly int irradiance_width;
            public readonly int irradiance_height;
            public readonly int distance_width;
            public readonly int distance_height;
            public readonly int probe_data_width;
            public readonly int probe_data_height;
            public readonly int slices;

            public AllocationIdentity(YutrelDDGIVolume volume)
            {
                probe_count = volume.ProbeCount;
                irradiance_interior_texels = volume.ProbeIrradianceInteriorTexels;
                distance_interior_texels = volume.ProbeDistanceInteriorTexels;
                irradiance_format = probe_irradiance_format;
                distance_format = probe_distance_format;
                probe_data_format = YutrelDDGIResourceManager.probe_data_format;
                var irradiance_tile = irradiance_interior_texels + 2;
                var distance_tile = distance_interior_texels + 2;
                irradiance_width = probe_count.x * irradiance_tile;
                irradiance_height = probe_count.z * irradiance_tile;
                distance_width = probe_count.x * distance_tile;
                distance_height = probe_count.z * distance_tile;
                probe_data_width = probe_count.x;
                probe_data_height = probe_count.z;
                slices = probe_count.y;
            }

            public bool Equals(AllocationIdentity other)
            {
                return probe_count == other.probe_count &&
                       irradiance_interior_texels == other.irradiance_interior_texels &&
                       distance_interior_texels == other.distance_interior_texels &&
                       irradiance_format == other.irradiance_format &&
                       distance_format == other.distance_format &&
                       probe_data_format == other.probe_data_format &&
                       irradiance_width == other.irradiance_width &&
                       irradiance_height == other.irradiance_height &&
                       distance_width == other.distance_width &&
                       distance_height == other.distance_height &&
                       probe_data_width == other.probe_data_width &&
                       probe_data_height == other.probe_data_height &&
                       slices == other.slices;
            }
        }

        private readonly struct HistoryIdentity
        {
            public readonly YutrelDDGIVolume volume;
            public readonly Vector3 bounds_min;
            public readonly Vector3 probe_spacing;
            public readonly float probe_max_ray_distance;
            public readonly float probe_ray_radiance_max;
            public readonly float irradiance_encoding_gamma;
            public readonly float distance_exponent;
            public readonly float probe_random_ray_backface_threshold;
            public readonly bool relocation_enabled;
            public readonly bool classification_enabled;
            public readonly bool uses_fixed_rays;
            public readonly float probe_min_frontface_distance;
            public readonly float probe_fixed_ray_backface_threshold;

            public HistoryIdentity(YutrelDDGIVolume volume, ResolvedDDGISettings ddgi_settings)
            {
                var encoding_settings = ddgi_settings.encoding;
                var blending_settings = ddgi_settings.blending;
                var relocation_settings = ddgi_settings.relocation;
                var classification_settings = ddgi_settings.classification;

                this.volume = volume;
                bounds_min = volume.WorldBounds.min;
                probe_spacing = volume.GetWorldProbeSpacing();
                probe_max_ray_distance = volume.ProbeMaxRayDistance;
                probe_ray_radiance_max = encoding_settings.probeRayRadianceMax;
                irradiance_encoding_gamma = encoding_settings.irradianceEncodingGamma;
                distance_exponent = blending_settings.distanceExponent;
                probe_random_ray_backface_threshold = blending_settings.probeRandomRayBackfaceThreshold;
                relocation_enabled = relocation_settings.enabled;
                classification_enabled = classification_settings.enabled;
                uses_fixed_rays = (relocation_enabled || classification_enabled) &&
                                  volume.RaysPerProbe > DDGIResources.FixedRayCount;
                probe_min_frontface_distance = relocation_settings.probeMinFrontfaceDistance;
                probe_fixed_ray_backface_threshold = relocation_settings.probeFixedRayBackfaceThreshold;
            }

            public ClearFlags GetClearFlags(HistoryIdentity other)
            {
                var flags = ClearFlags.None;
                if (volume != other.volume ||
                    bounds_min != other.bounds_min ||
                    probe_spacing != other.probe_spacing ||
                    probe_max_ray_distance != other.probe_max_ray_distance ||
                    uses_fixed_rays != other.uses_fixed_rays ||
                    relocation_enabled != other.relocation_enabled ||
                    classification_enabled != other.classification_enabled ||
                    probe_min_frontface_distance != other.probe_min_frontface_distance ||
                    probe_fixed_ray_backface_threshold != other.probe_fixed_ray_backface_threshold)
                {
                    flags |= ClearFlags.All;
                }

                if (probe_ray_radiance_max != other.probe_ray_radiance_max ||
                    irradiance_encoding_gamma != other.irradiance_encoding_gamma ||
                    probe_random_ray_backface_threshold != other.probe_random_ray_backface_threshold)
                {
                    flags |= ClearFlags.Irradiance;
                }

                if (distance_exponent != other.distance_exponent)
                {
                    flags |= ClearFlags.Distance;
                }

                return flags;
            }
        }
    }
}
