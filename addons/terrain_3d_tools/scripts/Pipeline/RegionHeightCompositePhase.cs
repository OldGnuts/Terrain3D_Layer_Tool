// /Core/Pipeline/RegionHeightCompositePhase.cs
using Godot;
using System.Collections.Generic;
using System.Linq;
using Terrain3DTools.Layers;
using Terrain3DTools.Utils;
using Terrain3DTools.Core;
using Terrain3DTools.Core.Debug;
using System;

namespace Terrain3DTools.Pipeline
{
    /// <summary>
    /// Phase 2: Composites height layers into region heightmaps.
    /// Takes all height layer masks that affect each region and applies them sequentially
    /// to build the final terrain elevation data. This phase produces the actual terrain
    /// geometry that will be used for visualization and gameplay.
    /// Depends on: HeightLayerMaskPhase (for dirty layers)
    /// </summary>
    public class RegionHeightCompositePhase : IProcessingPhase
    {
        private const string DEBUG_CLASS_NAME = "RegionHeightCompositePhase";

        public RegionHeightCompositePhase()
        {
            DebugManager.Instance?.RegisterClass(DEBUG_CLASS_NAME);
        }

        public Dictionary<object, AsyncGpuTask> Execute(TerrainProcessingContext context)
        {
            var tasks = new Dictionary<object, AsyncGpuTask>();

            DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.PhaseExecution,
                $"Compositing height data for {context.AllDirtyRegions.Count} region(s)");

            foreach (var regionCoords in context.AllDirtyRegions)
            {
                if (!context.CurrentlyActiveRegions.Contains(regionCoords))
                {
                    continue;
                }

                var tieredLayers = context.RegionDependencyManager.GetTieredLayersForRegion(regionCoords);

                var allHeightLayers = tieredLayers?.HeightLayers
                    .Where(l => GodotObject.IsInstanceValid(l))
                    .ToList() ?? new List<TerrainLayerBase>();

                var currentRegionCoords = regionCoords;

                var dependencies = allHeightLayers
                    .Where(l => context.HeightLayerMaskTasks.ContainsKey(l))
                    .Select(l => context.HeightLayerMaskTasks[l])
                    .ToList();

                Action onComplete = () =>
                {
                    context.RegionMapManager.RefreshRegionPreview(currentRegionCoords);
                    context.RegionDependencyManager.MarkRegionUpdated(currentRegionCoords);
                };

                var task = CreateRegionHeightCompositeTask(
                    currentRegionCoords,
                    allHeightLayers,
                    dependencies,
                    onComplete,
                    context);

                if (task != null)
                {
                    context.RegionHeightCompositeTasks[currentRegionCoords] = task;
                    tasks[currentRegionCoords] = task;
                    AsyncGpuTaskManager.Instance.AddTask(task);

                    DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.RegionCompositing,
                        $"Created composite task for region {currentRegionCoords} with {allHeightLayers.Count} layer(s)");
                }
            }

            return tasks;
        }

        /// <summary>
        /// Creates a GPU task to composite all height layers into a single region heightmap.
        /// First clears the region to a base elevation, then applies each layer's effect in order.
        /// </summary>
        private AsyncGpuTask CreateRegionHeightCompositeTask(
            Vector2I regionCoords,
            List<TerrainLayerBase> heightLayers,
            List<AsyncGpuTask> dependencies,
            Action onComplete,
            TerrainProcessingContext context)
        {
            var regionData = context.RegionMapManager.GetOrCreateRegionData(regionCoords);

            DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.RegionCompositing,
                $"Region {regionCoords} - HeightMap RID valid: {regionData.HeightMap.IsValid}");

            var allCommands = new List<Action<long>>();
            var allTempRids = new List<Rid>();
            var allShaderPaths = new List<string>();

            var (clearCmd, clearRids, clearShader) = GpuKernels.CreateClearCommands(
                regionData.HeightMap,
                Colors.Black,
                context.RegionSize,
                context.RegionSize,
                DEBUG_CLASS_NAME);

            if (clearCmd != null)
            {
                allCommands.Add(clearCmd);
                allTempRids.AddRange(clearRids);
                allShaderPaths.Add(clearShader);
            }

            if (heightLayers.Count > 0)
            {
                var regionMin = TerrainCoordinateHelper.RegionMinWorld(regionCoords, context.RegionSize);
                var regionSizeWorld = new Vector2(context.RegionSize, context.RegionSize);

                foreach (var layer in heightLayers)
                {
                    var (applyCmd, applyRids, applyShaderPaths) = layer.CreateApplyRegionCommands(
                        regionCoords,
                        regionData,
                        context.RegionSize,
                        regionMin,
                        regionSizeWorld);

                    if (applyCmd == null)
                    {
                        DebugManager.Instance?.LogWarning(DEBUG_CLASS_NAME,
                            $"Region {regionCoords} - Layer '{layer.LayerName}' returned null command!");
                    }
                    if (applyCmd != null)
                    {
                        allCommands.Add(applyCmd);
                        allTempRids.AddRange(applyRids);
                        allShaderPaths.AddRange(applyShaderPaths);
                    }
                }

                DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.RegionCompositing,
                    $"Region {regionCoords} - Applied {heightLayers.Count} height layer(s)");
            }

            Action<long> combinedCommands = (computeList) =>
            {
                if (allCommands.Count == 0) return;

                allCommands[0]?.Invoke(computeList);

                for (int i = 1; i < allCommands.Count; i++)
                {
                    Gpu.Rd.ComputeListAddBarrier(computeList);
                    allCommands[i]?.Invoke(computeList);
                }
            };

            var owners = new List<object> { regionData };
            owners.AddRange(heightLayers);

            return new AsyncGpuTask(
                combinedCommands,
                onComplete,
                allTempRids,
                owners,
                $"Region {regionCoords} height composite",
                dependencies,
                allShaderPaths);
        }
    }
}