// /Brushes/BrushComputeDispatcher.cs
using Godot;
using System;
using System.Collections.Generic;
using Terrain3DTools.Core;
using Terrain3DTools.Core.Debug;
using Terrain3DTools.Utils;

namespace Terrain3DTools.Brushes
{
    /// <summary>
    /// Handles GPU compute dispatch for brush operations.
    /// Uses batched dispatch to minimize resource creation and improve performance.
    /// Always performs dual-write: to edit buffer (for undo) and region map (for display).
    /// </summary>
    public static class BrushComputeDispatcher
    {
        private const string DEBUG_CLASS_NAME = "BrushComputeDispatcher";

        // Shader paths
        private const string HEIGHT_BRUSH_SHADER = "res://addons/terrain_3d_tools/Shaders/Brushes/height_brush.glsl";
        private const string HEIGHT_SMOOTH_SHADER = "res://addons/terrain_3d_tools/Shaders/Brushes/height_smooth_brush.glsl";
        //private const string TEXTURE_BRUSH_SHADER = "res://addons/terrain_3d_tools/Shaders/Brushes/texture_brush.glsl";
        private const string TEXTURE_INTENT_SHADER = "res://addons/terrain_3d_tools/Shaders/Brushes/texture_brush_intent.glsl";
        private const string TEXTURE_APPLY_SHADER = "res://addons/terrain_3d_tools/Shaders/ManualEdit/apply_texture_edit.glsl";
        private const string EXCLUSION_BRUSH_SHADER = "res://addons/terrain_3d_tools/Shaders/Brushes/exclusion_brush.glsl";

        // Batching state
        private static bool _batchActive = false;
        private static long _currentComputeList = -1;
        private static List<Rid> _batchTempRids = new();
        private static int _dispatchCount = 0;
        private static bool _needsBarrier = false;

        static BrushComputeDispatcher()
        {
            DebugManager.Instance?.RegisterClass(DEBUG_CLASS_NAME);
        }

        #region Batch Management

        public static void BeginBatch()
        {
            if (_batchActive)
            {
                DebugManager.Instance?.LogWarning(DEBUG_CLASS_NAME,
                    "BeginBatch called while batch already active");
                return;
            }

            AsyncGpuTaskManager.Instance?.SyncIfNeeded();

            _batchActive = true;
            _currentComputeList = Gpu.ComputeListBegin();
            _batchTempRids.Clear();
            _dispatchCount = 0;
            _needsBarrier = false;

            DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.GpuDispatches,
                "Batch started");
        }

