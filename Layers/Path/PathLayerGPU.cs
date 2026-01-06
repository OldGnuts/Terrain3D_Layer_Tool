// /Layers/PathLayerGPU.cs
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
    ///


    /// GPU operations for PathLayer. Handles shader dispatches for SDF generation,
    /// mask creation, and region application.
    ///

    public partial class PathLayer
    {
        #region Shader Paths
        private const string SHADER_PATH_SDF = "res://addons/terrain_3d_tools/Shaders/Path/PathSDF.glsl";
        private const string SHADER_PATH_MASK = "res://addons/terrain_3d_tools/Shaders/Path/PathMask.glsl";
        private const string SHADER_APPLY_HEIGHT = "res://addons/terrain_3d_tools/Shaders/Path/PathApplyHeight.glsl";
        private const string SHADER_APPLY_TEXTURE = "res://addons/terrain_3d_tools/Shaders/Path/PathApplyTexture.glsl";
        #endregion
        #region GPU Constants
        public const int GPU_SEGMENT_FLOAT_COUNT = 12;
        #endregion

        #region Pipeline Integration (The Missing Link)
        /// <summary>
        /// Generates GPU commands for mask generation using a provided state snapshot.
        /// Called by LayerMaskPipeline (Phase 5) on a worker thread.
        /// </summary>
        public (Action<long> commands, List<Rid> tempRids, List<string> shaderPaths) CreatePathMaskCommandsFromState(PathBakeState state)
        {
            var allCommands = new List<Action<long>>();
            var allTempRids = new List<Rid>();
            var allShaderPaths = new List<string>();

            // 1. SDF Generation
            var (sdfCmd, sdfRids, sdfShaders) = CreateSDFCommands(state);
            if (sdfCmd != null)
            {
                allCommands.Add(sdfCmd);
                allTempRids.AddRange(sdfRids);
                allShaderPaths.AddRange(sdfShaders);
            }

            // 2. Mask Generation
            var (maskCmd, maskRids, maskShaders) = CreateMaskCommands(state);
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

        #region SDF and Mask Generation Commands
        /// <summary>
        /// Creates SDF generation commands.
        /// <param name="explicitState">Optional state to use. If null, uses _activeBakeState.</param>
        /// </summary>
        public (Action<long> commands, List<Rid> tempRids, List<string> shaderPaths) CreateSDFCommands(PathBakeState explicitState = null)
        {
            var state = explicitState ?? _activeBakeState;

            if (state == null || state.Points == null || state.Points.Length < 2)
            {
                DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.MaskGeneration,
                    "Skipping SDF generation - No valid bake state available");
                return (null, new List<Rid>(), new List<string>());
            }

            // USE STATE RIDS, NOT CLASS FIELDS
            if (!state.SdfTextureRid.IsValid || !state.ZoneTextureRid.IsValid)
            {
                DebugManager.Instance?.LogWarning(DEBUG_CLASS_NAME,
                    "Skipping SDF generation - GPU resources not ready");
                return (null, new List<Rid>(), new List<string>());
            }

            var segments = GenerateSegmentsFromState(state);
            if (segments.Length == 0)
            {
                return (null, new List<Rid>(), new List<string>());
            }

            int segmentCount = segments.Length / GPU_SEGMENT_FLOAT_COUNT;
            var operation = new AsyncComputeOperation(SHADER_PATH_SDF);

            // Binding 0: Output SDF texture
            operation.BindStorageImage(0, state.SdfTextureRid); // <-- Use State RID

            // Binding 1: Output zone data texture
            operation.BindStorageImage(1, state.ZoneTextureRid); // <-- Use State RID

            // Binding 2: Path segment data
            byte[] segmentBytes = GpuUtils.FloatArrayToBytes(segments);
            operation.BindTemporaryStorageBuffer(2, segmentBytes);

            // Binding 3: Profile data
            operation.BindTemporaryStorageBuffer(3, state.ProfileGpuDataScaled);

            // ... (Push Constants calculation same as before)
            Vector2 maskWorldSize = state.MaskWorldMax - state.MaskWorldMin;
            float scaleX = maskWorldSize.X > 0.001f ? state.Size.X / maskWorldSize.X : 1.0f;
            float scaleY = maskWorldSize.Y > 0.001f ? state.Size.Y / maskWorldSize.Y : 1.0f;
            float avgScale = (scaleX + scaleY) / 2f;
            float profileHalfWidthPixels = state.ProfileHalfWidth * avgScale;
            float cornerSmoothingPixels = state.CornerSmoothing * avgScale;

            var pushConstants = GpuUtils.CreatePushConstants()
                .Add(state.Size.X)
                .Add(state.Size.Y)
                .Add(Vector2.Zero)
                .Add(segmentCount)
                .Add(state.EnabledZoneCount)
                .Add(profileHalfWidthPixels)
                .Add(cornerSmoothingPixels)
                .Add(state.SmoothCorners ? 1 : 0)
                .AddPadding(12)
                .Build();

            operation.SetPushConstants(pushConstants);

            uint groupsX = (uint)((state.Size.X + 7) / 8);
            uint groupsY = (uint)((state.Size.Y + 7) / 8);

            return (
                operation.CreateDispatchCommands(groupsX, groupsY),
                operation.GetTemporaryRids(),
                new List<string> { SHADER_PATH_SDF }
            );
        }

        /// <summary>
        /// Creates Mask generation commands.
        /// <param name="explicitState">Optional state to use. If null, uses _activeBakeState.</param>
        /// </summary>
        public (Action<long> commands, List<Rid> tempRids, List<string> shaderPaths) CreateMaskCommands(PathBakeState explicitState = null)
        {
            var state = explicitState ?? _activeBakeState;
            if (state == null) return (null, new List<Rid>(), new List<string>());

            // USE STATE RIDS
            if (!state.SdfTextureRid.IsValid || !state.ZoneTextureRid.IsValid || !state.LayerTextureRid.IsValid)
            {
                DebugManager.Instance?.LogWarning(DEBUG_CLASS_NAME,
                    "Skipping mask generation - GPU resources not ready");
                return (null, new List<Rid>(), new List<string>());
            }

            var operation = new AsyncComputeOperation(SHADER_PATH_MASK);

            operation.BindStorageImage(0, state.LayerTextureRid); // <-- Use State RID
            operation.BindSamplerWithTexture(1, state.SdfTextureRid); // <-- Use State RID
            operation.BindSamplerWithTexture(2, state.ZoneTextureRid); // <-- Use State RID

            operation.BindTemporaryStorageBuffer(3, state.ProfileGpuDataScaled);
            operation.BindTemporaryStorageBuffer(4, state.ZoneCurveData);

            Vector2 maskWorldSize = state.MaskWorldMax - state.MaskWorldMin;
            float scaleX = maskWorldSize.X > 0.001f ? state.Size.X / maskWorldSize.X : 1.0f;
            float scaleY = maskWorldSize.Y > 0.001f ? state.Size.Y / maskWorldSize.Y : 1.0f;
            float avgScale = (scaleX + scaleY) / 2f;
            float profileHalfWidthPixels = state.ProfileHalfWidth * avgScale;

            var pushConstants = GpuUtils.CreatePushConstants()
                .Add(state.Size.X)
                .Add(state.Size.Y)
                .Add(state.EnabledZoneCount)
                .Add(profileHalfWidthPixels)
                .Build();

            operation.SetPushConstants(pushConstants);

            uint groupsX = (uint)((state.Size.X + 7) / 8);
            uint groupsY = (uint)((state.Size.Y + 7) / 8);

            return (
                operation.CreateDispatchCommands(groupsX, groupsY),
                operation.GetTemporaryRids(),
                new List<string> { SHADER_PATH_MASK }
            );
        }

        #endregion

        #region Region Application Commands 

        /// <summary>
        /// Standard entry point for applying commands.
        /// Uses the internal _activeBakeState (Fall back behavior).
        /// </summary>
        public override (Action<long> commands, List<Rid> tempRids, List<string>) CreateApplyRegionCommands(
            Vector2I regionCoords,
            RegionData regionData,
            int regionSize,
            Vector2 regionMinWorld,
            Vector2 regionSizeWorld)
        {
            // If called without state, use the current active state
            // Note: In threaded pipelines, this is risky! Prefer CreateApplyRegionCommandsFromState
            return CreateApplyRegionCommandsFromState(
                _activeBakeState,
                regionCoords,
                regionData,
                regionSize,
                regionMinWorld,
                regionSizeWorld);
        }

        /// <summary>
        /// Explicit entry point using a captured state snapshot.
        /// Thread-Safe for use in Worker Threads.
        /// </summary>
        public (Action<long> commands, List<Rid> tempRids, List<string>) CreateApplyRegionCommandsFromState(
            PathBakeState state,
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
                    state, regionCoords, regionData, regionSize, regionMinWorld, regionSizeWorld);

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
                    state, regionCoords, regionData, regionSize, regionMinWorld, regionSizeWorld);

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

        // Helper wrappers to match existing signatures but accept State
        private (Action<long>, List<Rid>, List<string>) CreateApplyHeightCommands(
            PathBakeState state, Vector2I rc, RegionData rd, int rs, Vector2 min, Vector2 size)
        {
            // Just call the existing override logic, but pass the state explicitly
            // I previously provided CreateApplyHeightCommands logic that used 'state'.
            // This is just ensuring the plumbing connects.
            return CreateApplyHeightCommandsOverride(state, rc, rd, rs, min, size);
        }

        private (Action<long>, List<Rid>, List<string>) CreateApplyTextureCommands(
            PathBakeState state, Vector2I rc, RegionData rd, int rs, Vector2 min, Vector2 size)
        {
            return CreateApplyTextureCommandsOverride(state, rc, rd, rs, min, size);
        }

        // Renaming the Logic methods from previous step to ensure clarity
        // These contain the actual GPU logic we wrote in the previous step
        private (Action<long>, List<Rid>, List<string>) CreateApplyHeightCommandsOverride(
             PathBakeState state, Vector2I regionCoords, RegionData regionData, int regionSize, Vector2 regionMinWorld, Vector2 regionSizeWorld)
        {
            if (!ModifiesHeight || !regionData.HeightMap.IsValid) return (null, new List<Rid>(), new List<string>());
            if (state == null || state.Points == null || state.Points.Length < 2) return (null, new List<Rid>(), new List<string>());
            if (!state.SdfTextureRid.IsValid || !state.ZoneTextureRid.IsValid || !state.LayerTextureRid.IsValid) return (null, new List<Rid>(), new List<string>());

            OverlapResult? overlap = RegionMaskOverlap.GetRegionMaskOverlapFromBounds(
                regionCoords, regionSize, state.MaskWorldMin, state.MaskWorldMax, state.Size);

            if (!overlap.HasValue) return (null, new List<Rid>(), new List<string>());
            var o = overlap.Value;

            var segments = GenerateSegmentsFromState(state);
            if (segments.Length == 0) return (null, new List<Rid>(), new List<string>());

            var operation = new AsyncComputeOperation(SHADER_APPLY_HEIGHT);
            operation.BindStorageImage(0, regionData.HeightMap);
            operation.BindSamplerWithTexture(1, state.LayerTextureRid);
            operation.BindSamplerWithTexture(2, state.SdfTextureRid);
            operation.BindSamplerWithTexture(3, state.ZoneTextureRid);
            operation.BindTemporaryStorageBuffer(4, state.ProfileGpuDataUnscaled);
            operation.BindTemporaryStorageBuffer(5, GpuUtils.FloatArrayToBytes(segments));

            int segmentCount = segments.Length / GPU_SEGMENT_FLOAT_COUNT;
            var pushConstants = GpuUtils.CreatePushConstants()
                .Add(o.RegionMin).Add(o.RegionMax).Add(o.MaskMin).Add(o.MaskMax)
                .Add(state.Size.X).Add(state.Size.Y).Add(segmentCount).Add(state.EnabledZoneCount)
                .Add(state.WorldHeightScale).AddPadding(12).Build();
            operation.SetPushConstants(pushConstants);

            uint gx = (uint)((regionSize + 7) / 8);
            uint gy = (uint)((regionSize + 7) / 8);
            return (operation.CreateDispatchCommands(gx, gy), operation.GetTemporaryRids(), new List<string> { SHADER_APPLY_HEIGHT });
        }

        private (Action<long>, List<Rid>, List<string>) CreateApplyTextureCommandsOverride(
            PathBakeState state, Vector2I regionCoords, RegionData regionData, int regionSize, Vector2 regionMinWorld, Vector2 regionSizeWorld)
        {
            if (!ModifiesTexture) return (null, new List<Rid>(), new List<string>());
            if (!regionData.ControlMap.IsValid) return (null, new List<Rid>(), new List<string>());
            if (state == null || state.Points == null || state.Points.Length < 2) return (null, new List<Rid>(), new List<string>());
            if (!state.LayerTextureRid.IsValid || !state.ZoneTextureRid.IsValid) return (null, new List<Rid>(), new List<string>());

            OverlapResult? overlap = RegionMaskOverlap.GetRegionMaskOverlapFromBounds(
                regionCoords, regionSize, state.MaskWorldMin, state.MaskWorldMax, state.Size);

            if (!overlap.HasValue) return (null, new List<Rid>(), new List<string>());
            var o = overlap.Value;

            var operation = new AsyncComputeOperation(SHADER_APPLY_TEXTURE);
            operation.BindStorageImage(0, regionData.ControlMap);
            operation.BindSamplerWithTexture(1, state.LayerTextureRid);
            operation.BindSamplerWithTexture(2, state.ZoneTextureRid);
            operation.BindTemporaryStorageBuffer(3, state.ProfileGpuDataUnscaled);

            var pushConstants = GpuUtils.CreatePushConstants()
                .Add(o.RegionMin).Add(o.RegionMax).Add(o.MaskMin).Add(o.MaskMax)
                .Add(state.Size.X).Add(state.Size.Y).Add(state.EnabledZoneCount).AddPadding(4).Build();
            operation.SetPushConstants(pushConstants);

            uint gx = (uint)((regionSize + 7) / 8);
            uint gy = (uint)((regionSize + 7) / 8);
            return (operation.CreateDispatchCommands(gx, gy), operation.GetTemporaryRids(), new List<string> { SHADER_APPLY_TEXTURE });
        }
        #endregion

        #region Exclusion Map Commands
        // /Layers/PathLayer.cs
        // ADD this method override to PathLayer class

        /// <summary>
        /// Writes the path's influence mask to the region's exclusion map.
        /// Uses MAX blend mode so overlapping paths don't create artificial gaps.
        /// </summary>
        public override (Action<long> commands, List<Rid> tempRids, List<string> shaders) CreateWriteExclusionCommands(
            Vector2I regionCoords,
            RegionData regionData,
            int regionSize,
            Vector2 regionMinWorld,
            Vector2 regionSizeWorld)
        {
            // Use the captured bake state for thread safety
            var bakeState = GetActiveBakeState();
            if (bakeState == null || !bakeState.SdfTextureRid.IsValid)
            {
                return (null, new List<Rid>(), new List<string>());
            }

            // Check overlap
            var overlap = RegionMaskOverlap.GetRegionMaskOverlap(
                regionCoords,
                regionSize,
                (bakeState.MaskWorldMin + bakeState.MaskWorldMax) * 0.5f,
                bakeState.Size);

            if (!overlap.HasValue)
            {
                return (null, new List<Rid>(), new List<string>());
            }

            var o = overlap.Value;
            var shaderPath = "res://addons/terrain_3d_tools/Shaders/Layers/ExclusionMapWrite.glsl";
            var operation = new AsyncComputeOperation(shaderPath);

            // Get or create exclusion map
            var exclusionMap = regionData.GetOrCreateExclusionMap(regionSize);

            // Bind textures
            operation.BindStorageImage(0, exclusionMap);                    // Output (read-write for MAX)
            operation.BindSamplerWithTexture(1, bakeState.SdfTextureRid);   // Path SDF (influence source)

            // Calculate influence falloff based on profile width
            float influenceWidth = _profile?.TotalWidth ?? 4.0f;

            var pushConstants = GpuUtils.CreatePushConstants()
                .Add(o.RegionMin.X).Add(o.RegionMin.Y)
                .Add(o.RegionMax.X).Add(o.RegionMax.Y)
                .Add(o.MaskMin.X).Add(o.MaskMin.Y)
                .Add(o.MaskMax.X).Add(o.MaskMax.Y)
                .Add(influenceWidth)      // Influence radius
                .Add(1.0f)                // Exclusion strength (1.0 = full exclusion on path)
                .AddPadding(8)
                .Build();

            operation.SetPushConstants(pushConstants);

            uint groupsX = (uint)((regionSize + 7) / 8);
            uint groupsY = (uint)((regionSize + 7) / 8);

            return (
                operation.CreateDispatchCommands(groupsX, groupsY),
                operation.GetTemporaryRids(),
                new List<string> { shaderPath });
        }
        #endregion
    }
}