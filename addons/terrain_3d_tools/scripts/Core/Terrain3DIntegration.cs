// /Core/Terrain3DIntegration.cs 

using Godot;
using System.Collections.Generic;
using System.Linq;
//using Terrain3DWrapper;
using TokisanGames;
using Terrain3DTools.Core.Debug;

namespace Terrain3DTools.Core
{
    /// <summary>
    /// Synchronizes our internal region data with Terrain3D.
    /// Reads texture data from GPU and pushes to Terrain3D's data structures.
    /// </summary>
    public class Terrain3DIntegration
    {
        private const string DEBUG_CLASS_NAME = "Terrain3DIntegration";

        private readonly Terrain3D _terrain3D;
        private readonly RegionMapManager _regionMapManager;
        private readonly int _regionSize;
        private readonly float _heightScale;

        // Track async operations
        private readonly Dictionary<Vector2I, PendingRegionPush> _pendingPushes = new();
        private PushBatch _currentBatch = null;

        private class PendingRegionPush
        {
            public Vector2I RegionCoord;
            public Image HeightImage;
            public Image ControlImage;
            public int PendingCallbacks;
            public Rid TemporaryHeightRid; // Store temporary RID for cleanup
        }

        private class PushBatch
        {
            public HashSet<Vector2I> ExpectedRegions = new();
            public HashSet<Vector2I> CompletedRegions = new();
            public List<Vector2I> AllActiveRegions = new();
        }

        public bool HasPendingPushes => _pendingPushes.Count > 0;
        public int PendingPushCount => _pendingPushes.Count;

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
        /// Initiates async push of updated regions to Terrain3D.
        /// This starts async texture reads from GPU.
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

            // Cancel previous batch if still pending
            if (_pendingPushes.Count > 0)
            {
                DebugManager.Instance?.LogWarning(DEBUG_CLASS_NAME,
                    $"Cancelling {_pendingPushes.Count} pending push(es)");

                // Clean up temporary RIDs
                foreach (var pending in _pendingPushes.Values)
                {
                    if (pending.TemporaryHeightRid.IsValid)
                    {
                        Gpu.FreeRid(pending.TemporaryHeightRid);
                    }
                }
                _pendingPushes.Clear();
            }

            // Start new batch
            _currentBatch = new PushBatch
            {
                ExpectedRegions = new HashSet<Vector2I>(updatedRegions),
                AllActiveRegions = allActiveRegions
            };
            DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.TerrainPush,
                $"Starting push batch: {updatedRegions.Count} regions");

