// /Core/RegionDependencyManager.cs
using Godot;
using Godot.Collections;
using System.Collections.Generic;
using System.Linq;
using Terrain3DTools.Layers;
using Terrain3DTools.Utils;
using Terrain3DTools.Core.Debug;

namespace Terrain3DTools.Core
{
    public class TieredRegionLayers
    {
        public List<TerrainLayerBase> HeightLayers { get; } = new();
        public List<TerrainLayerBase> TextureLayers { get; } = new();
        public List<TerrainLayerBase> FeatureLayers { get; } = new();


        /// <summary>
        /// Determines if this region has any content that actually defines terrain geometry.
        /// A region needs height data (from height layers or height-modifying features) to be worth processing.
        /// </summary>
        public bool HasGeometryDefiningLayers()
        {
            // Has explicit height layers
            if (HeightLayers.Count > 0) return true;

            // Has feature layers that modify height
            if (FeatureLayers.Any(f => f is FeatureLayer featureLayer && featureLayer.ModifiesHeight))
                return true;

            return false;
        }

        /// <summary>
        /// Determines if this region should be processed at all.
        /// Regions with only texture layers (and no height data) should be skipped.
        /// </summary>
        public bool ShouldProcess()
        {
            // If there's any geometry-defining content, process the region
            return HasGeometryDefiningLayers();
        }

        /// <summary>
        /// Ensure that the active regions list doesn't grow beyond the
        /// limitations of the terrain system
        /// </summary>
        /// <param name="coords">The regions coordinates</param>
        /// <param name="minMaxRegionCoords">The min / max coordinates of the terrain system</param>
        /// <returns></returns>
        public bool IsInBounds(Vector2I coords, Vector2I minMaxRegionCoords)
        {
            if (coords.X >= minMaxRegionCoords.X && coords.X <= minMaxRegionCoords.Y &&
                coords.Y >= minMaxRegionCoords.X && coords.Y <= minMaxRegionCoords.Y)
                return true;
            return false;
        }
    }

    public class RegionDependencyManager
    {
        private const string DEBUG_CLASS_NAME = "RegionDependencyManager";

        private readonly int _regionSize;
        private readonly Vector2I _minMaxRegionCoords;
        private readonly System.Collections.Generic.Dictionary<string, Rect2I> _previousLayerBounds = new();
        private HashSet<string> _knownLayerNames = new();
        private readonly System.Collections.Generic.Dictionary<Vector2I, TieredRegionLayers> _regionToLayersMap = new();

        private readonly HashSet<Vector2I> _regionsUpdatedThisCycle = new();

        private readonly HashSet<Vector2I> _regionsNeedingRebuild = new();

        public RegionDependencyManager(int regionSize, Vector2I minMaxRegionCoords)
        {
            _regionSize = regionSize;
            _minMaxRegionCoords = minMaxRegionCoords;

            DebugManager.Instance?.RegisterClass(DEBUG_CLASS_NAME);
            DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.Initialization,
                $"RegionDependencyManager created with region size {regionSize}, bounds [{minMaxRegionCoords.X}, {minMaxRegionCoords.Y}]");
        }

