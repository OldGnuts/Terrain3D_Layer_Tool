// /Pipeline/HeightLayerMaskPhase.cs
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using Terrain3DTools.Core;
using Terrain3DTools.Core.Debug;
using Terrain3DTools.Layers;
using Terrain3DTools.Utils;

namespace Terrain3DTools.Pipeline
{
    /// <summary>
    /// Phase 1: Generates mask textures for all dirty height layers.
    /// Uses Lazy (JIT) initialization via LayerMaskPipeline.
    /// Now supports height-requiring masks by staging data from previous cycle.
    /// </summary>
    public class HeightLayerMaskPhase : IProcessingPhase
    {
        private const string DEBUG_CLASS_NAME = "HeightLayerMaskPhase";

        public HeightLayerMaskPhase()
        {
            DebugManager.Instance?.RegisterClass(DEBUG_CLASS_NAME);
        }

        public Dictionary<object, AsyncGpuTask> Execute(TerrainProcessingContext context)
        {
            var tasks = new Dictionary<object, AsyncGpuTask>();

            DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.PhaseExecution,
                $"Processing {context.DirtyHeightLayers.Count} height layer(s)");

            foreach (var layer in context.DirtyHeightLayers)
            {
                if (!GodotObject.IsInstanceValid(layer)) continue;

                AsyncGpuTask task;

                // Check if any masks require height data
                if (layer.DoesAnyMaskRequireHeightData())
                {
                    // Use the staging pattern like TextureLayerMaskPhase
                    task = CreateHeightMaskTaskWithStaging(layer, context);
                }
                else
                {
                    // Simple case - no height data needed
                    task = LayerMaskPipeline.CreateUpdateLayerTextureTask(
                        layer.layerTextureRID,
                        layer,
                        layer.Size.X,
                        layer.Size.Y,
                        new Rid(),
                        new Rid(),
                        0,
                        new List<AsyncGpuTask>(),
                        null);
                }

                if (task != null)
                {
                    context.HeightLayerMaskTasks[layer] = task;
                    tasks[layer] = task;
                    AsyncGpuTaskManager.Instance.AddTask(task);
                }
            }

            return tasks;
        }

        /// <summary>
        /// Creates a height mask task that stages height data from existing regions.
        /// Uses previous cycle's composited heightmaps as the data source.
        /// </summary>
        private AsyncGpuTask CreateHeightMaskTaskWithStaging(
            TerrainLayerBase layer,
            TerrainProcessingContext context)
        {
            string layerName = layer.LayerName;
            int width = layer.Size.X;
            int height = layer.Size.Y;
            Rid targetTexture = layer.layerTextureRID;

            // Determine overlapping regions (may have data from previous cycle)
            var overlappingRegionCoords = TerrainCoordinateHelper
                .GetRegionBoundsForLayer(layer, context.RegionSize)
                .GetRegionCoords()
                .Where(coord => context.CurrentlyActiveRegions.Contains(coord))
                .ToList();

            DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.MaskGeneration,
                $"Layer '{layerName}' requires height data, overlaps {overlappingRegionCoords.Count} active region(s)");

            // Capture references for the closure
            var regionMapManager = context.RegionMapManager;
            int regionSize = context.RegionSize;

            Func<(Action<long>, List<Rid>, List<string>)> generator = () =>
            {
                if (!GodotObject.IsInstanceValid(layer))
                    return ((l) => { }, new List<Rid>(), new List<string>());

                var allCommands = new List<Action<long>>();
                var operationRids = new HashSet<Rid>();
                var allShaderPaths = new List<string>();

                // Owner RIDs (freed last)
                Rid heightmapArrayRid = new Rid();
                Rid metadataBufferRid = new Rid();
                int stagedRegionCount = 0;

                // Stage height data from existing regions (previous cycle's composites)
                if (layer.DoesAnyMaskRequireHeightData())
                {
                    var (stagingTask, stagingResult) = HeightDataStager.StageHeightDataForLayerAsync(
                        layer, regionMapManager, regionSize, null);

                    if (stagingTask != null && stagingResult.IsValid)
                    {
                        allCommands.Add(stagingTask.GpuCommands);

                        var (stagedRids, stagedPaths) = stagingTask.ExtractResourcesForCleanup();
                        foreach (var rid in stagedRids) operationRids.Add(rid);
                        allShaderPaths.AddRange(stagedPaths);

                        heightmapArrayRid = stagingResult.HeightmapArrayRid;
                        metadataBufferRid = stagingResult.MetadataBufferRid;
                        stagedRegionCount = stagingResult.ActiveRegionCount;

                        DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.MaskGeneration,
                            $"Staged {stagedRegionCount} region(s) for '{layerName}'");
                    }
                    else
                    {
                        DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.MaskGeneration,
                            $"No height data available to stage for '{layerName}' (first cycle or no overlapping regions)");
                    }
                }

                // Create the mask pipeline task
                var pipelineTask = LayerMaskPipeline.CreateUpdateLayerTextureTask(
                    targetTexture,
                    layer,
                    width,
                    height,
                    heightmapArrayRid,
                    metadataBufferRid,
                    stagedRegionCount,
                    null,
                    null);

                if (pipelineTask != null)
                {
                    pipelineTask.Prepare();
                    if (pipelineTask.GpuCommands != null)
                    {
                        allCommands.Add(pipelineTask.GpuCommands);

                        var (pipeRids, pipePaths) = pipelineTask.ExtractResourcesForCleanup();
                        foreach (var rid in pipeRids) operationRids.Add(rid);
                        allShaderPaths.AddRange(pipePaths);
                    }
                }

                // Combine commands
                Action<long> combined = (computeList) =>
                {
                    for (int i = 0; i < allCommands.Count; i++)
                    {
                        allCommands[i]?.Invoke(computeList);
                        if (i < allCommands.Count - 1)
                            Gpu.Rd.ComputeListAddBarrier(computeList);
                    }
                };

                // Build cleanup list: operation RIDs first, then owner RIDs
                var finalCleanupList = operationRids.ToList();
                if (heightmapArrayRid.IsValid) finalCleanupList.Add(heightmapArrayRid);
                if (metadataBufferRid.IsValid) finalCleanupList.Add(metadataBufferRid);

                return (combined, finalCleanupList, allShaderPaths);
            };

            var owners = new List<object> { layer };
            return new AsyncGpuTask(
                generator,
                null,
                owners,
                $"Height Mask: {layerName} (Lazy+Staging)",
                null);
        }
    }
}