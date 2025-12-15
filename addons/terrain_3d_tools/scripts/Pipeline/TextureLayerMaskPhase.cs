// /Core/Pipeline/TextureLayerMaskPhase.cs
using Godot;
using System.Collections.Generic;
using System.Linq;
using Terrain3DTools.Layers;
using Terrain3DTools.Core;
using Terrain3DTools.Utils;
using Terrain3DTools.Core.Debug;

namespace Terrain3DTools.Pipeline
{
    /// <summary>
    /// Phase 3: Generates mask textures for all dirty texture layers.
    /// Texture layers control material blending and painting on the terrain surface.
    /// Some texture layer masks may require height data for slope-based or elevation-based
    /// masking operations, which requires staging heightmaps from composited regions.
    /// </summary>
    public class TextureLayerMaskPhase : IProcessingPhase
    {
        private const string DEBUG_CLASS_NAME = "TextureLayerMaskPhase";

        public TextureLayerMaskPhase()
        {
            DebugManager.Instance?.RegisterClass(DEBUG_CLASS_NAME);
        }

        public Dictionary<object, AsyncGpuTask> Execute(TerrainProcessingContext context)
        {
            var tasks = new Dictionary<object, AsyncGpuTask>();

            DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.PhaseExecution,
                $"Processing {context.DirtyTextureLayers.Count} texture layer(s)");

            foreach (var layer in context.DirtyTextureLayers)
            {
                if (!GodotObject.IsInstanceValid(layer)) continue;

                var overlappingRegionCoords = TerrainCoordinateHelper
                    .GetRegionBoundsForLayer(layer, context.RegionSize)
                    .GetRegionCoords()
                    .ToList();

                var activeOverlappingRegions = overlappingRegionCoords
                    .Where(coord => context.CurrentlyActiveRegions.Contains(coord))
                    .ToList();

                if (activeOverlappingRegions.Count == 0)
                {
                    DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.MaskGeneration,
                        $"Layer '{layer.LayerName}' has no active regions - creating clear task");

                    var clearTask = CreateClearLayerTextureTask(layer);
                    
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

                if (layer.DoesAnyMaskRequireHeightData())
                {
                    DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.MaskGeneration,
                        $"Layer '{layer.LayerName}' requires height data - staging from {activeOverlappingRegions.Count} regions");

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
                        DebugManager.Instance?.LogWarning(DEBUG_CLASS_NAME,
                            $"Failed to stage height data for layer '{layer.LayerName}' - creating clear task");

                        var clearTask = CreateClearLayerTextureTask(layer);
                        
                        if (clearTask != null)
                        {
                            context.TextureLayerMaskTasks[layer] = clearTask;
                            tasks[layer] = clearTask;
                            AsyncGpuTaskManager.Instance.AddTask(clearTask);
                        }
                        
                        continue;
                    }
                }

                System.Action onComplete = () => 
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
                    onComplete);

                if (maskTask != null)
                {
                    context.TextureLayerMaskTasks[layer] = maskTask;
                    tasks[layer] = maskTask;
                    AsyncGpuTaskManager.Instance.AddTask(maskTask);

                    DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.MaskGeneration,
                        $"Created mask task for layer '{layer.LayerName}'");
                }
            }

            return tasks;
        }

        /// <summary>
        /// Creates a task to clear a layer's texture to transparent.
        /// Used when a layer moves to an area with no valid regions or when height data staging fails.
        /// </summary>
        private AsyncGpuTask CreateClearLayerTextureTask(TerrainLayerBase layer)
        {
            if (!layer.layerTextureRID.IsValid)
                return null;

            System.Action onComplete = () => 
            { 
                if (GodotObject.IsInstanceValid(layer)) 
                    layer.Visualizer.Update(); 
            };

            var clearTask = GpuKernels.CreateClearTask(
                layer.layerTextureRID,
                Colors.Transparent,
                layer.Size.X,
                layer.Size.Y,
                null,
                onComplete,
                layer,
                $"Clear texture layer '{layer.LayerName}'");

            if (clearTask != null)
            {
                DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.MaskGeneration,
                    $"Created clear task for layer '{layer.LayerName}'");
            }

            return clearTask;
        }
    }
}