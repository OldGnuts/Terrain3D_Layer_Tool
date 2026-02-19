// /Core/Terrain3DIntegration.cs

using Godot;
using System.Collections.Generic;
using System.Linq;
using TokisanGames;
using Terrain3DTools.Core.Debug;
using Terrain3DTools.Utils;
using System;

namespace Terrain3DTools.Core
{
    /// <summary>
    /// Synchronizes our internal region data with Terrain3D.
    /// Reads texture data from GPU (synchronously) and pushes to Terrain3D's data structures.
    /// </summary>
    public class Terrain3DIntegration
    {
        private const string DEBUG_CLASS_NAME = "Terrain3DIntegration";

        private readonly Terrain3D _terrain3D;
        private readonly RegionMapManager _regionMapManager;
        private readonly int _regionSize;
        private readonly float _heightScale;

        private bool _isPushing = false;

        public bool HasPendingPushes => _isPushing;

        public Terrain3DIntegration(
            Terrain3D terrain3D,
            RegionMapManager regionMapManager,
            int regionSize,
            float heightScale = 1.0f)
        {
            _terrain3D = terrain3D;
            _regionMapManager = regionMapManager;
            _regionSize = regionSize;
            _heightScale = heightScale;

            DebugManager.Instance?.RegisterClass(DEBUG_CLASS_NAME);
        }

        /// <summary>
        /// Pushes updated regions to Terrain3D using synchronous texture reads.
        /// This method blocks until all regions are pushed, ensuring data consistency.
        /// </summary>
        public void PushRegionsToTerrain(
            HashSet<Vector2I> updatedRegions,
            List<Vector2I> allActiveRegions)
        {
            if (_terrain3D == null || updatedRegions.Count == 0)
            {
                DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.TerrainPush,
                    "Nothing to push");
                return;
            }

            _isPushing = true;

            // Sync here, right before readback
            // This ensures all pending GPU work is complete
            AsyncGpuTaskManager.Instance?.SyncIfNeeded();

            DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.TerrainPush,
                $"Starting synchronous push: {updatedRegions.Count} regions, on frame {Engine.GetProcessFrames()}");

            int successCount = 0;
            int failCount = 0;

            foreach (var regionCoord in updatedRegions)
            {
                bool success = PushRegionSync(regionCoord);
                if (success)
                    successCount++;
                else
                    failCount++;
            }

            DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.TerrainPush,
                $"Push complete: {successCount} succeeded, {failCount} failed, on frame {Engine.GetProcessFrames()}");

            FinalizeTerrainUpdate(allActiveRegions);

            _isPushing = false;
        }

        /// <summary>
        /// Synchronously reads region data from GPU and pushes to Terrain3D.
        /// Ensures GPU work is complete before reading.
        /// </summary>
        private bool PushRegionSync(Vector2I regionCoord)
        {
            var regionData = _regionMapManager.GetRegionData(regionCoord);
            if (regionData == null || !regionData.HeightMap.IsValid)
            {
                DebugManager.Instance?.LogError(DEBUG_CLASS_NAME,
                    $"Invalid region data for {regionCoord}");
                return false;
            }

            Rid scaledHeightRid = new();
            Image heightImage = null;
            Image controlImage = null;

            try
            {
                scaledHeightRid = Gpu.CreateTexture2D(
                    (uint)_regionSize,
                    (uint)_regionSize,
                    RenderingDevice.DataFormat.R32Sfloat,
                    RenderingDevice.TextureUsageBits.StorageBit |
                    RenderingDevice.TextureUsageBits.CanCopyFromBit);

                if (!scaledHeightRid.IsValid)
                {
                    DebugManager.Instance?.LogError(DEBUG_CLASS_NAME,
                        $"Failed to create scaled height texture for {regionCoord}");
                    return false;
                }

                DispatchHeightScale(regionData.HeightMap, scaledHeightRid);

                // Sync here, right before readback
                // This ensures all pending GPU work is complete
                AsyncGpuTaskManager.Instance?.SyncIfNeeded();

                byte[] heightData = Gpu.Rd.TextureGetData(scaledHeightRid, 0);
                if (heightData == null || heightData.Length == 0)
                {
                    DebugManager.Instance?.LogError(DEBUG_CLASS_NAME,
                        $"Failed to read height data for {regionCoord}");
                    return false;
                }

                heightImage = Image.CreateFromData(
                    _regionSize, _regionSize, false, Image.Format.Rf, heightData);

                if (regionData.ControlMap.IsValid)
                {
                    byte[] controlData = Gpu.Rd.TextureGetData(regionData.ControlMap, 0);
                    if (controlData != null && controlData.Length > 0)
                    {
                        controlImage = Image.CreateFromData(
                            _regionSize, _regionSize, false, Image.Format.Rf, controlData);
                    }
                }

                PushImagesToTerrain3DLayers(regionCoord, heightImage, controlImage);

                DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.TerrainPush,
                    $"Pushed region {regionCoord}");

                return true;
            }
            catch (System.Exception ex)
            {
                DebugManager.Instance?.LogError(DEBUG_CLASS_NAME,
                    $"Exception pushing region {regionCoord}: {ex.Message}");
                return false;
            }
            finally
            {
                if (scaledHeightRid.IsValid)
                {
                    Gpu.FreeRid(scaledHeightRid);
                }
            }
        }

        /// <summary>
        /// Dispatches the height scale compute shader.
        /// </summary>
        private void DispatchHeightScale(Rid sourceHeightMap, Rid destHeightMap)
        {
            var op = new AsyncComputeOperation("res://addons/terrain_3d_tools/Shaders/Utils/height_scale.glsl");

            if (!op.IsValid())
            {
                DebugManager.Instance?.LogError(DEBUG_CLASS_NAME,
                    "Failed to create height scale compute operation");
                return;
            }

            op.BindStorageImage(0, sourceHeightMap);
            op.BindStorageImage(1, destHeightMap);

            var pushConstants = GpuUtils.CreatePushConstants()
                .Add(_heightScale)
                .AddPadding(12)
                .Build();
            op.SetPushConstants(pushConstants);

            uint groupsX = (uint)((_regionSize + 7) / 8);
            uint groupsY = (uint)((_regionSize + 7) / 8);

            var commands = op.CreateDispatchCommands(groupsX, groupsY);

            long computeList = Gpu.ComputeListBegin();
            commands?.Invoke(computeList);
            Gpu.ComputeListEnd();
            Gpu.Submit();
            AsyncGpuTaskManager.Instance?.MarkPendingSubmission();

            foreach (var rid in op.GetTemporaryRids())
            {
                Gpu.FreeRid(rid);
            }

            DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.GpuDispatches,
                $"Dispatched height scale {_heightScale}");
        }

        /// <summary>
        /// Pushes height and control images directly to Terrain3D as persistent overrides.
        /// Uses the override mechanism to bypass the non-destructive layer stack and commit data to disk.
        /// </summary>
        private void PushImagesToTerrain3DLayers(Vector2I regionCoord, Image heightImage, Image controlImage)
        {
            var t3DData = _terrain3D.Data;

            if (!t3DData.HasRegion(regionCoord))
            {
                t3DData.AddRegionBlank(regionCoord, false);
            }

            try
            {
                // Use the new override API to commit data directly to disk
                // Pass null for color map as we don't generate color data
                t3DData.SetRegionOverride(regionCoord, heightImage, controlImage, null);

                DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.TerrainPush,
                    $"Set region override for {regionCoord} (height={heightImage != null}, control={controlImage != null})");
            }
            catch (Exception ex)
            {
                DebugManager.Instance?.LogError(DEBUG_CLASS_NAME,
                    $"Failed to set region override for {regionCoord}: {ex.Message}");
            }
        }

        /// <summary>
        /// Finalizes terrain update by adding/removing regions and clearing overrides.
        /// </summary>
        private void FinalizeTerrainUpdate(List<Vector2I> allActiveRegions)
        {
            var t3DData = _terrain3D.Data;

            int addedCount = 0;
            foreach (var regionCoord in allActiveRegions)
            {
                if (!t3DData.HasRegion(regionCoord))
                {
                    try
                    {
                        t3DData.AddRegionBlank(regionCoord, false);
                        addedCount++;
                    }
                    catch (System.Exception ex)
                    {
                        DebugManager.Instance?.LogError(DEBUG_CLASS_NAME,
                            $"Failed to add region {regionCoord}: {ex.Message}");
                    }
                }
            }

            List<Vector2I> regionLocations = GetRegionLocations();
            int clearedOverrideCount = 0;
            foreach (Vector2I regionLocation in regionLocations)
            {
                if (!allActiveRegions.Contains(regionLocation))
                {
                    // Clear overrides for regions that are no longer active
                    try
                    {
                        t3DData.SetRegionOverride(regionLocation, null, null, null);
                        clearedOverrideCount++;
                    }
                    catch (Exception ex)
                    {
                        DebugManager.Instance?.LogError(DEBUG_CLASS_NAME,
                            $"Failed to clear region override for {regionLocation}: {ex.Message}");
                    }
                }
            }

            DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.TerrainSync,
                $"Finalized: cleared {clearedOverrideCount} region override(s), added {addedCount} region(s), on frame {Engine.GetProcessFrames()}");
        }

        public bool ValidateTerrainSystem()
        {
            if (_terrain3D == null || _regionMapManager == null || _regionSize <= 0)
            {
                DebugManager.Instance?.LogError(DEBUG_CLASS_NAME,
                    "Terrain system validation failed");
                return false;
            }
            return true;
        }

        /// <summary>
        /// Checks if the integration is ready to push instances.
        /// </summary>
        public bool IsReadyForInstancePush()
        {
            try
            {
                if (_terrain3D == null)
                    return false;

                if (!GodotObject.IsInstanceValid(_terrain3D))
                    return false;

                var instancer = _terrain3D.Instancer;
                if (instancer == null || !GodotObject.IsInstanceValid(instancer))
                    return false;

                var data = _terrain3D.Data;
                if (data == null || !GodotObject.IsInstanceValid(data))
                    return false;

                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Pushes instance transforms to Terrain3D's instancer system.
        /// Clears specific mesh IDs per region, then adds aggregated transforms.
        /// </summary>
        public void PushInstancesToTerrain(
            List<(Vector2I regionCoords, int meshAssetId, Transform3D[] transforms)> instanceData,
            Dictionary<Vector2I, HashSet<int>> regionsToClear)
        {
            if (!IsReadyForInstancePush())
            {
                DebugManager.Instance?.LogWarning(DEBUG_CLASS_NAME,
                    "Cannot push instances - Terrain3D not ready");
                return;
            }

            Terrain3DInstancer instancer = null;

            try
            {
                instancer = _terrain3D.Instancer;
                if (instancer == null || !GodotObject.IsInstanceValid(instancer))
                {
                    DebugManager.Instance?.LogWarning(DEBUG_CLASS_NAME,
                        "Terrain3D Instancer is null or invalid");
                    return;
                }
            }
            catch (Exception ex)
            {
                DebugManager.Instance?.LogError(DEBUG_CLASS_NAME,
                    $"Failed to access Terrain3D Instancer: {ex.Message}");
                return;
            }

            DebugManager.Instance?.StartTimer(DEBUG_CLASS_NAME, DebugCategory.TerrainPush, "PushInstances");

            // Step 1: Clear all affected (region, meshId) pairs
            int clearedCount = 0;
            foreach (var (regionCoords, meshIdsToClear) in regionsToClear)
            {
                foreach (var meshId in meshIdsToClear)
                {
                    try
                    {
                        instancer.ClearByLocation(regionCoords, meshId);
                        clearedCount++;

                        DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.TerrainPush,
                            $"Cleared region {regionCoords} for mesh {meshId}");
                    }
                    catch (Exception ex)
                    {
                        DebugManager.Instance?.LogError(DEBUG_CLASS_NAME,
                            $"Failed to clear region {regionCoords} for mesh {meshId}: {ex.Message}");
                    }
                }
            }

            // Step 2: Aggregate all transforms by mesh ID for AddTransforms call
            var transformsByMesh = new Dictionary<int, List<Transform3D>>();

            foreach (var (regionCoords, meshId, transforms) in instanceData)
            {
                if (transforms == null || transforms.Length == 0) continue;

                if (!transformsByMesh.ContainsKey(meshId))
                {
                    transformsByMesh[meshId] = new List<Transform3D>();
                }

                transformsByMesh[meshId].AddRange(transforms);
            }

            if (transformsByMesh.Count == 0)
            {
                DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.TerrainPush,
                    $"No transforms to push (cleared {clearedCount} region/mesh pairs)");
                DebugManager.Instance?.EndTimer(DEBUG_CLASS_NAME, DebugCategory.TerrainPush, "PushInstances");
                return;
            }

            // Step 3: Add transforms for each mesh type
            int totalInstancesAdded = 0;
            var meshIdList = transformsByMesh.Keys.ToList();

            for (int i = 0; i < meshIdList.Count; i++)
            {
                int meshId = meshIdList[i];
                var transforms = transformsByMesh[meshId];

                // Convert to Godot.Collections.Array
                var transformArray = new Godot.Collections.Array();
                foreach (var t in transforms)
                {
                    transformArray.Add(t);
                }

                // Empty colors array
                var colors = new Color[0];

                // Update on the last mesh only
                bool shouldUpdate = true;

                try
                {
                    DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.TerrainPush,
                        $"Calling AddTransforms for mesh {meshId} with {transforms.Count} transforms (update={shouldUpdate})");

                    instancer.AddTransforms(meshId, transformArray, colors, shouldUpdate);
                    totalInstancesAdded += transforms.Count;

                    DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.TerrainPush,
                        $"Successfully added {transforms.Count} instances for mesh {meshId}");
                }
                catch (Exception ex)
                {
                    DebugManager.Instance?.LogError(DEBUG_CLASS_NAME,
                        $"Failed to add transforms for mesh {meshId}: {ex.Message}");
                }
            }

            DebugManager.Instance?.EndTimer(DEBUG_CLASS_NAME, DebugCategory.TerrainPush, "PushInstances");
            DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.PerformanceMetrics,
                $"Instance push complete: cleared {clearedCount} region/mesh pairs, " +
                $"added {totalInstancesAdded} instances across {meshIdList.Count} mesh type(s)");
        }

        #region Fast Path Push

        /// <summary>
        /// Fast path push for height data during brush strokes.
        /// </summary>
        public bool PushRegionHeightFast(Vector2I regionCoords, float heightScale)
        {
            var regionData = _regionMapManager.GetRegionData(regionCoords);
            if (regionData == null || !regionData.HeightMap.IsValid)
            {
                return false;
            }

            Rid scaledHeightRid = new();

            try
            {
                scaledHeightRid = Gpu.CreateTexture2D(
                    (uint)_regionSize, (uint)_regionSize,
                    RenderingDevice.DataFormat.R32Sfloat,
                    RenderingDevice.TextureUsageBits.StorageBit |
                    RenderingDevice.TextureUsageBits.CanCopyFromBit);

                if (!scaledHeightRid.IsValid)
                    return false;

                // Dispatch returns false if shader failed - don't sync if nothing was submitted
                if (!DispatchHeightScaleFast(regionData.HeightMap, scaledHeightRid, heightScale))
                {
                    return false;
                }

                // Use managed sync - only syncs if there's pending work
                AsyncGpuTaskManager.Instance?.SyncIfNeeded();

                byte[] heightData = Gpu.Rd.TextureGetData(scaledHeightRid, 0);
                if (heightData == null || heightData.Length == 0)
                    return false;

                var heightImage = Image.CreateFromData(
                    _regionSize, _regionSize, false, Image.Format.Rf, heightData);

                PushHeightImageToTerrain3D(regionCoords, heightImage);

                return true;
            }
            catch (System.Exception ex)
            {
                DebugManager.Instance?.LogError(DEBUG_CLASS_NAME,
                    $"Fast height push failed for {regionCoords}: {ex.Message}");
                return false;
            }
            finally
            {
                if (scaledHeightRid.IsValid)
                {
                    Gpu.FreeRid(scaledHeightRid);
                }
            }
        }

        /// <summary>
        /// Fast path push for control/texture data during brush strokes.
        /// Assumes GPU work is already synced.
        /// </summary>
        public bool PushRegionControlFast(Vector2I regionCoords)
        {
            var regionData = _regionMapManager.GetRegionData(regionCoords);
            if (regionData == null || !regionData.ControlMap.IsValid)
            {
                return false;
            }

            try
            {
                byte[] controlData = Gpu.Rd.TextureGetData(regionData.ControlMap, 0);
                if (controlData == null || controlData.Length == 0)
                    return false;

                var controlImage = Image.CreateFromData(
                    _regionSize, _regionSize, false, Image.Format.Rf, controlData);

                PushControlImageToTerrain3D(regionCoords, controlImage);

                return true;
            }
            catch (System.Exception ex)
            {
                DebugManager.Instance?.LogError(DEBUG_CLASS_NAME,
                    $"Fast control push failed for {regionCoords}: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Fast path push for height data during brush strokes.
        /// </summary>
        private bool DispatchHeightScaleFast(Rid sourceHeightMap, Rid destHeightMap, float heightScale)
        {
            var op = new AsyncComputeOperation("res://addons/terrain_3d_tools/Shaders/Utils/height_scale.glsl");

            if (!op.IsValid())
            {
                DebugManager.Instance?.LogError(DEBUG_CLASS_NAME,
                    "Failed to create height scale compute operation");
                return false;
            }

            op.BindStorageImage(0, sourceHeightMap);
            op.BindStorageImage(1, destHeightMap);

            var pushConstants = GpuUtils.CreatePushConstants()
                .Add(heightScale)
                .AddPadding(12)
                .Build();
            op.SetPushConstants(pushConstants);

            uint groups = (uint)((_regionSize + 7) / 8);

            long computeList = Gpu.ComputeListBegin();
            op.CreateDispatchCommands(groups, groups)?.Invoke(computeList);
            Gpu.ComputeListEnd();
            Gpu.Submit();
            AsyncGpuTaskManager.Instance?.MarkPendingSubmission();

            // Cleanup temp RIDs immediately
            foreach (var rid in op.GetTemporaryRids())
            {
                Gpu.FreeRid(rid);
            }

            return true;
        }

        /// <summary>
        /// Push just height image to Terrain3D (no control map).
        /// </summary>
        private void PushHeightImageToTerrain3D(Vector2I regionCoords, Image heightImage)
        {
            var t3DData = _terrain3D.Data;

            if (!t3DData.HasRegion(regionCoords))
            {
                t3DData.AddRegionBlank(regionCoords, false);
            }

            var t3DRegion = t3DData.GetRegion(regionCoords);
            if (t3DRegion == null)
                return;

            t3DRegion.SetMap(Terrain3DRegion.MapType.Height, heightImage);
            t3DRegion.Edited = true;

            // Immediate update for height only
            t3DData.UpdateMaps(Terrain3DRegion.MapType.Height, false);
        }

        /// <summary>
        /// Push just control image to Terrain3D (no height map).
        /// </summary>
        private void PushControlImageToTerrain3D(Vector2I regionCoords, Image controlImage)
        {
            var t3DData = _terrain3D.Data;

            if (!t3DData.HasRegion(regionCoords))
            {
                t3DData.AddRegionBlank(regionCoords, false);
            }

            var t3DRegion = t3DData.GetRegion(regionCoords);
            if (t3DRegion == null)
                return;

            t3DRegion.SetMap(Terrain3DRegion.MapType.Control, controlImage);
            t3DRegion.Edited = true;

            // Immediate update for control only
            t3DData.UpdateMaps(Terrain3DRegion.MapType.Control, false);
        }

        #endregion

        #region Helpers

        /// <summary>
        /// Clears instances for a specific mesh in a specific region.
        /// Used when cleaning up removed layers.
        /// </summary>
        public void ClearInstancesForMesh(Vector2I regionCoords, int meshId)
        {
            if (!IsReadyForInstancePush()) return;

            try
            {
                var instancer = _terrain3D.Instancer;
                if (instancer != null && GodotObject.IsInstanceValid(instancer))
                {
                    instancer.ClearByLocation(regionCoords, meshId);
                    GD.Print($"Cleared instances for mesh {meshId} in region {regionCoords}");
                }
            }
            catch (Exception ex)
            {
                DebugManager.Instance?.LogError(DEBUG_CLASS_NAME,
                    $"Failed to clear instances for mesh {meshId} in region {regionCoords}: {ex.Message}");
            }
        }
        public List<Vector2I> GetRegionLocations()
        {
            var result = new List<Vector2I>();

            var locations = _terrain3D.Data.RegionLocations;

            if (locations is Godot.Collections.Array array)
            {
                foreach (var item in array)
                {
                    if (item.SafeAsVector2I() is Vector2I location)
                    {
                        result.Add(location);
                    }
                }
            }

            return result;
        }

        #endregion
    }
}