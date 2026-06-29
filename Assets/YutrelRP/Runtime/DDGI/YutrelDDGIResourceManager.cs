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

        private RTHandle probe_irradiance_rt;
        private RTHandle probe_distance_rt;
        private ResourceIdentity identity;
        private bool has_identity;

        public void Prepare(RenderGraph render_graph, Camera camera, DDGIResources resources)
        {
            Prepare(render_graph, ResolveActiveVolume(camera), resources);
        }

        public void Release()
        {
            ReleaseTextures();
        }

        public void Prepare(RenderGraph render_graph, YutrelDDGIVolume volume, DDGIResources resources)
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

            var next_identity = new ResourceIdentity(volume);
            if (!has_identity || !identity.Equals(next_identity) ||
                probe_irradiance_rt == null || probe_irradiance_rt.rt == null ||
                probe_irradiance_rt.rt.graphicsFormat != probe_irradiance_format ||
                probe_distance_rt == null || probe_distance_rt.rt == null ||
                probe_distance_rt.rt.graphicsFormat != probe_distance_format)
            {
                ReleaseTextures();
                probe_irradiance_rt = AllocTexture(next_identity.irradiance_width,
                    next_identity.irradiance_height, next_identity.slices, probe_irradiance_format,
                    "DDGI Probe Irradiance", FilterMode.Bilinear);
                probe_distance_rt = AllocTexture(next_identity.distance_width,
                    next_identity.distance_height, next_identity.slices, probe_distance_format,
                    "DDGI Probe Distance", FilterMode.Bilinear);
                identity = next_identity;
                has_identity = true;
            }

            resources.probe_irradiance = render_graph.ImportTexture(probe_irradiance_rt);
            resources.probe_distance = render_graph.ImportTexture(probe_distance_rt);
            resources.probe_count = volume.ProbeCount;
            resources.probe_irradiance_interior_texels = volume.ProbeIrradianceInteriorTexels;
            resources.probe_distance_interior_texels = volume.ProbeDistanceInteriorTexels;
            resources.is_valid = resources.probe_irradiance.IsValid() && resources.probe_distance.IsValid();
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

        private void ReleaseTextures()
        {
            RTHandles.Release(probe_irradiance_rt);
            RTHandles.Release(probe_distance_rt);
            probe_irradiance_rt = null;
            probe_distance_rt = null;
            has_identity = false;
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

        private readonly struct ResourceIdentity : IEquatable<ResourceIdentity>
        {
            public readonly Vector3Int probe_count;
            public readonly int irradiance_interior_texels;
            public readonly int distance_interior_texels;
            public readonly int irradiance_width;
            public readonly int irradiance_height;
            public readonly int distance_width;
            public readonly int distance_height;
            public readonly int slices;

            public ResourceIdentity(YutrelDDGIVolume volume)
            {
                probe_count = volume.ProbeCount;
                irradiance_interior_texels = volume.ProbeIrradianceInteriorTexels;
                distance_interior_texels = volume.ProbeDistanceInteriorTexels;
                var irradiance_tile = irradiance_interior_texels + 2;
                var distance_tile = distance_interior_texels + 2;
                irradiance_width = probe_count.x * irradiance_tile;
                irradiance_height = probe_count.z * irradiance_tile;
                distance_width = probe_count.x * distance_tile;
                distance_height = probe_count.z * distance_tile;
                slices = probe_count.y;
            }

            public bool Equals(ResourceIdentity other)
            {
                return probe_count == other.probe_count &&
                       irradiance_interior_texels == other.irradiance_interior_texels &&
                       distance_interior_texels == other.distance_interior_texels &&
                       irradiance_width == other.irradiance_width &&
                       irradiance_height == other.irradiance_height &&
                       distance_width == other.distance_width &&
                       distance_height == other.distance_height &&
                       slices == other.slices;
            }
        }
    }
}