        public static void EndBatch()
        {
            if (!_batchActive)
                return;

            _batchActive = false;

            if (_dispatchCount > 0)
            {
                Gpu.ComputeListEnd();
                Gpu.Submit();
                AsyncGpuTaskManager.Instance?.MarkPendingSubmission();

                DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.GpuDispatches,
                    $"Batch ended: {_dispatchCount} dispatches, {_batchTempRids.Count} temp resources");
            }
            else
            {
                // No dispatches - just close the compute list without submit
                Gpu.ComputeListEnd();

                DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.GpuDispatches,
                    "Batch ended: 0 dispatches, no submit");
            }

            if (_batchTempRids.Count > 0)
            {
                AsyncGpuTaskManager.Instance?.QueueCleanup(_batchTempRids, null);
                _batchTempRids = new List<Rid>();
            }

            _currentComputeList = -1;
            _dispatchCount = 0;
            _needsBarrier = false;
        }

        private static bool EnsureBatch()
        {
            if (_batchActive)
                return false;

            BeginBatch();
            return true;
        }

        #endregion

        #region Height Brush

        /// <summary>
        /// Dispatches a height brush dab. Always writes to both heightDelta and heightMap.
        /// </summary>
        public static Rect2I DispatchHeightBrush(
            Rid heightDeltaRid,
            Rid heightMapRid,
            Vector2I regionCoords,
            int regionSize,
            Vector3 brushCenter,
            float brushRadius,
            float strength,
            float heightDelta,
            int falloffType,
            bool isCircle)
        {
            if (!heightDeltaRid.IsValid || !heightMapRid.IsValid)
            {
                DebugManager.Instance?.LogError(DEBUG_CLASS_NAME,
                    $"Invalid RIDs: heightDelta={heightDeltaRid.IsValid}, heightMap={heightMapRid.IsValid}");
                return new Rect2I();
            }

            var (bounds, groupsX, groupsY) = CalculateDispatchBounds(
                brushCenter, brushRadius, regionCoords, regionSize);

            if (groupsX == 0 || groupsY == 0)
                return bounds;

            bool ownsBatch = EnsureBatch();

            try
            {
                var op = new AsyncComputeOperation(HEIGHT_BRUSH_SHADER);
                if (!op.IsValid())
                {
                    DebugManager.Instance?.LogError(DEBUG_CLASS_NAME, "Failed to create height brush compute operation");
                    return bounds;
                }

                op.BindStorageImage(0, heightDeltaRid);
                op.BindStorageImage(1, heightMapRid);

                // 12 values = 48 bytes, pad to 64 bytes (16 more)
                var pushConstants = GpuUtils.CreatePushConstants()
                    .Add(brushCenter.X)
                    .Add(brushCenter.Z)
                    .Add(brushRadius)
                    .Add(heightDelta)
                    .Add(strength)
                    .Add(falloffType)
                    .Add(bounds.Position.X)
                    .Add(bounds.Position.Y)
                    .Add(regionCoords.X * regionSize)
                    .Add(regionCoords.Y * regionSize)
                    .Add(regionSize)
                    .Add(isCircle ? 1 : 0)
                    .AddPadding(16)  // Pad from 48 to 64 bytes
                    .Build();

                op.SetPushConstants(pushConstants);
                AddDispatchToBatch(op, groupsX, groupsY);
            }
            finally
            {
                if (ownsBatch)
                    EndBatch();
            }

            return bounds;
        }

        #endregion

        #region Height Smooth Brush

        /// <summary>
        /// Dispatches a height smooth brush dab. Reads from and writes to heightMap.
        /// </summary>
        public static Rect2I DispatchHeightSmoothBrush(
            Rid heightDeltaRid,
            Rid heightMapRid,
            Vector2I regionCoords,
            int regionSize,
            Vector3 brushCenter,
            float brushRadius,
            float strength,
            int kernelSize,
            int falloffType,
            bool isCircle)
        {
            if (!heightDeltaRid.IsValid || !heightMapRid.IsValid)
            {
                DebugManager.Instance?.LogError(DEBUG_CLASS_NAME,
                    $"Invalid RIDs: heightDelta={heightDeltaRid.IsValid}, heightMap={heightMapRid.IsValid}");
                return new Rect2I();
            }

            var (bounds, groupsX, groupsY) = CalculateDispatchBounds(
                brushCenter, brushRadius, regionCoords, regionSize);

            if (groupsX == 0 || groupsY == 0)
                return bounds;

            bool ownsBatch = EnsureBatch();

            try
            {
                var op = new AsyncComputeOperation(HEIGHT_SMOOTH_SHADER);
                if (!op.IsValid())
                {
                    DebugManager.Instance?.LogError(DEBUG_CLASS_NAME, "Failed to create height smooth compute operation");
                    return bounds;
                }

                op.BindStorageImage(0, heightDeltaRid);
                op.BindStorageImage(1, heightMapRid);

                // 12 values = 48 bytes, pad to 64 bytes
                var pushConstants = GpuUtils.CreatePushConstants()
                    .Add(brushCenter.X)
                    .Add(brushCenter.Z)
                    .Add(brushRadius)
                    .Add(strength)
                    .Add(falloffType)
                    .Add(kernelSize)
                    .Add(bounds.Position.X)
                    .Add(bounds.Position.Y)
                    .Add(regionCoords.X * regionSize)
                    .Add(regionCoords.Y * regionSize)
                    .Add(regionSize)
                    .Add(isCircle ? 1 : 0)
                    .AddPadding(16)  // Pad from 48 to 64 bytes
                    .Build();

                op.SetPushConstants(pushConstants);
                AddDispatchToBatch(op, groupsX, groupsY);
            }
            finally
            {
                if (ownsBatch)
                    EndBatch();
            }

            return bounds;
        }

        #endregion

        #region Texture Brush - Unified Intent+Apply Architecture

        /// <summary>
        /// Dispatches texture brush with unified intent+apply architecture.
        /// Two-pass approach:
        /// 1. Intent shader: accumulates user paint intent into TextureEdit buffer
        /// 2. Apply shader: computes result from intent + current ControlMap state
        /// 
        /// This ensures fast path and pipeline produce identical results.
        /// Supports concurrent overlay and base edits without overwriting each other.
        /// </summary>
        public static Rect2I DispatchTextureBrushUnified(
            Rid textureEditRid,
            Rid controlMapRid,
            Vector2I regionCoords,
            int regionSize,
            Vector3 brushCenter,
            float brushRadius,
            float strength,
            int falloffType,
            bool isCircle,
            int textureMode,
            int primaryTextureId,
            int secondaryTextureId,
            int targetBlend,
            int blendStep,
            int overlayMinVisibleBlend,
            int baseMaxVisibleBlend,
            int baseOverrideThreshold,
            int overlayOverrideThreshold,
            float blendReductionRate)
        {
            if (!textureEditRid.IsValid || !controlMapRid.IsValid)
            {
                DebugManager.Instance?.LogError(DEBUG_CLASS_NAME,
                    $"Invalid RIDs: textureEdit={textureEditRid.IsValid}, controlMap={controlMapRid.IsValid}");
                return new Rect2I();
            }

            var (bounds, groupsX, groupsY) = CalculateDispatchBounds(
                brushCenter, brushRadius, regionCoords, regionSize);

            if (groupsX == 0 || groupsY == 0)
                return bounds;

            bool ownsBatch = EnsureBatch();

            try
            {
                // === DISPATCH 1: Intent shader (writes to TextureEdit) ===
                var intentOp = new AsyncComputeOperation(TEXTURE_INTENT_SHADER);
                if (!intentOp.IsValid())
                {
                    DebugManager.Instance?.LogError(DEBUG_CLASS_NAME,
                        "Failed to create texture intent compute operation");
                    return bounds;
                }

                intentOp.BindStorageImage(0, textureEditRid);

                // Intent push constants: 16 values × 4 bytes = 64 bytes (16-byte aligned)
                var intentPushConstants = GpuUtils.CreatePushConstants()
                    .Add(brushCenter.X)                   // 0
                    .Add(brushCenter.Z)                   // 4
                    .Add(brushRadius)                     // 8
                    .Add(strength)                        // 12
                    .Add(falloffType)                     // 16
                    .Add(bounds.Position.X)               // 20
                    .Add(bounds.Position.Y)               // 24
                    .Add(regionCoords.X * regionSize)     // 28
                    .Add(regionCoords.Y * regionSize)     // 32
                    .Add(regionSize)                      // 36
                    .Add(isCircle ? 1 : 0)                // 40
                    .Add(textureMode)                     // 44
                    .Add(primaryTextureId)                // 48
                    .Add(secondaryTextureId)              // 52
                    .Add(targetBlend)                     // 56
                    .Add(blendStep)                       // 60
                    .Build();                             // Total: 64 bytes

                intentOp.SetPushConstants(intentPushConstants);
                AddDispatchToBatch(intentOp, groupsX, groupsY);

                // === DISPATCH 2: Apply shader (reads TextureEdit, writes ControlMap) ===
                var applyOp = new AsyncComputeOperation(TEXTURE_APPLY_SHADER);
                if (!applyOp.IsValid())
                {
                    DebugManager.Instance?.LogError(DEBUG_CLASS_NAME,
                        "Failed to create texture apply compute operation");
                    return bounds;
                }

                applyOp.BindStorageImage(0, controlMapRid);
                applyOp.BindStorageImage(1, textureEditRid);

                // Apply push constants: 8 values × 4 bytes = 32 bytes (16-byte aligned)
                //
                // Offset  Type   Name
                // ------  ----   ----
                // 0       int    u_region_size
                // 4       int    overlay_min_visible_blend
                // 8       int    base_max_visible_blend
                // 12      int    base_override_threshold
                // 16      int    overlay_override_threshold
                // 20      float  blend_reduction_rate
                // 24      int    _pad0
                // 28      int    _pad1
                // Total: 32 bytes

                var applyPushConstants = GpuUtils.CreatePushConstants()
                    .Add(regionSize)                      // 0
                    .Add(overlayMinVisibleBlend)          // 4
                    .Add(baseMaxVisibleBlend)             // 8
                    .Add(baseOverrideThreshold)           // 12
                    .Add(overlayOverrideThreshold)        // 16
                    .Add(blendReductionRate)              // 20
                    .AddPadding(8)                        // 24-31: padding to 32 bytes
                    .Build();

                applyOp.SetPushConstants(applyPushConstants);

                // Apply shader processes full region to catch all edited pixels
                uint applyGroupsX = (uint)((regionSize + 7) / 8);
                uint applyGroupsY = (uint)((regionSize + 7) / 8);
                AddDispatchToBatch(applyOp, applyGroupsX, applyGroupsY);

                DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.GpuDispatches,
                    $"Dispatched texture brush unified: intent({groupsX}x{groupsY}) + apply({applyGroupsX}x{applyGroupsY}) mode={textureMode}");
            }
            finally
            {
                if (ownsBatch)
                    EndBatch();
            }

            return bounds;
        }

        #endregion

        #region Exclusion Brush

        /// <summary>
        /// Dispatches an exclusion brush dab. Always writes to both exclusionEdit and exclusionMap.
        /// </summary>
        public static Rect2I DispatchExclusionBrush(
            Rid exclusionEditRid,
            Rid exclusionMapRid,
            Vector2I regionCoords,
            int regionSize,
            Vector3 brushCenter,
            float brushRadius,
            float strength,
            int falloffType,
            bool isCircle,
            bool addExclusion,
            bool accumulate)
        {
            if (!exclusionEditRid.IsValid || !exclusionMapRid.IsValid)
            {
                DebugManager.Instance?.LogError(DEBUG_CLASS_NAME,
                    $"Invalid RIDs: exclusionEdit={exclusionEditRid.IsValid}, exclusionMap={exclusionMapRid.IsValid}");
                return new Rect2I();
            }

            var (bounds, groupsX, groupsY) = CalculateDispatchBounds(
                brushCenter, brushRadius, regionCoords, regionSize);

            if (groupsX == 0 || groupsY == 0)
                return bounds;

            bool ownsBatch = EnsureBatch();

            try
            {
                var op = new AsyncComputeOperation(EXCLUSION_BRUSH_SHADER);
                if (!op.IsValid())
                {
                    DebugManager.Instance?.LogError(DEBUG_CLASS_NAME, "Failed to create exclusion brush compute operation");
                    return bounds;
                }

                op.BindStorageImage(0, exclusionEditRid);
                op.BindStorageImage(1, exclusionMapRid);

                // 13 values = 52 bytes, pad to 64 bytes (12 more)
                var pushConstants = GpuUtils.CreatePushConstants()
                    .Add(brushCenter.X)
                    .Add(brushCenter.Z)
                    .Add(brushRadius)
                    .Add(strength)
                    .Add(falloffType)
                    .Add(bounds.Position.X)
                    .Add(bounds.Position.Y)
                    .Add(regionCoords.X * regionSize)
                    .Add(regionCoords.Y * regionSize)
                    .Add(regionSize)
                    .Add(isCircle ? 1 : 0)
                    .Add(addExclusion ? 1 : 0)
                    .Add(accumulate ? 1 : 0)
                    .AddPadding(12)  // Pad from 52 to 64 bytes
                    .Build();

                op.SetPushConstants(pushConstants);
                AddDispatchToBatch(op, groupsX, groupsY);
            }
            finally
            {
                if (ownsBatch)
                    EndBatch();
            }

            return bounds;
        }

        #endregion


        #region Helpers

        private static (Rect2I bounds, uint groupsX, uint groupsY) CalculateDispatchBounds(
            Vector3 brushCenter,
            float brushRadius,
            Vector2I regionCoords,
            int regionSize)
        {
            float regionMinX = regionCoords.X * regionSize;
            float regionMinZ = regionCoords.Y * regionSize;

            int minPx = Mathf.Max(0, Mathf.FloorToInt(brushCenter.X - brushRadius - regionMinX));
            int maxPx = Mathf.Min(regionSize - 1, Mathf.CeilToInt(brushCenter.X + brushRadius - regionMinX));
            int minPz = Mathf.Max(0, Mathf.FloorToInt(brushCenter.Z - brushRadius - regionMinZ));
            int maxPz = Mathf.Min(regionSize - 1, Mathf.CeilToInt(brushCenter.Z + brushRadius - regionMinZ));

            if (minPx > maxPx || minPz > maxPz)
                return (new Rect2I(), 0, 0);

            int width = maxPx - minPx + 1;
            int height = maxPz - minPz + 1;

            var bounds = new Rect2I(minPx, minPz, width, height);

            uint groupsX = (uint)((width + 7) / 8);
            uint groupsY = (uint)((height + 7) / 8);

            return (bounds, groupsX, groupsY);
        }

        private static void AddDispatchToBatch(AsyncComputeOperation op, uint groupsX, uint groupsY)
        {
            if (!_batchActive || _currentComputeList < 0)
            {
                DebugManager.Instance?.LogError(DEBUG_CLASS_NAME,
                    "AddDispatchToBatch called without active batch");
                return;
            }

            if (_needsBarrier)
            {
                Gpu.Rd.ComputeListAddBarrier(_currentComputeList);
            }

            var commands = op.CreateDispatchCommands(groupsX, groupsY);
            commands.Invoke(_currentComputeList);

            _batchTempRids.AddRange(op.GetTemporaryRids());

            _dispatchCount++;
            _needsBarrier = true;
        }

        #endregion
    }
}