        public void Update(Godot.Collections.Array<TerrainLayerBase> currentLayers, HashSet<Vector2I> regionsToUpdate)
        {
            if (_regionSize <= 0) return;

            DebugManager.Instance?.StartTimer(DEBUG_CLASS_NAME, DebugCategory.RegionDependencies, "Update");
            DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.RegionDependencies,
                $"Update called with {currentLayers.Count} layers");
            DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.RegionDependencies,
                $"Update START: regionsToUpdate has {regionsToUpdate.Count} regions");

            // 1. BEFORE clearing the map, capture all regions that currently have layers
            //    These are regions that might need rebuilding if layers move away
            var regionsThatHadLayers = new HashSet<Vector2I>(_regionToLayersMap.Keys);

            // 2. Detect which layers have changed (added, removed, or moved)
            DetectLayerChanges(currentLayers, regionsToUpdate);
            DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.RegionDependencies,
                $"After DetectLayerChanges: {regionsToUpdate.Count} boundary regions");

            // 3. Update the mapping of which layers affect which regions
            UpdateRegionToLayerMap(currentLayers);
            DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.RegionDependencies,
                $"Update END: regionsToUpdate has {regionsToUpdate.Count} boundary regions");
                
            // 4. Find regions that HAD layers but now don't - these need rebuilding
            var regionsThatLostAllLayers = regionsThatHadLayers.Except(_regionToLayersMap.Keys);
            foreach (var region in regionsThatLostAllLayers)
            {
                _regionsNeedingRebuild.Add(region);
                regionsToUpdate.Add(region);  // Also add to the boundary dirty set

                DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.RegionDetails,
                    $"Region {region} lost all layers - marked for rebuild");
            }

            DebugManager.Instance?.EndTimer(DEBUG_CLASS_NAME, DebugCategory.RegionDependencies, "Update");

            DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.PerformanceMetrics,
                $"Tracking {currentLayers.Count} layers across {_regionToLayersMap.Count} regions, {_regionsNeedingRebuild.Count} need rebuild");
        }

        public TieredRegionLayers GetTieredLayersForRegion(Vector2I regionCoords)
        {
            return _regionToLayersMap.GetValueOrDefault(regionCoords);
        }

        /// <summary>
        /// Returns only region coordinates that should actually be processed.
        /// Filters out regions that only have texture layers with no height data.
        /// Ensures that region coordinates are within the terrain systems bounds.
        /// </summary>
        public IReadOnlyCollection<Vector2I> GetActiveRegionCoords()
        {
            var activeRegions = _regionToLayersMap
                .Where(kvp => kvp.Value.IsInBounds(kvp.Key, _minMaxRegionCoords))
                .Where(kvp => kvp.Value.ShouldProcess())
                .Select(kvp => kvp.Key)
                .ToList();

            DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.RegionDependencies,
                $"Active regions: {activeRegions.Count}");

            return activeRegions;
        }

        /// <summary>
        /// Returns ALL region coordinates including those with only texture layers.
        /// Useful for understanding the full extent of layer coverage.
        /// </summary>
        public IReadOnlyCollection<Vector2I> GetAllRegionCoords()
        {
            return _regionToLayersMap.Keys;
        }

        public HashSet<Vector2I> DetermineDirtyRegions(
    HashSet<Vector2I> boundaryDirtyRegions,
    IEnumerable<TerrainLayerBase> layersWithChanges)
        {
            DebugManager.Instance?.StartTimer(DEBUG_CLASS_NAME, DebugCategory.RegionDependencies, "DetermineDirtyRegions");

            var allDirtyRegions = new HashSet<Vector2I>(boundaryDirtyRegions);

            // 1. For each changed layer, find all regions it overlaps
            foreach (var layer in layersWithChanges)
            {
                var bounds = TerrainCoordinateHelper.GetRegionBoundsForLayer(layer, _regionSize);
                foreach (var coord in bounds.GetRegionCoords())
                {
                    allDirtyRegions.Add(coord);
                }
            }

            // 2. Filter to processable regions, BUT keep boundary regions even if they have no layers
            // Boundary regions are from position/size changes and MUST be processed to clear old data
            var processableRegions = allDirtyRegions
                .Where(coord =>
                {
                    // CRITICAL: Always process boundary regions (from layer movement/resize)
                    // These need to be cleared even if they have no layers now
                    if (boundaryDirtyRegions.Contains(coord))
                        return true;

                    // For other dirty regions, only process if they have geometry-defining layers
                    var tieredLayers = GetTieredLayersForRegion(coord);
                    return tieredLayers?.ShouldProcess() ?? false;
                })
                .ToHashSet();

            DebugManager.Instance?.EndTimer(DEBUG_CLASS_NAME, DebugCategory.RegionDependencies, "DetermineDirtyRegions");

            DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.PerformanceMetrics,
                $"Dirty regions: {processableRegions.Count} (filtered from {allDirtyRegions.Count}, boundary: {boundaryDirtyRegions.Count})");

            return processableRegions;
        }


        public void MarkRegionUpdated(Vector2I regionCoords)
        {
            _regionsUpdatedThisCycle.Add(regionCoords);

            DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.RegionDetails,
                $"Marked region {regionCoords} as updated");
        }

        public HashSet<Vector2I> GetAndClearUpdatedRegions()
        {
            var updated = new HashSet<Vector2I>(_regionsUpdatedThisCycle);
            _regionsUpdatedThisCycle.Clear();

            if (updated.Count > 0)
            {
                DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.PerformanceMetrics,
                    $"Clearing {updated.Count} updated region(s)");
            }

            return updated;
        }

        public int GetUpdatedRegionCount()
        {
            return _regionsUpdatedThisCycle.Count;
        }

        public HashSet<Vector2I> PeekUpdatedRegions()
        {
            return new HashSet<Vector2I>(_regionsUpdatedThisCycle);
        }

        public List<Vector2I> GetRegionsToRemove(IReadOnlyCollection<Vector2I> currentlyManagedRegions)
        {
            var activeRegions = GetActiveRegionCoords();
            var regionsToRemove = currentlyManagedRegions.Except(activeRegions).ToList();

            if (regionsToRemove.Count > 0)
            {
                DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.RegionLifecycle,
                    $"Identified {regionsToRemove.Count} regions for removal");
            }

            return regionsToRemove;
        }

        private void UpdateRegionToLayerMap(Godot.Collections.Array<TerrainLayerBase> currentLayers)
        {
            DebugManager.Instance?.StartTimer(DEBUG_CLASS_NAME, DebugCategory.RegionDependencies, "UpdateRegionToLayerMap");

            _regionToLayersMap.Clear();

            // 1. For each layer, determine which regions it overlaps
            foreach (var layer in currentLayers)
            {
                if (layer == null) continue;

                var bounds = TerrainCoordinateHelper.GetRegionBoundsForLayer(layer, _regionSize);

                // 2. Add the layer to each region's tiered collection
                for (int x = bounds.Position.X; x < bounds.End.X; x++)
                {
                    for (int z = bounds.Position.Y; z < bounds.End.Y; z++)
                    {
                        var coord = new Vector2I(x, z);

                        if (!_regionToLayersMap.TryGetValue(coord, out var tieredLayers))
                        {
                            tieredLayers = new TieredRegionLayers();
                            _regionToLayersMap[coord] = tieredLayers;
                        }

                        // 3. Add layer to appropriate tier based on type
                        switch (layer.GetLayerType())
                        {
                            case LayerType.Height:
                                tieredLayers.HeightLayers.Add(layer);
                                break;
                            case LayerType.Texture:
                                tieredLayers.TextureLayers.Add(layer);
                                break;
                            case LayerType.Feature:
                                tieredLayers.FeatureLayers.Add(layer);
                                break;
                        }
                    }
                }
            }

            DebugManager.Instance?.EndTimer(DEBUG_CLASS_NAME, DebugCategory.RegionDependencies, "UpdateRegionToLayerMap");

            DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.PerformanceMetrics,
                $"Updated region map: {_regionToLayersMap.Count} regions affected by {currentLayers.Count} layers");
        }

        private void DetectLayerChanges(Array<TerrainLayerBase> currentLayers, HashSet<Vector2I> regionsToUpdate)
        {
            DebugManager.Instance?.StartTimer(DEBUG_CLASS_NAME, DebugCategory.LayerDirtying, "DetectLayerChanges");

            var currentLayerNames = new HashSet<string>();
            int newLayerCount = 0;
            int movedLayerCount = 0;

            foreach (var layer in currentLayers)
            {
                if (layer == null) continue;
                currentLayerNames.Add(layer.LayerName);

                var currentBounds = TerrainCoordinateHelper.GetRegionBoundsForLayer(layer, _regionSize);
                bool isNewLayer = !_knownLayerNames.Contains(layer.LayerName);

                DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.LayerDetails,
                    $"Checking layer '{layer.LayerName}': Pos={layer.GlobalPosition}, Bounds={currentBounds}");

                if (isNewLayer)
                {
                    newLayerCount++;
                    MarkRegionsInBoundsDirty(currentBounds, regionsToUpdate);

                    DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.LayerDetails,
                        $"NEW layer '{layer.LayerName}' - marked {currentBounds.GetRegionCoords().Count()} regions");
                }
                else if (_previousLayerBounds.TryGetValue(layer.LayerName, out var previousBounds))
                {
                    DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.LayerDetails,
                        $"Existing layer '{layer.LayerName}': PrevBounds={previousBounds}, CurBounds={currentBounds}");

                    if (previousBounds != currentBounds)
                    {
                        movedLayerCount++;
                        MarkRegionsInBoundsDirty(previousBounds, regionsToUpdate);
                        MarkRegionsInBoundsDirty(currentBounds, regionsToUpdate);
                        layer.ForcePositionDirty();

                        DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.LayerDetails,
                            $"MOVED layer '{layer.LayerName}' - old {previousBounds.GetRegionCoords().Count()} regions + new {currentBounds.GetRegionCoords().Count()} regions");
                    }
                    else if (layer.PositionDirty)
                    {
                        // Bounds didn't change but layer says it moved - still mark regions dirty
                        DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.LayerDetails,
                            $"Layer '{layer.LayerName}' is position dirty but bounds unchanged - marking regions anyway");

                        MarkRegionsInBoundsDirty(currentBounds, regionsToUpdate);
                    }
                }

                _previousLayerBounds[layer.LayerName] = currentBounds;
            }

            // Detect deleted layers
            var deletedLayerNames = _knownLayerNames.Except(currentLayerNames).ToList();
            if (deletedLayerNames.Count > 0)
            {
                DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.LayerLifecycle,
                    $"Deleted {deletedLayerNames.Count} layer(s)");

                foreach (var deletedName in deletedLayerNames)
                {
                    if (_previousLayerBounds.TryGetValue(deletedName, out var lastBounds))
                    {
                        MarkRegionsInBoundsDirty(lastBounds, regionsToUpdate);
                        _previousLayerBounds.Remove(deletedName);
                    }
                }
            }

            _knownLayerNames = currentLayerNames;

            DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.RegionDependencies,
                $"DetectLayerChanges complete: New={newLayerCount}, Moved={movedLayerCount}, Deleted={deletedLayerNames.Count}, BoundaryRegions={regionsToUpdate.Count}");

            DebugManager.Instance?.EndTimer(DEBUG_CLASS_NAME, DebugCategory.LayerDirtying, "DetectLayerChanges");
        }

        private void MarkRegionsInBoundsDirty(Rect2I bounds, HashSet<Vector2I> regionsToUpdate)
        {
            // Mark all regions within bounds as needing update
            for (int x = bounds.Position.X; x < bounds.Position.X + bounds.Size.X; x++)
            {
                for (int z = bounds.Position.Y; z < bounds.Position.Y + bounds.Size.Y; z++)
                {
                    regionsToUpdate.Add(new Vector2I(x, z));
                }
            }
        }
    }
}