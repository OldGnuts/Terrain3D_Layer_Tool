// /Layers/Path/PathLayerGPU.cs
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using Terrain3DTools.Core;
using Terrain3DTools.Layers.Path;
using Terrain3DTools.Core.Debug;
using Terrain3DTools.Utils;

namespace Terrain3DTools.Layers
{
    /// <summary>
    /// GPU operations for PathLayer. Handles shader dispatches for SDF generation,
    /// mask creation, and region application.
    /// </summary>
    public partial class PathLayer
    {
        #region Shader Paths
        private const string SHADER_PATH_SDF = "res://addons/terrain_3d_tools/Shaders/Path/PathSDF.glsl";
        private const string SHADER_PATH_MASK = "res://addons/terrain_3d_tools/Shaders/Path/PathMask.glsl";
        private const string SHADER_APPLY_HEIGHT = "res://addons/terrain_3d_tools/Shaders/Path/PathApplyHeight.glsl";
        private const string SHADER_APPLY_TEXTURE = "res://addons/terrain_3d_tools/Shaders/Path/PathApplyTexture.glsl";
        #endregion

        #region GPU Data Structures
        /// <summary>
        /// Represents a path segment for GPU processing.
        /// Must match GLSL struct layout exactly.
        /// </summary>
        private struct GpuPathSegment
        {
            public Vector2 StartPos;      // 8 bytes
            public float StartHeight;     // 4 bytes
            public float StartDistance;   // 4 bytes (distance along path)

            public Vector2 EndPos;        // 8 bytes
            public float EndHeight;       // 4 bytes
            public float EndDistance;     // 4 bytes

            public Vector2 Tangent;       // 8 bytes (normalized direction)
            public Vector2 Perpendicular; // 8 bytes (normalized right vector)

            // Total: 48 bytes (12 floats), aligned to 16 bytes

            public float[] ToFloatArray()
            {
                return new float[]
                {
                    StartPos.X, StartPos.Y, StartHeight, StartDistance,
                    EndPos.X, EndPos.Y, EndHeight, EndDistance,
                    Tangent.X, Tangent.Y, Perpendicular.X, Perpendicular.Y
                };
            }
        }

        public const int GPU_SEGMENT_FLOAT_COUNT = 12;
        #endregion

        #region SDF and Mask Generation Commands
        /// <summary>
        /// Creates commands to generate the SDF (signed distance field) texture.
        /// This is the foundation for all path operations.
        /// </summary>
        public (Action<long> commands, List<Rid> tempRids, List<string> shaderPaths) CreateSDFCommands()
        {
            if (_curve == null || _curve.PointCount < 2)
            {
                DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.MaskGeneration,
                    $"Skipping SDF generation - curve has {_curve?.PointCount ?? 0} points (need at least 2)");
                return (null, new List<Rid>(), new List<string>());
            }

            var segments = GenerateSegmentData();
            if (segments.Length == 0)
            {
                DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.MaskGeneration,
                    "Skipping SDF generation - no valid segments generated");
                return (null, new List<Rid>(), new List<string>());
            }

            // DEBUG: Log performance-relevant info
            int segmentCount = segments.Length / GPU_SEGMENT_FLOAT_COUNT;
            long pixelCount = (long)Size.X * Size.Y;
            long operations = pixelCount * segmentCount;

            DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.PerformanceMetrics,
                $"SDF: {Size.X}x{Size.Y} ({pixelCount:N0} px), {segmentCount} segments, Resolution: {_resolution}, Adaptive: {_adaptiveResolution}");

            var operation = new AsyncComputeOperation(SHADER_PATH_SDF);

            // Binding 0: Output SDF texture
            operation.BindStorageImage(0, _sdfTextureRid);

            // Binding 1: Output zone data texture
            operation.BindStorageImage(1, _zoneDataTextureRid);

            // Binding 2: Path segment data
            byte[] segmentBytes = GpuUtils.FloatArrayToBytes(segments);
            operation.BindTemporaryStorageBuffer(2, segmentBytes);

            // Binding 3: Profile data
            byte[] profileBytes = _profile?.ToGpuBuffer() ?? new byte[64];
            operation.BindTemporaryStorageBuffer(3, profileBytes);

            // Push constants
            Vector2 layerCenter = new Vector2(GlobalPosition.X, GlobalPosition.Z);
            Vector2 layerMin = layerCenter - new Vector2(Size.X, Size.Y) * 0.5f;

            var pushConstants = GpuUtils.CreatePushConstants()
                .Add(Size.X)                        // layer_width - 4 bytes (offset 0)
                .Add(Size.Y)                        // layer_height - 4 bytes (offset 4)
                .Add(layerMin)                      // layer_min_world (vec2) - 8 bytes (offset 8)
                .Add(segmentCount)                  // segment_count - 4 bytes (offset 16)
                .Add(_profile?.EnabledZoneCount ?? 1)  // zone_count - 4 bytes (offset 20)
                .Add(_profile?.HalfWidth ?? 2.0f)  // profile_half_width - 4 bytes (offset 24)
                .Add(_cornerSmoothing)              // corner_smoothing - 4 bytes (offset 28)
                .Add(_smoothCorners ? 1 : 0)        // smooth_corners - 4 bytes (offset 32)
                .AddPadding(12)                     // padding to 48 bytes (offset 36 + 12 = 48)
                .Build();

            operation.SetPushConstants(pushConstants);

            uint groupsX = (uint)((Size.X + 7) / 8);
            uint groupsY = (uint)((Size.Y + 7) / 8);

            DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.GpuDispatches,
                $"SDF Dispatch: {groupsX}x{groupsY} groups");

            return (
                operation.CreateDispatchCommands(groupsX, groupsY),
                operation.GetTemporaryRids(),
                new List<string> { SHADER_PATH_SDF }
            );
        }

        /// <summary>
        /// Creates commands to generate the final mask texture from SDF and profile.
        /// </summary>
        public (Action<long> commands, List<Rid> tempRids, List<string> shaderPaths) CreateMaskCommands()
        {
            if (!_sdfTextureRid.IsValid || !layerTextureRID.IsValid)
            {
                return (null, new List<Rid>(), new List<string>());
            }

            var operation = new AsyncComputeOperation(SHADER_PATH_MASK);

            // Binding 0: Output mask texture
            operation.BindStorageImage(0, layerTextureRID);

            // Binding 1: Input SDF texture
            operation.BindSamplerWithTexture(1, _sdfTextureRid);

            // Binding 2: Input zone data texture
            operation.BindSamplerWithTexture(2, _zoneDataTextureRid);

            // Binding 3: Profile data
            byte[] profileBytes = _profile?.ToGpuBuffer() ?? new byte[64];
            operation.BindTemporaryStorageBuffer(3, profileBytes);

            // Binding 4: Zone curve data (baked curves for each zone)
            byte[] curveData = BakeZoneCurves();
            operation.BindTemporaryStorageBuffer(4, curveData);


            var pushConstants = GpuUtils.CreatePushConstants()
                .Add(Size.X)                           // 4 bytes (offset 0)
                .Add(Size.Y)                           // 4 bytes (offset 4)
                .Add(_profile?.EnabledZoneCount ?? 1)  // 4 bytes (offset 8)
                .Add(_profile?.HalfWidth ?? 2.0f)      // 4 bytes (offset 12)
                                                       //.AddPadding(32)                        // padding to 48 bytes (offset 16 + 32 = 48)
                .Build();

            operation.SetPushConstants(pushConstants);

            uint groupsX = (uint)((Size.X + 7) / 8);
            uint groupsY = (uint)((Size.Y + 7) / 8);

            return (
                operation.CreateDispatchCommands(groupsX, groupsY),
                operation.GetTemporaryRids(),
                new List<string> { SHADER_PATH_MASK }
            );
        }

        /// <summary>
        /// Combined command for path layer mask generation.
        /// Called by LayerMaskPipeline.
        /// </summary>
        public (Action<long> commands, List<Rid> tempRids, List<string> shaderPaths) CreatePathMaskCommands()
        {
            var allCommands = new List<Action<long>>();
            var allTempRids = new List<Rid>();
            var allShaderPaths = new List<string>();

            // Step 1: Generate SDF
            var (sdfCmd, sdfRids, sdfShaders) = CreateSDFCommands();
            if (sdfCmd != null)
            {
                allCommands.Add(sdfCmd);
                allTempRids.AddRange(sdfRids);
                allShaderPaths.AddRange(sdfShaders);
            }

            // Step 2: Generate mask from SDF
            var (maskCmd, maskRids, maskShaders) = CreateMaskCommands();
            if (maskCmd != null)
            {
                allCommands.Add(maskCmd);
                allTempRids.AddRange(maskRids);
                allShaderPaths.AddRange(maskShaders);
            }

            if (allCommands.Count == 0)
            {
                return (null, new List<Rid>(), new List<string>());
            }

            // Combine with barriers
            Action<long> combined = (computeList) =>
            {
                for (int i = 0; i < allCommands.Count; i++)
                {
                    allCommands[i]?.Invoke(computeList);
                    if (i < allCommands.Count - 1)
                    {
                        Gpu.Rd.ComputeListAddBarrier(computeList);
                    }
                }
            };

            return (combined, allTempRids, allShaderPaths);
        }

        /// <summary>
        /// Creates commands to generate height data texture.
        /// </summary>
        public (Action<long> commands, List<Rid> tempRids, List<string> shaderPaths) CreatePathHeightDataCommands()
        {
            // For now, height data is embedded in the SDF generation
            // This method exists for compatibility with the pipeline
            return (null, new List<Rid>(), new List<string>());
        }
        #endregion

        #region Region Application Commands
        public override (Action<long> commands, List<Rid> tempRids, List<string>) CreateApplyHeightCommands(
            Vector2I regionCoords,
            RegionData regionData,
            int regionSize,
            Vector2 regionMinWorld,
            Vector2 regionSizeWorld)
        {
            if (!ModifiesHeight || !layerTextureRID.IsValid || !regionData.HeightMap.IsValid)
            {
                return (null, new List<Rid>(), new List<string>());
            }

            // Early exit if curve has insufficient points
            if (_curve == null || _curve.PointCount < 2)
            {
                DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.RegionCompositing,
                    $"Skipping height application - curve has {_curve?.PointCount ?? 0} points");
                return (null, new List<Rid>(), new List<string>());
            }

            // Generate segment data and validate
            var segments = GenerateSegmentData();
            if (segments.Length == 0)
            {
                DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.RegionCompositing,
                    "[PathLayer] Skipping height application - no valid segments generated");
                return (null, new List<Rid>(), new List<string>());
            }

            // Check overlap
            Vector2 maskCenter = new Vector2(GlobalPosition.X, GlobalPosition.Z);
            OverlapResult? overlap = RegionMaskOverlap.GetRegionMaskOverlap(
                regionCoords, regionSize, maskCenter, Size);

            if (!overlap.HasValue)
            {
                return (null, new List<Rid>(), new List<string>());
            }

            var o = overlap.Value;
            var operation = new AsyncComputeOperation(SHADER_APPLY_HEIGHT);

            // Binding 0: Region heightmap (read/write)
            operation.BindStorageImage(0, regionData.HeightMap);

            // Binding 1: Layer mask (influence)
            operation.BindSamplerWithTexture(1, layerTextureRID);

            // Binding 2: SDF texture (for height sampling)
            operation.BindSamplerWithTexture(2, _sdfTextureRid);

            // Binding 3: Zone data texture
            operation.BindSamplerWithTexture(3, _zoneDataTextureRid);

            // Binding 4: Profile data
            byte[] profileBytes = _profile?.ToGpuBuffer() ?? new byte[64];
            operation.BindTemporaryStorageBuffer(4, profileBytes);

            // Binding 5: Segment data (for height interpolation) - validated above
            byte[] segmentBytes = GpuUtils.FloatArrayToBytes(segments);
            operation.BindTemporaryStorageBuffer(5, segmentBytes);

            int segmentCount = segments.Length / GPU_SEGMENT_FLOAT_COUNT;

            var pushConstants = GpuUtils.CreatePushConstants()
                .Add(o.RegionMin)                      // ivec2 - 8 bytes (offset 0)
                .Add(o.RegionMax)                      // ivec2 - 8 bytes (offset 8)
                .Add(o.MaskMin)                        // ivec2 - 8 bytes (offset 16)
                .Add(o.MaskMax)                        // ivec2 - 8 bytes (offset 24)
                .Add(Size.X).Add(Size.Y)               // 8 bytes (offset 32)
                .Add(segmentCount)                     // 4 bytes (offset 40)
                .Add(_profile?.EnabledZoneCount ?? 1)  // 4 bytes (offset 44)
                .Add(_worldHeightScale)                // 4 bytes (offset 48)
                .AddPadding(12)                        // padding to 64 bytes (offset 52 + 12 = 64)
                .Build();

            operation.SetPushConstants(pushConstants);

            uint groupsX = (uint)((regionSize + 7) / 8);
            uint groupsY = (uint)((regionSize + 7) / 8);

            return (
                operation.CreateDispatchCommands(groupsX, groupsY),
                operation.GetTemporaryRids(),
                new List<string> { SHADER_APPLY_HEIGHT }
            );
        }

        public override (Action<long> commands, List<Rid> tempRids, List<string>) CreateApplyTextureCommands(
    Vector2I regionCoords,
    RegionData regionData,
    int regionSize,
    Vector2 regionMinWorld,
    Vector2 regionSizeWorld)
        {
            if (!ModifiesTexture || !layerTextureRID.IsValid || !regionData.ControlMap.IsValid)
            {
                return (null, new List<Rid>(), new List<string>());
            }

            // Early exit if curve has insufficient points
            if (_curve == null || _curve.PointCount < 2)
            {
                DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.RegionCompositing,
                    $"Skipping texture application - curve has {_curve?.PointCount ?? 0} points");
                return (null, new List<Rid>(), new List<string>());
            }

            Vector2 maskCenter = new Vector2(GlobalPosition.X, GlobalPosition.Z);
            OverlapResult? overlap = RegionMaskOverlap.GetRegionMaskOverlap(
                regionCoords, regionSize, maskCenter, Size);

            if (!overlap.HasValue)
            {
                return (null, new List<Rid>(), new List<string>());
            }

            var o = overlap.Value;
            var operation = new AsyncComputeOperation(SHADER_APPLY_TEXTURE);

            // Binding 0: Region control map (read/write)
            operation.BindStorageImage(0, regionData.ControlMap);

            // Binding 1: Layer mask
            operation.BindSamplerWithTexture(1, layerTextureRID);

            // Binding 2: Zone data texture
            operation.BindSamplerWithTexture(2, _zoneDataTextureRid);

            // Binding 3: Profile data (includes texture IDs per zone)
            byte[] profileBytes = _profile?.ToGpuBuffer() ?? new byte[64];
            operation.BindTemporaryStorageBuffer(3, profileBytes);

            var pushConstants = GpuUtils.CreatePushConstants()
                .Add(o.RegionMin)
                .Add(o.RegionMax)
                .Add(o.MaskMin)
                .Add(o.MaskMax)
                .Add(Size.X).Add(Size.Y)
                .Add(_profile?.EnabledZoneCount ?? 1)
                .AddPadding(4)
                .Build();

            operation.SetPushConstants(pushConstants);

            uint groupsX = (uint)((regionSize + 7) / 8);
            uint groupsY = (uint)((regionSize + 7) / 8);

            return (
                operation.CreateDispatchCommands(groupsX, groupsY),
                operation.GetTemporaryRids(),
                new List<string> { SHADER_APPLY_TEXTURE }
            );
        }

        public override (Action<long> commands, List<Rid> tempRids, List<string>) CreateApplyRegionCommands(
            Vector2I regionCoords,
            RegionData regionData,
            int regionSize,
            Vector2 regionMinWorld,
            Vector2 regionSizeWorld)
        {
            var allCommands = new List<Action<long>>();
            var allTempRids = new List<Rid>();
            var allShaderPaths = new List<string>();

            // Height first
            if (ModifiesHeight)
            {
                var (heightCmd, heightRids, heightShaders) = CreateApplyHeightCommands(
                    regionCoords, regionData, regionSize, regionMinWorld, regionSizeWorld);

                if (heightCmd != null)
                {
                    allCommands.Add(heightCmd);
                    allTempRids.AddRange(heightRids);
                    allShaderPaths.AddRange(heightShaders);
                }
            }

            // Then texture
            if (ModifiesTexture)
            {
                var (texCmd, texRids, texShaders) = CreateApplyTextureCommands(
                    regionCoords, regionData, regionSize, regionMinWorld, regionSizeWorld);

                if (texCmd != null)
                {
                    allCommands.Add(texCmd);
                    allTempRids.AddRange(texRids);
                    allShaderPaths.AddRange(texShaders);
                }
            }

            if (allCommands.Count == 0)
            {
                return (null, new List<Rid>(), new List<string>());
            }

            Action<long> combined = (computeList) =>
            {
                for (int i = 0; i < allCommands.Count; i++)
                {
                    allCommands[i]?.Invoke(computeList);
                    if (i < allCommands.Count - 1)
                    {
                        Gpu.Rd.ComputeListAddBarrier(computeList);
                    }
                }
            };

            return (combined, allTempRids, allShaderPaths);
        }
        #endregion

        #region Data Generation Helpers
        /// <summary>
        /// Generate segment data for GPU from sampled curve points.
        /// </summary>
        private float[] GenerateSegmentData()
        {
            // Check if size or position changed since last generation
            if (_lastSegmentDataSize != Size)
            {
                _segmentDataDirty = true;
            }

            // Return cached data if still valid
            if (!_segmentDataDirty && _cachedSegmentData != null && _cachedSegmentData.Length > 0)
            {
                DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.MaskGeneration,
                    $"Using cached segment data ({_cachedSegmentData.Length / GPU_SEGMENT_FLOAT_COUNT} segments)");
                return _cachedSegmentData;
            }

            Vector3[] points = GetSampledPoints();
            if (points.Length < 2)
            {
                _cachedSegmentData = Array.Empty<float>();
                _segmentDataDirty = false;
                return _cachedSegmentData;
            }

            Vector2 layerCenter = new Vector2(GlobalPosition.X, GlobalPosition.Z);
            Vector2 layerMin = layerCenter - new Vector2(Size.X, Size.Y) * 0.5f;

            var segments = new List<float>();
            float accumulatedDist = 0f;

            for (int i = 0; i < points.Length - 1; i++)
            {
                Vector3 p1 = points[i];
                Vector3 p2 = points[i + 1];

                Vector2 start2D = new Vector2(p1.X, p1.Z) - layerMin;
                Vector2 end2D = new Vector2(p2.X, p2.Z) - layerMin;

                float segmentLength = start2D.DistanceTo(end2D);

                Vector2 tangent = (end2D - start2D).Normalized();
                Vector2 perpendicular = new Vector2(-tangent.Y, tangent.X);

                float startHeight = p1.Y / _worldHeightScale;
                float endHeight = p2.Y / _worldHeightScale;

                segments.Add(start2D.X);
                segments.Add(start2D.Y);
                segments.Add(startHeight);
                segments.Add(accumulatedDist);

                segments.Add(end2D.X);
                segments.Add(end2D.Y);
                segments.Add(endHeight);
                segments.Add(accumulatedDist + segmentLength);

                segments.Add(tangent.X);
                segments.Add(tangent.Y);
                segments.Add(perpendicular.X);
                segments.Add(perpendicular.Y);

                accumulatedDist += segmentLength;
            }

            _cachedSegmentData = segments.ToArray();
            _lastSegmentDataSize = Size;
            _segmentDataDirty = false;

            DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.MaskGeneration,
                $"Generated {_cachedSegmentData.Length / GPU_SEGMENT_FLOAT_COUNT} segments (fresh)");

            return _cachedSegmentData;
        }

        /// <summary>
        /// Bake all zone height curves into a GPU buffer.
        /// </summary>
        private byte[] BakeZoneCurves()
        {
            const int CURVE_RESOLUTION = 64;

            var data = new List<float>();

            if (_profile?.Zones == null)
            {
                // Default flat curve
                for (int i = 0; i < CURVE_RESOLUTION; i++)
                {
                    data.Add(1.0f);
                }
                return GpuUtils.FloatArrayToBytes(data.ToArray());
            }

            foreach (var zone in _profile.Zones.Where(z => z != null && z.Enabled))
            {
                var curve = zone.HeightCurve;

                for (int i = 0; i < CURVE_RESOLUTION; i++)
                {
                    float t = (float)i / (CURVE_RESOLUTION - 1);
                    float value = curve?.SampleBaked(t) ?? 1.0f;
                    data.Add(value);
                }
            }

            // Ensure we have at least one curve
            while (data.Count < CURVE_RESOLUTION)
            {
                data.Add(1.0f);
            }

            return GpuUtils.FloatArrayToBytes(data.ToArray());
        }
        #endregion
    }
}