using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using Terrain3DTools.Layers;
using Terrain3DTools.Utils;
using Terrain3DTools.Core;
using Terrain3DTools.Core.Debug;

namespace Terrain3DTools.Pipeline
{
    /// <summary>
    /// Phase 4: Composites texture layers into region control maps.
    /// Uses lazy (JIT) initialization.
    /// </summary>
    public class RegionTextureCompositePhase : IProcessingPhase
    {
        private const string DEBUG_CLASS_NAME = "RegionTextureCompositePhase";

        public RegionTextureCompositePhase()
        {
            DebugManager.Instance?.RegisterClass(DEBUG_CLASS_NAME);
        }

        public Dictionary<object, AsyncGpuTask> Execute(TerrainProcessingContext context)
        {
            var tasks = new Dictionary<object, AsyncGpuTask>();

            DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.PhaseExecution,
                $"Compositing texture data for {context.AllDirtyRegions.Count} region(s)");

            foreach (var regionCoords in context.AllDirtyRegions)
            {
                if (!context.CurrentlyActiveRegions.Contains(regionCoords))
                {
                    continue;
                }

                var tieredLayers = context.RegionDependencyManager.GetTieredLayersForRegion(regionCoords);

                if (tieredLayers == null || !tieredLayers.ShouldProcess())
                {
                    continue;
                }

                var allTextureLayers = tieredLayers.TextureLayers
                    .Where(l => GodotObject.IsInstanceValid(l))
                    .ToList();

                var currentRegionCoords = regionCoords;

                var dependencies = allTextureLayers
                    .Where(l => context.TextureLayerMaskTasks.ContainsKey(l))
                    .Select(l => context.TextureLayerMaskTasks[l])
                    .ToList();

                if (context.RegionHeightCompositeTasks.ContainsKey(regionCoords))
                {
                    dependencies.Add(context.RegionHeightCompositeTasks[regionCoords]);
                }

                Action onComplete = () => { };

                var regionData = context.RegionMapManager.GetOrCreateRegionData(regionCoords);

                var readSources = allTextureLayers
                    .Where(l => l.layerTextureRID.IsValid)
                    .Select(l => l.layerTextureRID)
                    .ToList();

                var task = CreateRegionControlCompositeTaskLazy(
                    currentRegionCoords,
                    allTextureLayers,
                    dependencies,
                    onComplete,
                    context);

                if (task != null)
                {
                    task.DeclareResources(
                        writes: new[] { regionData.ControlMap },
                        reads: readSources
                    );

                    context.RegionTextureCompositeTasks[currentRegionCoords] = task;
                    tasks[currentRegionCoords] = task;
                    AsyncGpuTaskManager.Instance.AddTask(task);
                }
            }

            return tasks;
        }

        private AsyncGpuTask CreateRegionControlCompositeTaskLazy(
            Vector2I regionCoords,
            List<TerrainLayerBase> textureLayers,
            List<AsyncGpuTask> dependencies,
            Action onComplete,
            TerrainProcessingContext context)
        {
            int regionSize = context.RegionSize;

            Func<(Action<long>, List<Rid>, List<string>)> generator = () =>
            {
                var regionData = context.RegionMapManager.GetOrCreateRegionData(regionCoords);
                if (!regionData.ControlMap.IsValid)
                {
                    return ((l) => { }, new List<Rid>(), new List<string>());
                }

                var allCommands = new List<Action<long>>();
                var allTempRids = new List<Rid>();
                var allShaderPaths = new List<string>();

                var (clearCmd, clearRids, clearShader) = GpuKernels.CreateClearCommands(
                    regionData.ControlMap,
                    Colors.Black,
                    regionSize,
                    regionSize,
                    DEBUG_CLASS_NAME);

                if (clearCmd != null)
                {
                    allCommands.Add(clearCmd);
                    allTempRids.AddRange(clearRids);
                    allShaderPaths.Add(clearShader);
                }

                if (textureLayers.Count > 0)
                {
                    var regionMin = TerrainCoordinateHelper.RegionMinWorld(regionCoords, regionSize);
                    var regionSizeWorld = new Vector2(regionSize, regionSize);

                    foreach (var layer in textureLayers)
                    {
                        if (!GodotObject.IsInstanceValid(layer)) continue;

                        var (applyCmd, applyRids, applyShaderPaths) = layer.CreateApplyRegionCommands(
                            regionCoords,
                            regionData,
                            regionSize,
                            regionMin,
                            regionSizeWorld);

                        if (applyCmd != null)
                        {
                            allCommands.Add(applyCmd);
                            allTempRids.AddRange(applyRids);
                            allShaderPaths.AddRange(applyShaderPaths);
                        }
                    }
                }

                if (allCommands.Count == 0)
                {
                    return ((l) => { }, new List<Rid>(), new List<string>());
                }

                Action<long> combinedCommands = (computeList) =>
                {
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

            var regionDataRef = context.RegionMapManager.GetOrCreateRegionData(regionCoords);
            var owners = new List<object> { regionDataRef };
            owners.AddRange(textureLayers);

            return new AsyncGpuTask(
                generator,
                onComplete,
                owners,
                $"Texture Composite: Region {regionCoords}",
                dependencies);
        }
    }
}