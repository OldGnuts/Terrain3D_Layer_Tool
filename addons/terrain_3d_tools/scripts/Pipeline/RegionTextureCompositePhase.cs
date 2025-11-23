// /Core/Pipeline/RegionTextureCompositePhase.cs 
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
    /// Phase 4: Composites texture layers into region control maps.
    /// Depends on: TextureLayerMaskPhase
    /// </summary>
    public class RegionTextureCompositePhase : IProcessingPhase
    {
        public Dictionary<object, AsyncGpuTask> Execute(TerrainProcessingContext context)
        {
            var tasks = new Dictionary<object, AsyncGpuTask>();

            foreach (var regionCoords in context.AllDirtyRegions)
            {
                if (!context.CurrentlyActiveRegions.Contains(regionCoords)) continue;

                // Get ALL layers affecting this region
                var tieredLayers = context.RegionDependencyManager.GetTieredLayersForRegion(regionCoords);

                if (tieredLayers == null || !tieredLayers.ShouldProcess())
                {
                    continue; // Skip texture-only regions without height data
                }

                var allTextureLayers = tieredLayers.TextureLayers
                    .Where(l => GodotObject.IsInstanceValid(l))
                    .ToList();

                var currentRegionCoords = regionCoords;

                // Dependencies only for newly generated masks
                var dependencies = allTextureLayers
                    .Where(l => context.TextureLayerMaskTasks.ContainsKey(l))
                    .Select(l => context.TextureLayerMaskTasks[l])
                    .ToList();

                // Also depend on height composite if it exists
                if (context.RegionHeightCompositeTasks.ContainsKey(regionCoords))
                {
                    dependencies.Add(context.RegionHeightCompositeTasks[regionCoords]);
                }

                Action onComplete = () =>
                {
                    context.RegionDependencyManager.MarkRegionUpdated(currentRegionCoords);
                };

                var task = CreateRegionControlCompositeTask(
                    currentRegionCoords,
                    allTextureLayers,  // ALL layers, not just dirty ones
                    dependencies,
                    onComplete,
                    context);

                if (task != null)
                {
                    context.RegionTextureCompositeTasks[currentRegionCoords] = task;
                    tasks[currentRegionCoords] = task;
                    AsyncGpuTaskManager.Instance.AddTask(task);
                }
            }

            return tasks;
        }

        private AsyncGpuTask CreateRegionControlCompositeTask(
            Vector2I regionCoords,
            List<TerrainLayerBase> textureLayers,
            List<AsyncGpuTask> dependencies,
            Action onComplete,
            TerrainProcessingContext context)
        {
            var regionData = context.RegionMapManager.GetOrCreateRegionData(regionCoords);
            if (!regionData.ControlMap.IsValid) return null;

            var allCommands = new List<Action<long>>();
            var allTempRids = new List<Rid>();

            var (clearCmd, clearRids, shaderPaths) = GpuKernels.CreateClearCommands(
                regionData.ControlMap,
                Colors.Black,
                context.RegionSize,
                context.RegionSize);

            if (clearCmd != null) allCommands.Add(clearCmd);
            allTempRids.AddRange(clearRids);

            if (textureLayers.Count > 0)
            {
                var regionMin = TerrainCoordinateHelper.RegionMinWorld(regionCoords, context.RegionSize);
                var regionSizeWorld = new Vector2(context.RegionSize, context.RegionSize);

                foreach (var layer in textureLayers)
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
                foreach (var cmd in allCommands) cmd?.Invoke(computeList);
            };

            var owners = new List<object> { regionData };
            owners.AddRange(textureLayers);

            return new AsyncGpuTask(
                combinedCommands,
                onComplete,
                allTempRids,
                owners,
                "Region control map composite",
                dependencies,
                shaderPaths);
        }
    }
}
