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
    /// Phase 6: Applies feature layers to region data (both height and control maps).
    /// This is the final phase that modifies the actual terrain data.
    /// Depends on: FeatureLayerMaskPhase, RegionHeightCompositePhase, RegionTextureCompositePhase
    /// </summary>
    public class FeatureLayerApplicationPhase : IProcessingPhase
    {
        public Dictionary<object, AsyncGpuTask> Execute(TerrainProcessingContext context)
        {
            var tasks = new Dictionary<object, AsyncGpuTask>();

            foreach (var regionCoords in context.AllDirtyRegions)
            {
                if (!context.CurrentlyActiveRegions.Contains(regionCoords)) continue;

                var tieredLayers = context.RegionDependencyManager.GetTieredLayersForRegion(regionCoords);
                if (tieredLayers == null) continue;

                var validFeatureLayers = tieredLayers.FeatureLayers
                    .Where(l => GodotObject.IsInstanceValid(l))
                    .ToList();

                if (validFeatureLayers.Count == 0) continue;

                var currentRegionCoords = regionCoords;
                var dependencies = new List<AsyncGpuTask>();

                // Build dependencies for this region's feature layer application
                foreach (var featureLayer in validFeatureLayers)
                {
                    // Wait for this feature layer's mask to be generated
                    if (context.FeatureLayerMaskTasks.ContainsKey(featureLayer))
                    {
                        dependencies.Add(context.FeatureLayerMaskTasks[featureLayer]);
                    }

                    // If modifying height, also wait for this region's height composite
                    if (featureLayer.ModifiesHeight && 
                        context.RegionHeightCompositeTasks.ContainsKey(regionCoords))
                    {
                        dependencies.Add(context.RegionHeightCompositeTasks[regionCoords]);
                    }

                    // If modifying texture, also wait for this region's texture composite
                    if (featureLayer.ModifiesTexture && 
                        context.RegionTextureCompositeTasks.ContainsKey(regionCoords))
                    {
                        dependencies.Add(context.RegionTextureCompositeTasks[regionCoords]);
                    }
                }

                Action onComplete = () =>
                {
                    context.RegionMapManager.RefreshRegionPreview(currentRegionCoords);
                    context.RegionDependencyManager.MarkRegionUpdated(currentRegionCoords);
                };

                var task = CreateFeatureLayerApplicationTask(
                    currentRegionCoords,
                    validFeatureLayers,
                    dependencies,
                    onComplete,
                    context);

                if (task != null)
                {
                    tasks[currentRegionCoords] = task;
                    AsyncGpuTaskManager.Instance.AddTask(task);
                }
            }

            return tasks;
        }

        private AsyncGpuTask CreateFeatureLayerApplicationTask(
            Vector2I regionCoords,
            List<TerrainLayerBase> featureLayers,
            List<AsyncGpuTask> dependencies,
            Action onComplete,
            TerrainProcessingContext context)
        {
            var regionData = context.RegionMapManager.GetRegionData(regionCoords);
            if (regionData == null) return null;

            var regionMin = TerrainCoordinateHelper.RegionMinWorld(regionCoords, context.RegionSize);
            var regionSizeWorld = new Vector2(context.RegionSize, context.RegionSize);

            var allCommands = new List<Action<long>>();
            var allTempRids = new List<Rid>();
            var allShaderPaths = new List<string>();

            foreach (var layer in featureLayers)
            {
                var (cmd, rids, shaders) = layer.CreateApplyRegionCommands(
                    regionCoords,
                    regionData,
                    context.RegionSize,
                    regionMin,
                    regionSizeWorld);

                if (cmd != null)
                {
                    allCommands.Add(cmd);
                    allTempRids.AddRange(rids);
                    allShaderPaths.AddRange(shaders);
                }
            }

            if (allCommands.Count == 0) return null;

            Action<long> combinedCommands = (computeList) =>
            {
                for (int i = 0; i < allCommands.Count; i++)
                {
                    if (i > 0)
                    {
                        Gpu.Rd.ComputeListAddBarrier(computeList);
                    }

                    try
                    {
                        allCommands[i]?.Invoke(computeList);
                    }
                    catch (Exception ex)
                    {
                        GD.PrintErr($"[FeatureLayerApplicationPhase] Failed to execute command {i}: {ex.Message}");
                        break;
                    }
                }
            };

            var owners = new List<object> { regionData };
            owners.AddRange(featureLayers);

            return new AsyncGpuTask(
                combinedCommands,
                onComplete,
                allTempRids,
                owners,
                $"Feature Layer Application: Region {regionCoords}",
                dependencies,
                allShaderPaths);
        }
    }
}