// /Core/Pipeline/HeightLayerMaskPhase.cs
using Godot;
using System.Collections.Generic;
using System.Linq;
using Terrain3DTools.Core;
using Terrain3DTools.Utils;

namespace Terrain3DTools.Pipeline
{
    /// <summary>
    /// Phase 1: Generates mask textures for all dirty height layers.
    /// Height layer masks don't depend on any other data however
    /// visualization do depend on final region composites.
    /// </summary>
    public class HeightLayerMaskPhase : IProcessingPhase
    {
        public Dictionary<object, AsyncGpuTask> Execute(TerrainProcessingContext context)
        {
            var tasks = new Dictionary<object, AsyncGpuTask>();

            foreach (var layer in context.DirtyHeightLayers)
            {
                if (!GodotObject.IsInstanceValid(layer)) continue;

                var dependencies = new List<AsyncGpuTask>();
                Rid heightmapArrayRid = new Rid();
                Rid metadataBufferRid = new Rid();
                int activeRegionCount = 0;

                // Always stage height data for visualization purposes
                // We look for overlapping active regions to get the terrain shape
                var overlappingRegionCoords = TerrainCoordinateHelper
                    .GetRegionBoundsForLayer(layer, context.RegionSize)
                    .GetRegionCoords()
                    .ToList();

                var activeOverlappingRegions = overlappingRegionCoords
                    .Where(coord => context.CurrentlyActiveRegions.Contains(coord))
                    .ToList();

                if (activeOverlappingRegions.Count > 0)
                {
                    // We depend on the composite results from the previous frame (or current if we re-ordered)
                    // Note: Height layers generate masks *before* composite, so strictly speaking
                    // they visualize the "Old" terrain height + "New" mask. This is usually acceptable.
                    // If we need perfect sync, we'd need to re-architect to composite -> mask -> composite, which creates a cycle.
                    // For visualization, using the current region state is sufficient.

                    var compositeDependencies = activeOverlappingRegions
                        .Where(rc => context.RegionHeightCompositeTasks.ContainsKey(rc))
                        .Select(rc => context.RegionHeightCompositeTasks[rc])
                        .ToList();

                    var (stagingTask, stagingResult) = HeightDataStager.StageHeightDataForLayerAsync(
                        layer,
                        context.RegionMapManager,
                        context.RegionSize,
                        compositeDependencies);

                    if (stagingTask != null && stagingResult.IsValid)
                    {
                        dependencies.Add(stagingTask);
                        AsyncGpuTaskManager.Instance.AddTask(stagingTask);
                        
                        // Pass these RIDs to CreateUpdateLayerTextureTask
                        heightmapArrayRid = stagingResult.HeightmapArrayRid;
                        metadataBufferRid = stagingResult.MetadataBufferRid;
                        activeRegionCount = stagingResult.ActiveRegionCount;
                    }
                }

                System.Action onComplete = () =>
                {
                    if (GodotObject.IsInstanceValid(layer))
                        layer.Visualizer.Update();
                };

                // Pass the staged data to the pipeline
                var task = LayerMaskPipeline.CreateUpdateLayerTextureTask(
                    layer.layerTextureRID,
                    layer,
                    layer.Size.X,
                    layer.Size.Y,
                    heightmapArrayRid, // Sliced height map of regions
                    metadataBufferRid, // Data needed to index and offset into the heightMapArray
                    activeRegionCount,
                    dependencies,
                    onComplete);

                if (task != null)
                {
                    context.HeightLayerMaskTasks[layer] = task;
                    tasks[layer] = task;
                    AsyncGpuTaskManager.Instance.AddTask(task);
                }
            }

            return tasks;
        }
    }
}