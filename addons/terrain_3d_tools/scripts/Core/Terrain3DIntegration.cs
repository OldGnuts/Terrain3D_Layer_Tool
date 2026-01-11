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

                Gpu.Sync();

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

            foreach (var rid in op.GetTemporaryRids())
            {
                Gpu.FreeRid(rid);
            }

            DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.GpuDispatches,
                $"Dispatched height scale {_heightScale}");
        }

        /// <summary>
        /// Pushes height and control images into Terrain3D's non-destructive layer stack.
        /// </summary>
        private void PushImagesToTerrain3DLayers(Vector2I regionCoord, Image heightImage, Image controlImage)
        {
            var t3DData = _terrain3D.Data;

            if (!t3DData.HasRegion(regionCoord))
            {
                t3DData.AddRegionBlank(regionCoord, false);
            }

            if (heightImage == null && controlImage == null)
            {
                ReleaseRegionLayers(regionCoord);
                return;
            }

            long heightExternalId = GetExternalLayerId(regionCoord, Terrain3DRegion.MapType.Height);
            long controlExternalId = GetExternalLayerId(regionCoord, Terrain3DRegion.MapType.Control);

            int writesRemaining = 0;
            if (heightImage != null)
                writesRemaining++;
            if (controlImage != null)
                writesRemaining++;

            bool wroteAnyLayer = false;

            if (heightImage != null)
            {
                bool shouldUpdate = (--writesRemaining == 0);
                wroteAnyLayer |= TrySetMapLayer(regionCoord,
                    Terrain3DRegion.MapType.Height,
                    heightImage,
                    heightExternalId,
                    shouldUpdate);
            }
            else
            {
                bool shouldUpdate = controlImage == null;
                TryReleaseMapLayer(heightExternalId, true, shouldUpdate);
            }

            if (controlImage != null)
            {
                bool shouldUpdate = (--writesRemaining == 0);
                wroteAnyLayer |= TrySetMapLayer(regionCoord,
                    Terrain3DRegion.MapType.Control,
                    controlImage,
                    controlExternalId,
                    shouldUpdate);
            }
            else
            {
                bool shouldUpdate = heightImage == null;
                TryReleaseMapLayer(controlExternalId, true, shouldUpdate);
            }

            if (!wroteAnyLayer)
                return;

            var t3DRegion = t3DData.GetRegion(regionCoord);
            if (t3DRegion == null)
            {
                DebugManager.Instance?.LogError(DEBUG_CLASS_NAME,
                    $"Failed to get Terrain3D region {regionCoord}");
                return;
            }

            t3DRegion.Edited = true;
        }

        private bool TrySetMapLayer(
            Vector2I regionCoord,
            Terrain3DRegion.MapType mapType,
            Image image,
            long externalLayerId,
            bool shouldUpdate)
        {
            try
            {
                if (_terrain3D == null || !GodotObject.IsInstanceValid(_terrain3D))
                {
                    DebugManager.Instance?.LogError(DEBUG_CLASS_NAME,
                        "Terrain3D instance is null or invalid while attempting to set map layer");
                    return false;
                }

                var terrainData = _terrain3D.Data;
                if (terrainData == null || !GodotObject.IsInstanceValid(terrainData))
                {
                    DebugManager.Instance?.LogError(DEBUG_CLASS_NAME,
                        "Terrain3D data is null or invalid while attempting to set map layer");
                    return false;
                }

                var stampLayer = terrainData.SetMapLayer(
                    regionCoord,
                    (long)mapType,
                    image,
                    externalLayerId,
                    shouldUpdate);

                if (stampLayer == null)
                {
                    DebugManager.Instance?.LogWarning(DEBUG_CLASS_NAME,
                        $"Terrain3D returned null layer for {mapType} at {regionCoord} (layerId={externalLayerId})");
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                DebugManager.Instance?.LogError(DEBUG_CLASS_NAME,
                    $"Failed to set {mapType} layer for {regionCoord}: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Finalizes terrain update by adding/removing regions and updating maps.
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
            int removedLayerCount = 0;
            foreach (Vector2I regionLocation in regionLocations)
            {
                if (!allActiveRegions.Contains(regionLocation))
                {
                    removedLayerCount += ReleaseRegionLayers(regionLocation);
                }
            }

            try
            {
                t3DData.UpdateMaps((long)Terrain3DRegion.MapType.Max, true);

                DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.TerrainSync,
                    $"Finalized: removed {removedLayerCount} plugin layer(s), added {addedCount} region(s), on frame {Engine.GetProcessFrames()}");
            }
            catch (System.Exception ex)
            {
                DebugManager.Instance?.LogError(DEBUG_CLASS_NAME,
                    $"Failed to finalize: {ex.Message}");
            }
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
                bool shouldUpdate = (i == meshIdList.Count - 1);

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

        #region Helpers
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

        private static long GetExternalLayerId(Vector2I regionCoord, Terrain3DRegion.MapType mapType)
        {
            string token = $"{nameof(Terrain3DIntegration)}:{mapType}:{regionCoord.X}:{regionCoord.Y}";
            return unchecked((long)GD.Hash(token));
        }

        private bool TryReleaseMapLayer(long externalLayerId, bool removeLayer, bool shouldUpdate)
        {
            try
            {
                if (_terrain3D == null || !GodotObject.IsInstanceValid(_terrain3D))
                    return false;

                var terrainData = _terrain3D.Data;
                if (terrainData == null || !GodotObject.IsInstanceValid(terrainData))
                    return false;

                return terrainData.ReleaseMapLayer(externalLayerId, removeLayer, shouldUpdate);
            }
            catch (Exception ex)
            {
                DebugManager.Instance?.LogError(DEBUG_CLASS_NAME,
                    $"Failed to release layer {externalLayerId}: {ex.Message}");
                return false;
            }
        }

        private int ReleaseRegionLayers(Vector2I regionCoord)
        {
            int removedLayers = 0;

            var heightId = GetExternalLayerId(regionCoord, Terrain3DRegion.MapType.Height);
            if (TryReleaseMapLayer(heightId, true, false))
            {
                removedLayers++;
            }

            var controlId = GetExternalLayerId(regionCoord, Terrain3DRegion.MapType.Control);
            if (TryReleaseMapLayer(controlId, true, false))
            {
                removedLayers++;
            }

            if (removedLayers == 0)
            {
                DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.TerrainSync,
                    $"No plugin layers to release for region {regionCoord}");
            }

            return removedLayers;
        }
        #endregion
    }
}