            // Request async texture data for each region
            foreach (var regionCoord in updatedRegions)
            {
                RequestRegionData(regionCoord);
            }
        }

        private void RequestRegionData(Vector2I regionCoord)
        {
            var regionData = _regionMapManager.GetRegionData(regionCoord);
            if (regionData == null || !regionData.HeightMap.IsValid)
            {
                DebugManager.Instance?.LogError(DEBUG_CLASS_NAME,
                    $"Invalid region data for {regionCoord}");

                _currentBatch?.CompletedRegions.Add(regionCoord);
                CheckBatchCompletion();
                return;
            }

            // Track this region
            var pending = new PendingRegionPush
            {
                RegionCoord = regionCoord,
                PendingCallbacks = regionData.ControlMap.IsValid ? 2 : 1
            };
            _pendingPushes[regionCoord] = pending;

            // Apply height scale using compute shader
            Rid scaledHeightRid = ApplyHeightScale(regionData.HeightMap);

            if (!scaledHeightRid.IsValid)
            {
                DebugManager.Instance?.LogError(DEBUG_CLASS_NAME,
                    $"Failed to create scaled height texture for {regionCoord}");
                _pendingPushes.Remove(regionCoord);
                _currentBatch?.CompletedRegions.Add(regionCoord);
                CheckBatchCompletion();
                return;
            }

            // Store the temporary RID for cleanup
            pending.TemporaryHeightRid = scaledHeightRid;

            // Request height data from the scaled texture
            Callable heightCallback = Callable.From<byte[]>(data =>
                OnHeightDataReceived(regionCoord, data, scaledHeightRid));

            var error = Gpu.Rd.TextureGetDataAsync(scaledHeightRid, 0, heightCallback);
            if (error != Error.Ok)
            {
                DebugManager.Instance?.LogError(DEBUG_CLASS_NAME,
                    $"Failed to request height data for {regionCoord}: {error}");

                // Clean up temporary RID
                Gpu.FreeRid(scaledHeightRid);
                _pendingPushes.Remove(regionCoord);
                _currentBatch?.CompletedRegions.Add(regionCoord);
                CheckBatchCompletion();
                return;
            }

            // Request control data if available
            if (regionData.ControlMap.IsValid)
            {
                Callable controlCallback = Callable.From<byte[]>(data =>
                    OnControlDataReceived(regionCoord, data));

                error = Gpu.Rd.TextureGetDataAsync(regionData.ControlMap, 0, controlCallback);
                if (error != Error.Ok)
                {
                    pending.PendingCallbacks = 1; // Only waiting for height now
                }
            }
        }

        private Rid ApplyHeightScale(Rid sourceHeightMap)
        {
            // Create temporary texture for scaled height
            Rid scaledHeightMap = Gpu.CreateTexture2D(
                (uint)_regionSize,
                (uint)_regionSize,
                RenderingDevice.DataFormat.R32Sfloat,
                RenderingDevice.TextureUsageBits.StorageBit |
                RenderingDevice.TextureUsageBits.CanCopyFromBit);

            if (!scaledHeightMap.IsValid)
            {
                DebugManager.Instance?.LogError(DEBUG_CLASS_NAME,
                    "Failed to create temporary height texture");
                return new Rid();
            }

            // Create compute operation for height scaling
            var op = new AsyncComputeOperation("res://addons/terrain_3d_tools/Shaders/Utils/height_scale.glsl");

            if (!op.IsValid())
            {
                DebugManager.Instance?.LogError(DEBUG_CLASS_NAME,
                    "Failed to create height scale compute operation");
                Gpu.FreeRid(scaledHeightMap);
                return new Rid();
            }

            // Bind resources
            op.BindStorageImage(0, sourceHeightMap);  // Source
            op.BindStorageImage(1, scaledHeightMap);  // Target

            // Set height scale as push constant
            var pushConstants = GpuUtils.CreatePushConstants()
                .Add(_heightScale)
                .AddPadding(12)
                .Build();
            op.SetPushConstants(pushConstants);

            // Calculate dispatch groups (8x8 threads per group)
            uint groupsX = (uint)((_regionSize + 7) / 8);
            uint groupsY = (uint)((_regionSize + 7) / 8);

            // Create dispatch commands
            var commands = op.CreateDispatchCommands(groupsX, groupsY);

            // Execute synchronously
            long computeList = Gpu.ComputeListBegin();
            commands?.Invoke(computeList);
            Gpu.ComputeListEnd();
            Gpu.Submit();
            Gpu.Sync(); // Wait for completion

            // Clean up temporary resources from the operation
            foreach (var rid in op.GetTemporaryRids())
            {
                Gpu.FreeRid(rid);
            }

            DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.GpuDispatches,
                $"Applied height scale {_heightScale} to region texture");

            return scaledHeightMap;
        }

        private void OnHeightDataReceived(Vector2I regionCoord, byte[] data, Rid temporaryHeightRid)
        {
            // Free the temporary scaled height texture
            if (temporaryHeightRid.IsValid)
            {
                Gpu.FreeRid(temporaryHeightRid);
                DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.GpuResources,
                    $"Freed temporary height RID for region {regionCoord}");
            }

            if (!_pendingPushes.TryGetValue(regionCoord, out var pending))
                return;

            // Clear the stored temporary RID reference
            pending.TemporaryHeightRid = new Rid();

            pending.HeightImage = Image.CreateFromData(
                _regionSize, _regionSize, false, Image.Format.Rf, data);

            pending.PendingCallbacks--;
            TryCompletePush(regionCoord);
        }

        private void OnControlDataReceived(Vector2I regionCoord, byte[] data)
        {
            if (!_pendingPushes.TryGetValue(regionCoord, out var pending))
                return;

            pending.ControlImage = Image.CreateFromData(
                _regionSize, _regionSize, false, Image.Format.Rf, data);

            pending.PendingCallbacks--;
            TryCompletePush(regionCoord);
        }

        private void TryCompletePush(Vector2I regionCoord)
        {
            if (!_pendingPushes.TryGetValue(regionCoord, out var pending))
                return;

            // Wait for all callbacks
            if (pending.PendingCallbacks > 0)
                return;

            // Push to Terrain3D
            PushRegionToTerrain3D(pending);

            // Mark as complete
            _pendingPushes.Remove(regionCoord);
            _currentBatch?.CompletedRegions.Add(regionCoord);

            CheckBatchCompletion();
        }

        private void PushRegionToTerrain3D(PendingRegionPush pending)
        {
            var t3DData = _terrain3D.Data;

            // Ensure region exists
            if (!t3DData.HasRegion(pending.RegionCoord))
            {
                t3DData.AddRegionBlank(pending.RegionCoord, false);
            }

            var t3DRegion = t3DData.GetRegion(pending.RegionCoord);
            if (t3DRegion == null)
            {
                DebugManager.Instance?.LogError(DEBUG_CLASS_NAME,
                    $"Failed to get Terrain3D region {pending.RegionCoord}");
                return;
            }

            // Convert to terrain3d bindings
            if (pending.HeightImage != null)
                t3DRegion.SetMap(Terrain3DRegion.MapType.Height, pending.HeightImage);

            if (pending.ControlImage != null)
                t3DRegion.SetMap(Terrain3DRegion.MapType.Control, pending.ControlImage);

            t3DRegion.Edited = true;

            DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.TerrainPush,
                $"Pushed region {pending.RegionCoord}");
        }

        private void CheckBatchCompletion()
        {
            if (_currentBatch == null)
                return;

            // Check if all expected regions completed
            bool allComplete = _currentBatch.ExpectedRegions
                .All(r => _currentBatch.CompletedRegions.Contains(r));

            if (!allComplete)
                return;

            DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.TerrainSync,
                $"Batch complete: {_currentBatch.CompletedRegions.Count}/{_currentBatch.ExpectedRegions.Count}");

            // Finalize: remove regions and update terrain
            FinalizeTerrainUpdate(_currentBatch);
            _currentBatch = null;
        }

        private void FinalizeTerrainUpdate(PushBatch batch)
        {
            var t3DData = _terrain3D.Data;

            // Ensure all active regions exist
            int addedCount = 0;
            foreach (var regionCoord in batch.AllActiveRegions)
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
            // Remove unused regions
            List<Vector2I> regionLocations = GetRegionLocations();
            int removedCount = 0;
            foreach (Vector2I regionLocation in regionLocations)
            {
                if (!batch.AllActiveRegions.Contains(regionLocation))
                {
                    t3DData.RemoveRegionl(regionLocation, false);
                    removedCount++;
                }
            }

            // Final update
            try
            {
                t3DData.UpdateMaps(Terrain3DRegion.MapType.Max, true);

                DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.TerrainSync,
                    $"Finalized: removed {removedCount}, added {addedCount}");
            }
            catch (System.Exception ex)
            {
                DebugManager.Instance?.LogError(DEBUG_CLASS_NAME,
                    $"Failed to finalize: {ex.Message}");
            }
        }

        public void CancelPendingPushes()
        {
            if (_pendingPushes.Count > 0)
            {
                DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.TerrainPush,
                    $"Cancelling {_pendingPushes.Count} pending push(es)");

                // Clean up any temporary RIDs before clearing
                foreach (var pending in _pendingPushes.Values)
                {
                    if (pending.TemporaryHeightRid.IsValid)
                    {
                        Gpu.FreeRid(pending.TemporaryHeightRid);
                    }
                }

                _pendingPushes.Clear();
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

        public string GetStatusInfo()
        {
            return $"Terrain3DIntegration:\n" +
                   $"  Pending pushes: {_pendingPushes.Count}\n" +
                   $"  Height scale: {_heightScale}\n" +
                   $"  Current batch: {(_currentBatch != null ? $"{_currentBatch.CompletedRegions.Count}/{_currentBatch.ExpectedRegions.Count}" : "None")}";
        }
        #region Helpers
        /// <summary>
        /// Gets all region locations that currently exist in Terrain3D data.
        /// </summary>
        public System.Collections.Generic.List<Vector2I> GetRegionLocations()
        {
            var result = new System.Collections.Generic.List<Vector2I>();

            // Terrain3D API: get_region_locations() returns Array of Vector2i
            // Convert variant safe as list of V2I
            var locations =  _terrain3D.Data.RegionLocations;

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

    /// <summary>
        /// Helper extension methods for safe Variant handling
        /// </summary>
        public static class VariantExtensions
        {
            public static string SafeAsString(this Variant variant, string fallback = "")
            {
                return variant.VariantType != Variant.Type.Nil ? variant.AsString() : fallback;
            }

            public static bool SafeAsBool(this Variant variant, bool fallback = false)
            {
                return variant.VariantType != Variant.Type.Nil ? variant.AsBool() : fallback;
            }

            public static float SafeAsSingle(this Variant variant, float fallback = 0f)
            {
                return variant.VariantType != Variant.Type.Nil ? variant.AsSingle() : fallback;
            }

            public static int SafeAsInt32(this Variant variant, int fallback = 0)
            {
                return variant.VariantType != Variant.Type.Nil ? variant.AsInt32() : fallback;
            }

            public static uint SafeAsUInt32(this Variant variant, uint fallback = 0)
            {
                return variant.VariantType != Variant.Type.Nil ? variant.AsUInt32() : fallback;
            }

            public static Vector3 SafeAsVector3(this Variant variant, Vector3 fallback = default)
            {
                return variant.VariantType != Variant.Type.Nil ? variant.AsVector3() : fallback;
            }

            public static Vector2 SafeAsVector2(this Variant variant, Vector2 fallback = default)
            {
                return variant.VariantType != Variant.Type.Nil ? variant.AsVector2() : fallback;
            }

            public static Vector2I SafeAsVector2I(this Variant variant, Vector2I fallback = default)
            {
                return variant.VariantType != Variant.Type.Nil ? variant.AsVector2I() : fallback;
            }

            public static Color SafeAsColor(this Variant variant, Color fallback = default)
            {
                return variant.VariantType != Variant.Type.Nil ? variant.AsColor() : fallback;
            }

            public static Aabb SafeAsAabb(this Variant variant, Aabb fallback = default)
            {
                return variant.VariantType != Variant.Type.Nil ? variant.As<Aabb>() : fallback;
            }

            public static GodotObject SafeAsGodotObject(this Variant variant)
            {
                return variant.VariantType != Variant.Type.Nil ? variant.AsGodotObject() : null;
            }

            public static T SafeAs<[MustBeVariant] T>(this Variant variant, T fallback = default) where T : class
            {
                return variant.VariantType != Variant.Type.Nil ? variant.As<T>() : fallback;
            }

            public static T SafeAsGodotObject<T>(this Variant variant, T fallback = null) where T : GodotObject
            {
                if (variant.VariantType == Variant.Type.Nil)
                    return fallback;

                var obj = variant.AsGodotObject();
                return obj is T result ? result : fallback;
            }
        }
}