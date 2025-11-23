// /Core/RegionMapManager.cs
using Godot;
using System.Collections.Generic;
using System.Linq;
using Terrain3DTools.Layers;
using Terrain3DTools.Core.Debug;

namespace Terrain3DTools.Core
{
    public class RegionMapManager
    {
        private const string DEBUG_CLASS_NAME = "RegionMapManager";
        private readonly Dictionary<Vector2I, RegionData> _regionDataMap = new();
        private readonly int _regionSize;
        private readonly Node3D _previewParent;
        private readonly Node _owner;
        private bool _previewsEnabled = false;
        public bool PreviewsEnabled => _previewsEnabled;

        public RegionMapManager(int regionSize, Node3D previewParent, Node owner)
        {
            _regionSize = regionSize;
            _previewParent = previewParent;
            _owner = owner;

            DebugManager.Instance?.RegisterClass(DEBUG_CLASS_NAME);
            DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.Initialization,
                $"RegionMapManager created with region size {regionSize}");
        }

        public IReadOnlyCollection<Vector2I> GetManagedRegionCoords() => _regionDataMap.Keys;

        /// <summary>
        /// Enable / Disable region previews
        /// </summary>
        /// <param name="enabled"></param>
        public void SetPreviewsEnabled(bool enabled)
        {
            if (_previewsEnabled == enabled) return;
            _previewsEnabled = enabled;

            DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.RegionLifecycle,
                $"Setting Region Previews Enabled: {enabled}");

