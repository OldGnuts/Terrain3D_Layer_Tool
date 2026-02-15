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
    /// <summary>
    /// Phase 5: Generates mask textures for all dirty feature layers.
    /// Uses lazy (JIT) initialization and HeightDataStager.
    /// </summary>
    public class FeatureLayerMaskPhase : IProcessingPhase
    {
        private const string DEBUG_CLASS_NAME = "FeatureLayerMaskPhase";

        public FeatureLayerMaskPhase()
        {
            DebugManager.Instance?.RegisterClass(DEBUG_CLASS_NAME);
        }

        public Dictionary<object, AsyncGpuTask> Execute(TerrainProcessingContext context)
        {
            var tasks = new Dictionary<object, AsyncGpuTask>();
            
            DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.PhaseExecution,
                $"Processing {context.DirtyFeatureLayers.Count} feature layer(s)");

            foreach (var layer in context.DirtyFeatureLayers)
            {
                var featureLayer = layer as FeatureLayer;
                if (featureLayer == null || !GodotObject.IsInstanceValid(layer)) continue;

                featureLayer.SetWorldHeightScale(context.WorldHeightScale);

                var dependencies = new List<AsyncGpuTask>();
                var readSources = new List<Rid>();

                var overlappingRegionCoords = TerrainCoordinateHelper
                    .GetRegionBoundsForLayer(layer, context.RegionSize)
                    .GetRegionCoords()
                    .ToList();

                if (featureLayer.ModifiesHeight || featureLayer.DoesAnyMaskRequireHeightData())
                {
                    var heightDependencies = overlappingRegionCoords
                        .Where(rc => context.RegionHeightCompositeTasks.ContainsKey(rc))
                        .Select(rc => context.RegionHeightCompositeTasks[rc])
                        .ToList();
                    dependencies.AddRange(heightDependencies);

                    foreach (var regionCoord in overlappingRegionCoords)
                    {
                        var regionData = context.RegionMapManager.GetRegionData(regionCoord);
                        if (regionData?.HeightMap.IsValid == true)
                        {
                            readSources.Add(regionData.HeightMap);
                        }
                    }
                }

                if (featureLayer.ModifiesTexture)
                {
                    var textureDependencies = overlappingRegionCoords
                        .Where(rc => context.RegionTextureCompositeTasks.ContainsKey(rc))
                        .Select(rc => context.RegionTextureCompositeTasks[rc])
                        .ToList();
                    dependencies.AddRange(textureDependencies);
                }

                Action onComplete = () =>
                {
                    if (GodotObject.IsInstanceValid(layer))
                        layer.Visualizer?.Update();
                };

                var maskTask = CreateFeatureMaskTaskLazy(
                    featureLayer,
                    context.RegionMapManager,
                    context.RegionSize,
                    dependencies,
                    onComplete);

                if (maskTask != null)
                {
                    var writeTargets = layer.GetMaskWriteTargets().ToList();
                    
                    maskTask.DeclareResources(
                        writes: writeTargets,
                        reads: readSources
                    );

                    context.FeatureLayerMaskTasks[layer] = maskTask;
                    tasks[layer] = maskTask;
                    AsyncGpuTaskManager.Instance.AddTask(maskTask);
                    
                    DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.PhaseExecution,
                        $"Feature mask task added for '{layer.LayerName}' with {writeTargets.Count} write target(s)");
                }
            }
            return tasks;
        }

        private AsyncGpuTask CreateFeatureMaskTaskLazy(
            FeatureLayer layer,
            RegionMapManager regionMapManager,
            int regionSize,
            List<AsyncGpuTask> dependencies,
            Action onComplete)
        {
            string layerName = layer.LayerName;
            int width = layer.Size.X;
            int height = layer.Size.Y;

            Func<(Action<long>, List<Rid>, List<string>)> generator = () =>
            {
                if (!GodotObject.IsInstanceValid(layer)) 
                    return ((l) => { }, new List<Rid>(), new List<string>());

                var allCommands = new List<Action<long>>();
                var operationRids = new HashSet<Rid>();
                var allShaderPaths = new List<string>();

                Rid heightmapArrayRid = new Rid();
                Rid metadataBufferRid = new Rid();
                int stagedRegionCount = 0;

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
                    }
                }

                var pipelineTask = LayerMaskPipeline.CreateUpdateFeatureLayerTextureTask(
                    layer.layerTextureRID,
                    layer,
                    width,
                    height,
                    heightmapArrayRid,
                    metadataBufferRid,
                    stagedRegionCount,
                    null, 
                    null  
                );

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

            var owners = new List<object> { layer };
            return new AsyncGpuTask(
                generator,
                onComplete,
                owners,
                $"Feature Mask: {layerName}",
                dependencies);
        }
    }
}