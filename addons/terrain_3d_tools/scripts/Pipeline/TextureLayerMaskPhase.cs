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
    /// Phase 3: Generates mask textures for all dirty texture layers.
    /// <para>
    /// <b>Fix:</b> Strictly enforces cleanup order. 
    /// UniformSets (Dependents) are freed before TextureArrays/Buffers (Owners).
    /// </para>
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

                // Case 1: No active regions -> Clear
                if (activeOverlappingRegions.Count == 0)
                {
                    var clearTask = CreateClearLayerTextureTaskLazy(layer);
                    if (clearTask != null)
                    {
                        context.TextureLayerMaskTasks[layer] = clearTask;
                        tasks[layer] = clearTask;
                        AsyncGpuTaskManager.Instance.AddTask(clearTask);
                    }
                    continue;
                }

                // Case 2: Process Layer
                var dependencies = new List<AsyncGpuTask>();
                if (layer.DoesAnyMaskRequireHeightData())
                {
                    var compositeDependencies = activeOverlappingRegions
                        .Where(rc => context.RegionHeightCompositeTasks.ContainsKey(rc))
                        .Select(rc => context.RegionHeightCompositeTasks[rc])
                        .ToList();
                    dependencies.AddRange(compositeDependencies);
                }

                Action onComplete = () => 
                { 
                    if (GodotObject.IsInstanceValid(layer)) 
                        layer.Visualizer?.Update(); 
                };

                var maskTask = CreateTextureMaskTaskLazy(
                    layer,
                    context.RegionMapManager,
                    context.RegionSize,
                    activeOverlappingRegions.Count, 
                    dependencies,
                    onComplete);

                if (maskTask != null)
                {
                    context.TextureLayerMaskTasks[layer] = maskTask;
                    tasks[layer] = maskTask;
                    AsyncGpuTaskManager.Instance.AddTask(maskTask);
                }
            }

            return tasks;
        }

        private AsyncGpuTask CreateTextureMaskTaskLazy(
            TerrainLayerBase layer,
            RegionMapManager regionMapManager,
            int regionSize,
            int activeRegionCount,
            List<AsyncGpuTask> dependencies,
            Action onComplete)
        {
            string layerName = layer.LayerName;
            int width = layer.Size.X;
            int height = layer.Size.Y;
            
            Func<(Action<long>, List<Rid>, List<string>)> generator = () =>
            {
                if (!GodotObject.IsInstanceValid(layer)) return ((l) => { }, new List<Rid>(), new List<string>());

                var allCommands = new List<Action<long>>();
                
                // 1. Operation RIDs (UniformSets, Temp Buffers)
                // We use a HashSet here to deduplicate shared UniformSets from sub-tasks.
                var operationRids = new HashSet<Rid>();
                var allShaderPaths = new List<string>();

                // 2. Owner RIDs (Must be freed LAST)
                Rid heightmapArrayRid = new Rid();
                Rid metadataBufferRid = new Rid();
                int stagedRegionCount = 0;

                // --- STAGING ---
                if (layer.DoesAnyMaskRequireHeightData())
                {
                    var (stagingTask, stagingResult) = HeightDataStager.StageHeightDataForLayerAsync(
                        layer, regionMapManager, regionSize, null);

                    if (stagingTask != null && stagingResult.IsValid)
                    {
                        allCommands.Add(stagingTask.GpuCommands);
                        
                        // Extract Staging Internals (UniformSets for Copy commands)
                        var (stagedRids, stagedPaths) = stagingTask.ExtractResourcesForCleanup();
                        foreach (var rid in stagedRids) operationRids.Add(rid);
                        allShaderPaths.AddRange(stagedPaths);
                        
                        // Capture Owners
                        heightmapArrayRid = stagingResult.HeightmapArrayRid;
                        metadataBufferRid = stagingResult.MetadataBufferRid;
                        stagedRegionCount = stagingResult.ActiveRegionCount;
                    }
                }

                // --- PIPELINE ---
                var pipelineTask = LayerMaskPipeline.CreateUpdateLayerTextureTask(
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
                        
                        // Extract Pipeline Internals (UniformSets for Masks/Stitch/Falloff)
                        var (pipeRids, pipePaths) = pipelineTask.ExtractResourcesForCleanup();
                        foreach (var rid in pipeRids) operationRids.Add(rid);
                        allShaderPaths.AddRange(pipePaths);
                    }
                }

                // --- ASSEMBLY & ORDERING ---
                Action<long> combined = (computeList) =>
                {
                    for (int i = 0; i < allCommands.Count; i++)
                    {
                        allCommands[i]?.Invoke(computeList);
                        if (i < allCommands.Count - 1) Gpu.Rd.ComputeListAddBarrier(computeList);
                    }
                };

                // CRITICAL FIX: Convert Operation RIDs to List FIRST
                var finalCleanupList = operationRids.ToList();

                // CRITICAL FIX: Add Owners LAST.
                // This guarantees [UniformSets] -> [Texture/Buffer] destruction order.
                if (heightmapArrayRid.IsValid) finalCleanupList.Add(heightmapArrayRid);
                if (metadataBufferRid.IsValid) finalCleanupList.Add(metadataBufferRid);

                return (combined, finalCleanupList, allShaderPaths);
            };

            var owners = new List<object> { layer };
            return new AsyncGpuTask(
                generator,
                onComplete,
                owners,
                $"Texture Mask: {layerName} (Lazy)",
                dependencies);
        }

        private AsyncGpuTask CreateClearLayerTextureTaskLazy(TerrainLayerBase layer)
        {
            if (!layer.layerTextureRID.IsValid) return null;

            Action onComplete = () => 
            { 
                if (GodotObject.IsInstanceValid(layer)) 
                    layer.Visualizer?.Update(); 
            };

            Rid target = layer.layerTextureRID;
            int w = layer.Size.X;
            int h = layer.Size.Y;

            Func<(Action<long>, List<Rid>, List<string>)> generator = () =>
            {
                var (cmd, rids, shader) = GpuKernels.CreateClearCommands(
                    target, Colors.Transparent, w, h, DEBUG_CLASS_NAME);
                
                return (cmd, rids, new List<string> { shader });
            };

            return new AsyncGpuTask(
                generator,
                onComplete,
                new List<object> { layer },
                $"Clear Texture: {layer.LayerName} (Lazy)",
                null);
        }
    }
}