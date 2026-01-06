// /Pipeline/ExclusionMapWritePhase.cs
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using Terrain3DTools.Layers;
using Terrain3DTools.Core;
using Terrain3DTools.Core.Debug;
using Terrain3DTools.Utils;

namespace Terrain3DTools.Pipeline
{
    /// <summary>
    /// Phase 7: Clears and writes exclusion maps for regions.
    /// Non-instancer feature layers (paths, object placers) write their influence
    /// to the exclusion map using MAX blend mode.
    /// </summary>
    public class ExclusionMapWritePhase : IProcessingPhase
    {
        private const string DEBUG_CLASS_NAME = "ExclusionMapWritePhase";

        public ExclusionMapWritePhase()
        {
            DebugManager.Instance?.RegisterClass(DEBUG_CLASS_NAME);
        }

        public Dictionary<object, AsyncGpuTask> Execute(TerrainProcessingContext context)
        {
            var tasks = new Dictionary<object, AsyncGpuTask>();

            foreach (var regionCoords in context.AllDirtyRegions)
            {
                if (!context.CurrentlyActiveRegions.Contains(regionCoords)) continue;

                var regionData = context.RegionMapManager.GetRegionData(regionCoords);
                if (regionData == null) continue;

                var tieredLayers = context.RegionDependencyManager.GetTieredLayersForRegion(regionCoords);
                if (tieredLayers == null) continue;

                // Get non-instancer feature layers that can write exclusion
                var exclusionWriters = tieredLayers.FeatureLayers
                    .OfType<FeatureLayer>()
                    .Where(f => GodotObject.IsInstanceValid(f) && !f.IsInstancer)
                    .ToList();

                // Check if any instancers will need the exclusion map
                var hasInstancers = tieredLayers.FeatureLayers
                    .OfType<InstancerLayer>()
                    .Any(l => GodotObject.IsInstanceValid(l));

                if (!hasInstancers)
                {
                    // No instancers need the exclusion map - skip
                    continue;
                }

                // Build dependencies
                var dependencies = new List<AsyncGpuTask>();

                // Depend on feature application (paths need to be applied first)
                if (context.RegionFeatureApplicationTasks.TryGetValue(regionCoords, out var featureAppTask))
                {
                    dependencies.Add(featureAppTask);
                }

                // Also depend on individual feature mask tasks
                foreach (var writer in exclusionWriters)
                {
                    if (context.FeatureLayerMaskTasks.TryGetValue(writer, out var maskTask))
                    {
                        dependencies.Add(maskTask);
                    }
                }

                var currentRegionCoords = regionCoords;

                var task = CreateExclusionWriteTask(
                    currentRegionCoords,
                    regionData,
                    exclusionWriters,
                    dependencies,
                    context);

                if (task != null)
                {
                    tasks[regionCoords] = task;
                    context.ExclusionWriteTasks[regionCoords] = task;
                    AsyncGpuTaskManager.Instance.AddTask(task);

                    DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.PhaseExecution,
                        $"Created exclusion write task for region {regionCoords} with {exclusionWriters.Count} writers");
                }
            }

            return tasks;
        }

        private AsyncGpuTask CreateExclusionWriteTask(
            Vector2I regionCoords,
            RegionData regionData,
            List<FeatureLayer> exclusionWriters,
            List<AsyncGpuTask> dependencies,
            TerrainProcessingContext context)
        {
            int regionSize = context.RegionSize;

            // Capture state for lazy evaluation
            var writerStates = new List<(FeatureLayer layer, Rid maskRid, Vector2 min, Vector2 max)>();
            foreach (var writer in exclusionWriters)
            {
                var (min, max) = writer.GetWorldBounds();
                writerStates.Add((writer, writer.layerTextureRID, min, max));
            }

            Func<(Action<long>, List<Rid>, List<string>)> generator = () =>
            {
                var allCommands = new List<Action<long>>();
                var allTempRids = new List<Rid>();
                var allShaderPaths = new List<string>();

                // Ensure exclusion map exists
                var exclusionMap = regionData.GetOrCreateExclusionMap(regionSize);

                // Step 1: Clear exclusion map to 0
                if (regionData.ExclusionMapNeedsClearing)
                {
                    var (clearCmd, clearRids, clearShader) = GpuKernels.CreateClearCommands(
                        exclusionMap,
                        new Color(0, 0, 0, 0),
                        regionSize,
                        regionSize,
                        DEBUG_CLASS_NAME);

                    if (clearCmd != null)
                    {
                        allCommands.Add(clearCmd);
                        allTempRids.AddRange(clearRids);
                        allShaderPaths.Add(clearShader);
                    }

                    regionData.ClearExclusionMapFlag();
                }

                // Step 2: Each writer adds to exclusion map (MAX blend)
                var regionMin = TerrainCoordinateHelper.RegionMinWorld(regionCoords, regionSize);
                var regionSizeWorld = new Vector2(regionSize, regionSize);

                foreach (var (layer, maskRid, maskMin, maskMax) in writerStates)
                {
                    if (!GodotObject.IsInstanceValid(layer)) continue;

                    var (cmd, rids, shaders) = layer.CreateWriteExclusionCommands(
                        regionCoords,
                        regionData,
                        regionSize,
                        regionMin,
                        regionSizeWorld);

                    if (cmd != null)
                    {
                        allCommands.Add(cmd);
                        allTempRids.AddRange(rids);
                        allShaderPaths.AddRange(shaders);
                    }
                }

                if (allCommands.Count == 0)
                {
                    return ((l) => { }, new List<Rid>(), new List<string>());
                }

                Action<long> combinedCommands = (computeList) =>
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

                return (combinedCommands, allTempRids, allShaderPaths);
            };

            var owners = new List<object> { regionData };
            owners.AddRange(exclusionWriters);

            return new AsyncGpuTask(
                generator,
                null,
                owners,
                $"Exclusion write: Region {regionCoords}",
                dependencies);
        }
    }
}