            if (enabled)
            {
                // Recreate previews for all existing regions
                foreach (var kvp in _regionDataMap)
                {
                    CreatePreviewIfNeeded(kvp.Key, kvp.Value.HeightMap);
                    RefreshRegionPreview(kvp.Key);
                }
            }
            else
            {
                // Destroy all existing previews
                foreach (var regionCoords in _regionDataMap.Keys)
                {
                    var preview = GetRegionPreview(regionCoords);
                    if (preview != null)
                    {
                        preview.QueueFree();
                    }
                }
            }
        }

        /// <summary>
        /// Checks if a region is currently managed without creating it.
        /// </summary>
        public bool HasRegion(Vector2I regionCoords)
        {
            return _regionDataMap.ContainsKey(regionCoords);
        }

        /// <summary>
        /// Gets the RegionData for a given coordinate, returning null if it doesn't exist.
        /// </summary>
        public RegionData GetRegionData(Vector2I regionCoords)
        {
            _regionDataMap.TryGetValue(regionCoords, out var data);
            return data;
        }

        public RegionData GetOrCreateRegionData(Vector2I regionCoords)
        {
            if (_regionDataMap.TryGetValue(regionCoords, out var data))
            {
                return data;
            }

            DebugManager.Instance?.StartTimer(DEBUG_CLASS_NAME, DebugCategory.RegionLifecycle,
                $"CreateRegion:{regionCoords}");

            var newRegionData = new RegionData
            {
                HeightMap = Gpu.CreateTexture2D(
                    (uint)_regionSize, (uint)_regionSize,
                    RenderingDevice.DataFormat.R32Sfloat,
                    RenderingDevice.TextureUsageBits.StorageBit |
                    RenderingDevice.TextureUsageBits.CanUpdateBit |
                    RenderingDevice.TextureUsageBits.CanCopyFromBit |
                    RenderingDevice.TextureUsageBits.SamplingBit),

                ControlMap = Gpu.CreateTexture2D(
                    (uint)_regionSize, (uint)_regionSize,
                    RenderingDevice.DataFormat.R32Sfloat,
                    RenderingDevice.TextureUsageBits.StorageBit |
                    RenderingDevice.TextureUsageBits.CanUpdateBit |
                    RenderingDevice.TextureUsageBits.CanCopyFromBit |
                    RenderingDevice.TextureUsageBits.SamplingBit),
            };

            _regionDataMap[regionCoords] = newRegionData;

            DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.GpuResources,
                $"Created GPU textures for region {regionCoords}");

            if (_previewsEnabled)
            {
                CreatePreviewIfNeeded(regionCoords, newRegionData.HeightMap);
            }

            DebugManager.Instance?.EndTimer(DEBUG_CLASS_NAME, DebugCategory.RegionLifecycle,
                $"CreateRegion:{regionCoords}");

            DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.RegionDetails,
                $"Created region {regionCoords}");

            return newRegionData;
        }

        /// <summary>
        /// Manages region lifecycle by removing regions that are no longer needed.
        /// Returns the list of regions that were actually removed.
        /// NOTE: This should be called AFTER regions have been processed/cleared.
        /// </summary>
        public List<Vector2I> RemoveInactiveRegions(List<Vector2I> regionsToRemove, HashSet<Vector2I> boundaryDirtyRegions)
        {
            if (regionsToRemove.Count == 0)
            {
                return new List<Vector2I>();
            }

            DebugManager.Instance?.StartTimer(DEBUG_CLASS_NAME, DebugCategory.RegionLifecycle,
                "RemoveInactiveRegions");

            var removedRegions = new List<Vector2I>();

            foreach (var regionCoord in regionsToRemove)
            {
                // Don't remove regions that are still being processed
                // They'll be removed in the next update after processing completes
                if (boundaryDirtyRegions.Contains(regionCoord))
                {
                    DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.RegionDetails,
                        $"Deferring removal of region {regionCoord} (still being processed)");
                    continue;
                }

                if (RemoveRegion(regionCoord))
                {
                    removedRegions.Add(regionCoord);
                }
            }

            DebugManager.Instance?.EndTimer(DEBUG_CLASS_NAME, DebugCategory.RegionLifecycle,
                "RemoveInactiveRegions");

            if (removedRegions.Count > 0)
            {
                DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.RegionLifecycle,
                    $"Removed {removedRegions.Count} inactive regions");
            }

            return removedRegions;
        }

        /// <summary>
        /// Removes a region and its associated resources.
        /// Returns true if the region was removed, false if it didn't exist.
        /// Should only be called when region has no pending async operations.
        /// </summary>
        public bool RemoveRegion(Vector2I regionCoords)
        {
            // Remove preview
            GetRegionPreview(regionCoords)?.QueueFree();

            if (!_regionDataMap.TryGetValue(regionCoords, out var data))
                return false;

            DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.RegionLifecycle,
                $"Removing region {regionCoords}");

            // Free GPU resources immediately - they're not in use anymore
            // (All GPU tasks completed before we got here)
            data.FreeAll();

            _regionDataMap.Remove(regionCoords);
            return true;
        }

        /// <summary>
        /// Finds the preview node and calls its Refresh method directly.
        /// </summary>
        public void RefreshRegionPreview(Vector2I regionCoords)
        {
            if (!_previewsEnabled) return; // Early exit

            var preview = GetRegionPreview(regionCoords);
            if (preview != null)
            {
                preview.Refresh();

                DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.RegionDetails,
                    $"Refreshed preview for region {regionCoords}");
            }
        }

        public void FreeAll()
        {
            DebugManager.Instance?.StartTimer(DEBUG_CLASS_NAME, DebugCategory.Cleanup, "FreeAll");

            int regionCount = _regionDataMap.Count;

            DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.Cleanup,
                $"Freeing all {regionCount} regions");

            foreach (var kvp in _regionDataMap)
            {
                GetRegionPreview(kvp.Key)?.QueueFree();
                kvp.Value.FreeAll();
            }

            _regionDataMap.Clear();

            DebugManager.Instance?.EndTimer(DEBUG_CLASS_NAME, DebugCategory.Cleanup, "FreeAll");

            DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.PerformanceMetrics,
                $"Freed {regionCount} regions, map now empty");
        }

        private void CreatePreviewIfNeeded(Vector2I regionCoords, Rid heightMapRid)
        {
            if (!Engine.IsEditorHint() || _previewParent == null)
            {
                return;
            }

            var preview = new RegionPreview
            {
                Name = $"RegionPreview_{regionCoords.X}_{regionCoords.Y}",
                RegionCoords = regionCoords,
                LocalRegionRid = heightMapRid,
                RegionSize = _regionSize
            };

            _previewParent.AddChild(preview);
            if (_owner != null) preview.Owner = _owner;

            DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.RegionDetails,
                $"Created preview for region {regionCoords}");
        }

        private RegionPreview GetRegionPreview(Vector2I regionCoords)
        {
            return _previewParent?.GetNodeOrNull<RegionPreview>($"RegionPreview_{regionCoords.X}_{regionCoords.Y}");
        }
    }
}
