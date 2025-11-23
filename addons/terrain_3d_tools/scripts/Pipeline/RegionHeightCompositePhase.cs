// /Core/Pipeline/RegionHeightCompositePhase.cs 
using Godot;
using System.Collections.Generic;
using System.Linq;
using Terrain3DTools.Layers;
using Terrain3DTools.Utils;
using Terrain3DTools.Core;
using System;

namespace Terrain3DTools.Pipeline
{
    /// <summary>
    /// Phase 2: Composites height layers into region height maps.
    /// Depends on: HeightLayerMaskPhase
    /// </summary>
    public class RegionHeightCompositePhase : IProcessingPhase
    {
        public Dictionary<object, AsyncGpuTask> Execute(TerrainProcessingContext context)
        {
            var tasks = new Dictionary<object, AsyncGpuTask>();

            foreach (var regionCoords in context.AllDirtyRegions)
            {
                if (!context.CurrentlyActiveRegions.Contains(regionCoords))
                {
                    continue;
                }

                // Get ALL layers affecting this region (not just dirty ones)
                var tieredLayers = context.RegionDependencyManager.GetTieredLayersForRegion(regionCoords);

                var allHeightLayers = tieredLayers?.HeightLayers
                    .Where(l => GodotObject.IsInstanceValid(l))
                    .ToList() ?? new List<TerrainLayerBase>();

                var currentRegionCoords = regionCoords;

                // Dependencies are only for layers that just generated masks
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
                    allHeightLayers,  // ALL layers, not just dirty ones
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

        private AsyncGpuTask CreateRegionHeightCompositeTask(
            Vector2I regionCoords,
            List<TerrainLayerBase> heightLayers,
            List<AsyncGpuTask> dependencies,
            Action onComplete,
            TerrainProcessingContext context)
        {
            var regionData = context.RegionMapManager.GetOrCreateRegionData(regionCoords);

            var allCommands = new List<Action<long>>();
            var allTempRids = new List<Rid>();

            var (clearCmd, clearRids, shaderPaths) = GpuKernels.CreateClearCommands(
                regionData.HeightMap,
                Colors.Black,
                context.RegionSize,
                context.RegionSize);

            if (clearCmd != null) allCommands.Add(clearCmd);
            allTempRids.AddRange(clearRids);

            // Only add layer application commands if there are layers
            // If heightLayers is empty, the region will just be cleared
            if (heightLayers.Count > 0)
            {
                var regionMin = TerrainCoordinateHelper.RegionMinWorld(regionCoords, context.RegionSize);
                var regionSizeWorld = new Vector2(context.RegionSize, context.RegionSize);

                foreach (var layer in heightLayers)
                {
                    var (applyCmd, applyRids, applyRegionShaderPaths) = layer.CreateApplyRegionCommands(
                        regionCoords,
                        regionData,
                        context.RegionSize,
                        regionMin,
                        regionSizeWorld);

                    if (applyCmd != null) allCommands.Add(applyCmd);
                    shaderPaths.AddRange(applyRegionShaderPaths);
                    allTempRids.AddRange(applyRids);
                }
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
                "Region height map composite",
                dependencies,
                shaderPaths);
        }
    }

}
