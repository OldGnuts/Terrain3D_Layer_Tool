// /Core/Pipeline/RegionTextureCompositePhase.cs
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
    /// Phase 4: Composites texture layers into region control maps.
    /// Takes all texture layer masks that affect each region and blends them to create
    /// the final material/texture distribution data. Control maps determine which textures
    /// appear where and how they blend together.
    /// Depends on: TextureLayerMaskPhase, RegionHeightCompositePhase (for some texture operations)
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

                Action onComplete = () =>
                {
                    context.RegionDependencyManager.MarkRegionUpdated(currentRegionCoords);
                };

                var task = CreateRegionControlCompositeTask(
                    currentRegionCoords,
                    allTextureLayers,
                    dependencies,
                    onComplete,
                    context);

                if (task != null)
                {
                    context.RegionTextureCompositeTasks[currentRegionCoords] = task;
                    tasks[currentRegionCoords] = task;
                    AsyncGpuTaskManager.Instance.AddTask(task);

                    DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.RegionCompositing,
                        $"Created composite task for region {currentRegionCoords} with {allTextureLayers.Count} layer(s)");
                }
            }

            return tasks;
        }

        /// <summary>
        /// Creates a GPU task to composite all texture layers into a single region control map.
        /// First clears the control map, then applies each texture layer's blending operation.
        /// </summary>
        private AsyncGpuTask CreateRegionControlCompositeTask(
            Vector2I regionCoords,
            List<TerrainLayerBase> textureLayers,
            List<AsyncGpuTask> dependencies,
            Action onComplete,
            TerrainProcessingContext context)
        {
            var regionData = context.RegionMapManager.GetOrCreateRegionData(regionCoords);
            if (!regionData.ControlMap.IsValid)
            {
                DebugManager.Instance?.LogWarning(DEBUG_CLASS_NAME,
                    $"Region {regionCoords} has invalid control map");
                return null;
            }

            var allCommands = new List<Action<long>>();
            var allTempRids = new List<Rid>();
            var allShaderPaths = new List<string>();

            var (clearCmd, clearRids, clearShader) = GpuKernels.CreateClearCommands(
                regionData.ControlMap,
                Colors.Black,
                context.RegionSize,
                context.RegionSize,
                DEBUG_CLASS_NAME);

            if (clearCmd != null)
            {
                allCommands.Add(clearCmd);
                allTempRids.AddRange(clearRids);
                allShaderPaths.Add(clearShader);
            }

            if (textureLayers.Count > 0)
            {
                var regionMin = TerrainCoordinateHelper.RegionMinWorld(regionCoords, context.RegionSize);
                var regionSizeWorld = new Vector2(context.RegionSize, context.RegionSize);

                foreach (var layer in textureLayers)
                {
                    var (applyCmd, applyRids, applyShaderPaths) = layer.CreateApplyRegionCommands(
                        regionCoords,
                        regionData,
                        context.RegionSize,
                        regionMin,
                        regionSizeWorld);

                    if (applyCmd != null)
                    {
                        allCommands.Add(applyCmd);
                        allTempRids.AddRange(applyRids);
                        allShaderPaths.AddRange(applyShaderPaths);
                    }
                }

                DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.RegionCompositing,
                    $"Region {regionCoords} - Applied {textureLayers.Count} texture layer(s)");
            }

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
            owners.AddRange(textureLayers);

            return new AsyncGpuTask(
                combinedCommands,
                onComplete,
                allTempRids,
                owners,
                $"Region {regionCoords} texture composite",
                dependencies,
                allShaderPaths);
        }
    }
}