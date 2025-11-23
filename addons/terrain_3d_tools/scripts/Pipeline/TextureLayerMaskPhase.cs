// /Core/Pipeline/TextureLayerMaskPhase.cs (FIXED - handles clearing)
using Godot;
using System.Collections.Generic;
using System.Linq;
using Terrain3DTools.Layers;
using Terrain3DTools.Core;
using Terrain3DTools.Utils;

namespace Terrain3DTools.Pipeline
{
    /// <summary>
    /// Phase 3: Generates mask textures for all dirty texture layers.
    /// Texture layers may depend on height data for masks that use height-based logic.
    /// Depends on: RegionHeightCompositePhase (conditionally)
    /// </summary>
    public class TextureLayerMaskPhase : IProcessingPhase
    {
        public Dictionary<object, AsyncGpuTask> Execute(TerrainProcessingContext context)
        {
            var tasks = new Dictionary<object, AsyncGpuTask>();

            foreach (var layer in context.DirtyTextureLayers)
            {
                if (!GodotObject.IsInstanceValid(layer)) continue;

                // Check if this texture layer overlaps with any active (processable) regions
                var overlappingRegionCoords = TerrainCoordinateHelper
                    .GetRegionBoundsForLayer(layer, context.RegionSize)
                    .GetRegionCoords()
                    .ToList();

                var activeOverlappingRegions = overlappingRegionCoords
                    .Where(coord => context.CurrentlyActiveRegions.Contains(coord))
                    .ToList();

                // NEW: If no active regions, create a task to clear the layer's texture
                if (activeOverlappingRegions.Count == 0)
                {
                    System.Action onComplete = () => 
                    { 
                        if (GodotObject.IsInstanceValid(layer)) 
                            layer.Visualizer.Update(); 
                    };

                    // Create a clear task for this layer
                    var clearTask = CreateClearLayerTextureTask(layer, onComplete);
                    
                    if (clearTask != null)
                    {
                        context.TextureLayerMaskTasks[layer] = clearTask;
                        tasks[layer] = clearTask;
                        AsyncGpuTaskManager.Instance.AddTask(clearTask);
                    }
                    
                    continue;
                }

                var dependencies = new List<AsyncGpuTask>();
                Rid heightmapArrayRid = new Rid();
                Rid metadataBufferRid = new Rid();
                int activeRegionCount = 0;

                // If this layer needs height data for its mask generation
                if (layer.DoesAnyMaskRequireHeightData())
                {
                    var compositeDependencies = activeOverlappingRegions
                        .Where(rc => context.RegionHeightCompositeTasks.ContainsKey(rc))
                        .Select(rc => context.RegionHeightCompositeTasks[rc])
                        .ToList();

                    var (stagingTask, stagingResult) = HeightDataStager.StageHeightDataForLayerAsync(
                        layer, 
                        context.RegionMapManager, 
                        context.RegionSize, 
                        compositeDependencies);

                    if (stagingTask != null && stagingResult.IsValid)
                    {
                        dependencies.Add(stagingTask);
                        AsyncGpuTaskManager.Instance.AddTask(stagingTask);
                        heightmapArrayRid = stagingResult.HeightmapArrayRid;
                        metadataBufferRid = stagingResult.MetadataBufferRid;
                        activeRegionCount = stagingResult.ActiveRegionCount;
                    }
                    else
                    {
                        // Height data staging failed, clear the texture instead
                        System.Action onComplete = () => 
                        { 
                            if (GodotObject.IsInstanceValid(layer)) 
                                layer.Visualizer.Update(); 
                        };

                        var clearTask = CreateClearLayerTextureTask(layer, onComplete);
                        
                        if (clearTask != null)
                        {
                            context.TextureLayerMaskTasks[layer] = clearTask;
                            tasks[layer] = clearTask;
                            AsyncGpuTaskManager.Instance.AddTask(clearTask);
                        }
                        
                        continue;
                    }
                }

                System.Action normalOnComplete = () => 
                { 
                    if (GodotObject.IsInstanceValid(layer)) 
                        layer.Visualizer.Update(); 
                };

                var maskTask = LayerMaskPipeline.CreateUpdateLayerTextureTask(
                    layer.layerTextureRID, 
                    layer, 
                    layer.Size.X, 
                    layer.Size.Y,
                    heightmapArrayRid, 
                    metadataBufferRid, 
                    activeRegionCount, 
                    dependencies, 
                    normalOnComplete);

                if (maskTask != null)
                {
                    context.TextureLayerMaskTasks[layer] = maskTask;
                    tasks[layer] = maskTask;
                    AsyncGpuTaskManager.Instance.AddTask(maskTask);
                }
            }

            return tasks;
        }

        /// <summary>
        /// Creates a task to clear a layer's texture to black/transparent.
        /// Used when a layer moves to an area with no valid regions.
        /// </summary>
        private AsyncGpuTask CreateClearLayerTextureTask(TerrainLayerBase layer, System.Action onComplete)
        {
            if (!layer.layerTextureRID.IsValid)
                return null;

            var (clearCmd, clearRids, shaderPaths) = GpuKernels.CreateClearCommands(
                layer.layerTextureRID,
                Colors.Transparent, // Clear to transparent
                layer.Size.X,
                layer.Size.Y);

            if (clearCmd == null)
                return null;

            var owners = new List<object> { layer };

            return new AsyncGpuTask(
                clearCmd,
                onComplete,
                clearRids,
                owners,
                $"Clear texture layer '{layer.LayerName}'",
                null,
                shaderPaths);
        }
    }
}