// /Core/Pipeline/FeatureLayerApplicationPhase.cs
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
    /// Phase 6: Applies feature layers to region data (both height and control maps).
    /// This is the final modification phase that allows complex features like paths or roads
    /// to alter the composited terrain. Features can modify height geometry, texture blending,
    /// or both, making this the last phase before terrain data is ready for use.
    /// Depends on: FeatureLayerMaskPhase, RegionHeightCompositePhase, RegionTextureCompositePhase
    /// </summary>
    public class FeatureLayerApplicationPhase : IProcessingPhase
    {
        private const string DEBUG_CLASS_NAME = "FeatureLayerApplicationPhase";

        public FeatureLayerApplicationPhase()
        {
            DebugManager.Instance?.RegisterClass(DEBUG_CLASS_NAME);
        }

        public Dictionary<object, AsyncGpuTask> Execute(TerrainProcessingContext context)
        {
            var tasks = new Dictionary<object, AsyncGpuTask>();

            DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.PhaseExecution,
                $"Applying features to {context.AllDirtyRegions.Count} region(s)");

            int regionsWithFeatures = 0;

            foreach (var regionCoords in context.AllDirtyRegions)
            {
                if (!context.CurrentlyActiveRegions.Contains(regionCoords))
                {
                    continue;
                }

                var tieredLayers = context.RegionDependencyManager.GetTieredLayersForRegion(regionCoords);
                if (tieredLayers == null)
                {
                    continue;
                }

                var validFeatureLayers = tieredLayers.FeatureLayers
                    .Where(l => GodotObject.IsInstanceValid(l))
                    .ToList();

                if (validFeatureLayers.Count == 0)
                {
                    continue;
                }

                regionsWithFeatures++;

                var currentRegionCoords = regionCoords;
                var dependencies = new List<AsyncGpuTask>();

                foreach (var featureLayer in validFeatureLayers)
                {
                    if (context.FeatureLayerMaskTasks.ContainsKey(featureLayer))
                    {
                        dependencies.Add(context.FeatureLayerMaskTasks[featureLayer]);
                    }

                    if (featureLayer.ModifiesHeight && 
                        context.RegionHeightCompositeTasks.ContainsKey(regionCoords))
                    {
                        dependencies.Add(context.RegionHeightCompositeTasks[regionCoords]);
                    }

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

                    DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.RegionCompositing,
                        $"Created feature application task for region {currentRegionCoords} with {validFeatureLayers.Count} feature(s)");
                }
            }

            DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.PhaseExecution,
                $"Feature application - {regionsWithFeatures} region(s) have features");

            return tasks;
        }

        /// <summary>
        /// Creates a GPU task to apply all feature layers to a region's heightmap and control map.
        /// Features are applied in order, with barriers between each to ensure proper sequencing.
        /// </summary>
        private AsyncGpuTask CreateFeatureLayerApplicationTask(
            Vector2I regionCoords,
            List<TerrainLayerBase> featureLayers,
            List<AsyncGpuTask> dependencies,
            Action onComplete,
            TerrainProcessingContext context)
        {
            var regionData = context.RegionMapManager.GetRegionData(regionCoords);
            if (regionData == null)
            {
                DebugManager.Instance?.LogWarning(DEBUG_CLASS_NAME,
                    $"Region {regionCoords} has no data for feature application");
                return null;
            }

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

            if (allCommands.Count == 0)
            {
                DebugManager.Instance?.LogWarning(DEBUG_CLASS_NAME,
                    $"No feature commands generated for region {regionCoords}");
                return null;
            }

            DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.RegionCompositing,
                $"Region {regionCoords} - Applying {featureLayers.Count} feature layer(s)");

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
            owners.AddRange(featureLayers);

            return new AsyncGpuTask(
                combinedCommands,
                onComplete,
                allTempRids,
                owners,
                $"Feature application: Region {regionCoords}",
                dependencies,
                allShaderPaths);
        }
    }
}