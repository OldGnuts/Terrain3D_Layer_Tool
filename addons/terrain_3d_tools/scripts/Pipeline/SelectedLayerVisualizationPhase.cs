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
    /// Phase 11: Updates visualization for the currently selected layer.
    /// Strictly enforces cleanup order for GPU resources.
    /// </summary>
    public class SelectedLayerVisualizationPhase : IProcessingPhase
    {
        private const string DEBUG_CLASS_NAME = "SelectedLayerVisualizationPhase";

        public SelectedLayerVisualizationPhase()
        {
            DebugManager.Instance?.RegisterClass(DEBUG_CLASS_NAME);
        }

        public Dictionary<object, AsyncGpuTask> Execute(TerrainProcessingContext context)
        {
            var tasks = new Dictionary<object, AsyncGpuTask>();
            var selectedLayer = context.SelectedLayer;

            if (selectedLayer == null || !GodotObject.IsInstanceValid(selectedLayer)) 
                return tasks;

            if (selectedLayer.DoesAnyMaskRequireHeightData())
            {
                DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.PhaseExecution,
                    $"Layer '{selectedLayer.LayerName}' mask pipeline handles visualization - skipping phase");
                return tasks;
            }

            DebugManager.Instance?.StartTimer(DEBUG_CLASS_NAME, DebugCategory.PhaseExecution,
                $"PlanVisualization:{selectedLayer.LayerName}");

            var overlappingRegions = TerrainCoordinateHelper
                .GetRegionBoundsForLayer(selectedLayer, context.RegionSize)
                .GetRegionCoords()
                .Where(coord => context.CurrentlyActiveRegions.Contains(coord))
                .ToList();

            if (overlappingRegions.Count == 0)
            {
                DebugManager.Instance?.EndTimer(DEBUG_CLASS_NAME, DebugCategory.PhaseExecution,
                    $"PlanVisualization:{selectedLayer.LayerName}");
                return tasks;
            }

            // Build dependencies - visualization runs after everything else
            var dependencies = new List<AsyncGpuTask>();
            
            foreach (var coord in overlappingRegions)
            {
                if (context.RegionHeightCompositeTasks.TryGetValue(coord, out var heightTask))
                    dependencies.Add(heightTask);
                
                if (context.RegionTextureCompositeTasks.TryGetValue(coord, out var textureTask))
                    dependencies.Add(textureTask);
                
                if (context.RegionFeatureApplicationTasks.TryGetValue(coord, out var featureTask))
                    dependencies.Add(featureTask);

                if (context.BlendSmoothingTasks.TryGetValue(coord, out var smoothTask))
                    dependencies.Add(smoothTask);

                if (context.ManualEditApplicationTasks.TryGetValue(coord, out var manualTask))
                    dependencies.Add(manualTask);
            }

            // Collect read sources (heightmaps from overlapping regions)
            var readSources = new List<Rid>();
            foreach (var coord in overlappingRegions)
            {
                var regionData = context.RegionMapManager.GetRegionData(coord);
                if (regionData?.HeightMap.IsValid == true)
                    readSources.Add(regionData.HeightMap);
            }

            // Write target is the visualization texture
            var writeTargets = new List<Rid>();
            if (selectedLayer.layerHeightVisualizationTextureRID.IsValid)
                writeTargets.Add(selectedLayer.layerHeightVisualizationTextureRID);

            var task = CreateVisualizationTaskLazy(
                selectedLayer,
                context.RegionMapManager,
                context.RegionSize,
                dependencies);

            if (task != null)
            {
                task.DeclareResources(
                    writes: writeTargets,
                    reads: readSources
                );

                tasks[selectedLayer] = task;
                AsyncGpuTaskManager.Instance.AddTask(task);
            }

            DebugManager.Instance?.EndTimer(DEBUG_CLASS_NAME, DebugCategory.PhaseExecution,
                $"PlanVisualization:{selectedLayer.LayerName}");

            return tasks;
        }

        private AsyncGpuTask CreateVisualizationTaskLazy(
            TerrainLayerBase layer,
            RegionMapManager regionMapManager,
            int regionSize,
            List<AsyncGpuTask> dependencies)
        {
            string layerName = layer.LayerName;
            int width = layer.Size.X;
            int height = layer.Size.Y;
            
            Func<(Action<long>, List<Rid>, List<string>)> generator = () =>
            {
                if (!GodotObject.IsInstanceValid(layer) || !layer.layerHeightVisualizationTextureRID.IsValid)
                {
                    return ((l) => { }, new List<Rid>(), new List<string>());
                }

                DebugManager.Instance?.StartTimer(DEBUG_CLASS_NAME, DebugCategory.GpuResources, 
                    $"JIT_Prep_Viz:{layerName}");

                var allCommands = new List<Action<long>>();
                var operationRids = new HashSet<Rid>();
                var allShaderPaths = new List<string>();

                Rid heightmapArrayRid = new Rid();
                Rid metadataBufferRid = new Rid();

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
                }

                var (clearCmd, clearRids, clearShader) = GpuKernels.CreateClearCommands(
                    layer.layerHeightVisualizationTextureRID,
                    Colors.Black,
                    width,
                    height,
                    DEBUG_CLASS_NAME);

                if (clearCmd != null)
                {
                    allCommands.Add(clearCmd);
                    foreach (var rid in clearRids) operationRids.Add(rid);
                    allShaderPaths.Add(clearShader);
                }

                if (heightmapArrayRid.IsValid && metadataBufferRid.IsValid)
                {
                    var (stitchCmd, stitchRids, stitchShader) = GpuKernels.CreateStitchHeightmapCommands(
                        layer.layerHeightVisualizationTextureRID,
                        heightmapArrayRid,
                        metadataBufferRid,
                        width,
                        height,
                        stagingResult.ActiveRegionCount,
                        DEBUG_CLASS_NAME);

                    if (stitchCmd != null)
                    {
                        allCommands.Add(stitchCmd);
                        foreach (var rid in stitchRids) operationRids.Add(rid);
                        allShaderPaths.Add(stitchShader);
                    }
                }

                DebugManager.Instance?.EndTimer(DEBUG_CLASS_NAME, DebugCategory.GpuResources, 
                    $"JIT_Prep_Viz:{layerName}");

                if (allCommands.Count == 0)
                    return ((l) => { }, new List<Rid>(), new List<string>());

                Action<long> combined = (computeList) =>
                {
                    for (int i = 0; i < allCommands.Count; i++)
                    {
                        allCommands[i]?.Invoke(computeList);
                        if (i < allCommands.Count - 1)
                            Gpu.Rd.ComputeListAddBarrier(computeList);
                    }
                };

                var finalCleanupList = operationRids.ToList();
                if (heightmapArrayRid.IsValid) finalCleanupList.Add(heightmapArrayRid);
                if (metadataBufferRid.IsValid) finalCleanupList.Add(metadataBufferRid);

                return (combined, finalCleanupList, allShaderPaths);
            };

            Action onComplete = () =>
            {
                if (GodotObject.IsInstanceValid(layer))
                    layer.Visualizer?.Update();
            };

            var owners = new List<object> { layer };
            
            return new AsyncGpuTask(
                generator,
                onComplete,
                owners,
                $"Visualization: {layerName}",
                dependencies);
        }
    }
}