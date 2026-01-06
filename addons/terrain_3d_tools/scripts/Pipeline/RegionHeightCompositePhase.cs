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
    /// Refactored to use Lazy (JIT) initialization to prevent allocation spikes.
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
                };

                var task = CreateRegionHeightCompositeTaskLazy(
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
                }
            }

            return tasks;
        }

        /// <summary>
        /// Creates a Lazy AsyncGpuTask to composite height layers.
        /// Allocation of commands and resources happens only when the task is Prepared.
        /// </summary>
        private AsyncGpuTask CreateRegionHeightCompositeTaskLazy(
            Vector2I regionCoords,
            List<TerrainLayerBase> heightLayers,
            List<AsyncGpuTask> dependencies,
            Action onComplete,
            TerrainProcessingContext context)
        {
            // Capture state for the closure
            int regionSize = context.RegionSize;
            
            // --- GENERATOR FUNCTION ---
            Func<(Action<long>, List<Rid>, List<string>)> generator = () =>
            {
                var regionData = context.RegionMapManager.GetOrCreateRegionData(regionCoords);

                DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.RegionCompositing,
                    $"JIT Prep: Region {regionCoords}");

                var allCommands = new List<Action<long>>();
                var allTempRids = new List<Rid>();
                var allShaderPaths = new List<string>();

                // 1. Clear Command
                var (clearCmd, clearRids, clearShader) = GpuKernels.CreateClearCommands(
                    regionData.HeightMap,
                    Colors.Black,
                    regionSize,
                    regionSize,
                    DEBUG_CLASS_NAME);

                if (clearCmd != null)
                {
                    allCommands.Add(clearCmd);
                    allTempRids.AddRange(clearRids);
                    allShaderPaths.Add(clearShader);
                }

                // 2. Apply Layers
                if (heightLayers.Count > 0)
                {
                    var regionMin = TerrainCoordinateHelper.RegionMinWorld(regionCoords, regionSize);
                    var regionSizeWorld = new Vector2(regionSize, regionSize);

                    foreach (var layer in heightLayers)
                    {
                        if (!GodotObject.IsInstanceValid(layer)) continue;

                        var (applyCmd, applyRids, applyShaderPaths) = layer.CreateApplyRegionCommands(
                            regionCoords,
                            regionData,
                            regionSize,
                            regionMin,
                            regionSizeWorld);

                        if (applyCmd != null)
                        {
                            allCommands.Add(applyCmd);
                            allTempRids.AddRange(applyRids);
                            allShaderPaths.AddRange(applyShaderPaths);
                        }
                    }
                }

                // 3. Combine
                if (allCommands.Count == 0) 
                {
                    return ((l) => { }, new List<Rid>(), new List<string>());
                }

                Action<long> combinedCommands = (computeList) =>
                {
                    // Barrier before start
                    Gpu.Rd.ComputeListAddBarrier(computeList);
                    
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

            // Owners for resource tracking (safe to resolve now as they are just object references)
            // Note: We access RegionData here just to get the reference for 'owners', 
            // but we don't allocate its textures here. The RegionMapManager manages that lifecycle.
            // If RegionData creation is expensive, it might be an issue, but usually it's just RIDs.
            // Actually, context.RegionMapManager.GetOrCreateRegionData IS executed immediately here
            // to populate 'owners'. This is acceptable because regions persist across updates.
            // We are mostly saving on the CreateClearCommands / CreateApplyRegionCommands allocation.
            var regionDataRef = context.RegionMapManager.GetOrCreateRegionData(regionCoords);
            var owners = new List<object> { regionDataRef };
            owners.AddRange(heightLayers);

            return new AsyncGpuTask(
                generator,
                onComplete,
                owners,
                $"Region {regionCoords} height composite (Lazy)",
                dependencies);
        }
    }
}