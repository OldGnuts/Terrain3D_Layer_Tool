// /Pipeline/FeatureLayerApplicationPhase.cs
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using Terrain3DTools.Layers;
using Terrain3DTools.Core;
using Terrain3DTools.Utils;
using Terrain3DTools.Core.Debug;

namespace Terrain3DTools.Pipeline
{
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

                foreach (var featureLayer in validFeatureLayers)
                {
                    if (context.FeatureLayerMaskTasks.ContainsKey(featureLayer))
                        dependencies.Add(context.FeatureLayerMaskTasks[featureLayer]);
                    
                    if (featureLayer.ModifiesHeight && context.RegionHeightCompositeTasks.ContainsKey(regionCoords))
                        dependencies.Add(context.RegionHeightCompositeTasks[regionCoords]);

                    if (featureLayer.ModifiesTexture && context.RegionTextureCompositeTasks.ContainsKey(regionCoords))
                        dependencies.Add(context.RegionTextureCompositeTasks[regionCoords]);
                }

                Action onComplete = () =>
                {
                    context.RegionMapManager.RefreshRegionPreview(currentRegionCoords);
                };

                var task = CreateFeatureLayerApplicationTaskLazy(
                    currentRegionCoords,
                    validFeatureLayers,
                    dependencies,
                    onComplete,
                    context);

                if (task != null)
                {
                    tasks[currentRegionCoords] = task;
                    context.RegionFeatureApplicationTasks[currentRegionCoords] = task; 
                    AsyncGpuTaskManager.Instance.AddTask(task);
                    DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.PhaseExecution,
                        $"Feature layer application task added to AsynceGpuTaskManager on Frame : {Engine.GetProcessFrames}");
                }
            }

            return tasks;
        }

        private AsyncGpuTask CreateFeatureLayerApplicationTaskLazy(
            Vector2I regionCoords,
            List<TerrainLayerBase> featureLayers,
            List<AsyncGpuTask> dependencies,
            Action onComplete,
            TerrainProcessingContext context)
        {
            int regionSize = context.RegionSize;
            
            // Capture BakeState for all PathLayers NOW (Main Thread).
            // This ensures the worker thread sees the exact same state that Phase 5 saw.
            var pathLayerStates = new Dictionary<PathLayer, PathLayer.PathBakeState>();
            foreach (var layer in featureLayers)
            {
                if (layer is PathLayer pl)
                {
                    pathLayerStates[pl] = pl.GetActiveBakeState();
                }
            }

            Func<(Action<long>, List<Rid>, List<string>)> generator = () =>
            {
                var regionData = context.RegionMapManager.GetRegionData(regionCoords);
                if (regionData == null)
                {
                    return ((l) => { }, new List<Rid>(), new List<string>());
                }

                var allCommands = new List<Action<long>>();
                var allTempRids = new List<Rid>();
                var allShaderPaths = new List<string>();

                var regionMin = TerrainCoordinateHelper.RegionMinWorld(regionCoords, regionSize);
                var regionSizeWorld = new Vector2(regionSize, regionSize);

                foreach (var layer in featureLayers)
                {
                    if (!GodotObject.IsInstanceValid(layer)) continue;

                    (Action<long> cmd, List<Rid> rids, List<string> shaders) result;

                    // If it's a PathLayer, inject the pre-captured state
                    if (layer is PathLayer pl && pathLayerStates.TryGetValue(pl, out var capturedState))
                    {
                        DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.PhaseExecution,
                            $"Creating apply region commands for path layer from state on frame {Engine.GetProcessFrames().ToString()} \n Captured state : {capturedState} \n for region at : {regionCoords}");
                                
                        result = pl.CreateApplyRegionCommandsFromState(
                            capturedState,
                            regionCoords,
                            regionData,
                            regionSize,
                            regionMin,
                            regionSizeWorld);
                    }
                    else
                    {
                        result = layer.CreateApplyRegionCommands(
                            regionCoords,
                            regionData,
                            regionSize,
                            regionMin,
                            regionSizeWorld);
                    }

                    if (result.cmd != null)
                    {
                        allCommands.Add(result.cmd);
                        allTempRids.AddRange(result.rids);
                        allShaderPaths.AddRange(result.shaders);
                    }
                }

                if (allCommands.Count == 0)
                {
                    return ((l) => { }, new List<Rid>(), new List<string>());
                }

                Action<long> combinedCommands = (computeList) =>
                {
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

            var regionDataRef = context.RegionMapManager.GetRegionData(regionCoords);
            var owners = new List<object> { regionDataRef };
            owners.AddRange(featureLayers);

            return new AsyncGpuTask(
                generator,
                onComplete,
                owners,
                $"Feature application: Region {regionCoords} (Lazy)",
                dependencies);
        }
    }
}