// /Pipeline/InstancerPlacementPhase.cs
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using Terrain3DTools.Layers;
using Terrain3DTools.Layers.Instancer;
using Terrain3DTools.Core;
using Terrain3DTools.Core.Debug;
using Terrain3DTools.Utils;
using Terrain3DTools.Settings;

namespace Terrain3DTools.Pipeline
{
    /// <summary>
    /// Phase 10: Generates instance transforms for all instancer layers.
    /// Runs after exclusion maps are written by other feature layers.
    /// Uses a single compute dispatch per layer/region with mesh selection in shader.
    /// </summary>
    public class InstancerPlacementPhase : IProcessingPhase
    {
        private const string DEBUG_CLASS_NAME = "InstancerPlacementPhase";

        public InstancerPlacementPhase()
        {
            DebugManager.Instance?.RegisterClass(DEBUG_CLASS_NAME);
        }

        public Dictionary<object, AsyncGpuTask> Execute(TerrainProcessingContext context)
        {
            var tasks = new Dictionary<object, AsyncGpuTask>();

            // Get all instancer layers (dirty or not - they may need to update due to terrain changes)
            var allInstancerLayers = context.DirtyFeatureLayers
                .OfType<InstancerLayer>()
                .Where(l => GodotObject.IsInstanceValid(l))
                .ToList();

            if (allInstancerLayers.Count == 0)
            {
                DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.PhaseExecution,
                    "No instancer layers to process");
                return tasks;
            }

            var settings = GlobalToolSettingsManager.Current;
            int maxInstancesPerRegion = settings?.MaxInstancesPerRegion ?? 16384;

            DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.PhaseExecution,
                $"Processing {allInstancerLayers.Count} instancer layer(s), max {maxInstancesPerRegion} instances/region");

            foreach (var regionCoords in context.AllDirtyRegions)
            {
                if (!context.CurrentlyActiveRegions.Contains(regionCoords)) continue;

                var regionData = context.RegionMapManager.GetRegionData(regionCoords);
                if (regionData == null) continue;

                // Find instancer layers that overlap this region
                foreach (var instancerLayer in allInstancerLayers)
                {
                    var layerRegions = TerrainCoordinateHelper
                        .GetRegionBoundsForLayer(instancerLayer, context.RegionSize)
                        .GetRegionCoords();

                    if (!layerRegions.Contains(regionCoords)) continue;

                    // Capture bake state on main thread
                    var bakeState = instancerLayer.GetActiveBakeState();
                    if (bakeState == null || bakeState.MeshEntries.Count == 0)
                    {
                        DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.PhaseExecution,
                            $"Skipping '{instancerLayer.LayerName}' - no valid mesh entries");
                        continue;
                    }

                    var task = CreatePlacementTask(
                        instancerLayer,
                        bakeState,
                        regionCoords,
                        regionData,
                        maxInstancesPerRegion,
                        context);

                    if (task != null)
                    {
                        var taskKey = (instancerLayer.GetInstanceId(), regionCoords);
                        tasks[taskKey] = task;

                        // Store in context for downstream tracking
                        if (!context.InstancerPlacementTasks.ContainsKey(instancerLayer.GetInstanceId()))
                        {
                            context.InstancerPlacementTasks[instancerLayer.GetInstanceId()] =
                                new Dictionary<Vector2I, AsyncGpuTask>();
                        }
                        context.InstancerPlacementTasks[instancerLayer.GetInstanceId()][regionCoords] = task;

                        // Register with manager for readback/push tracking
                        var buffer = regionData.GetOrCreateInstanceBuffer(
                            instancerLayer.GetInstanceId(), maxInstancesPerRegion);
                        context.LayerManager?.RegisterPendingInstanceBuffer(
                            instancerLayer.GetInstanceId(), regionCoords, buffer);

                        AsyncGpuTaskManager.Instance.AddTask(task);

                        DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.PhaseExecution,
                            $"Created placement task for '{instancerLayer.LayerName}' in region {regionCoords}");
                    }
                }
            }

            DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.PerformanceMetrics,
                $"Phase complete - {tasks.Count} placement tasks created");

            return tasks;
        }

        private AsyncGpuTask CreatePlacementTask(
            InstancerLayer layer,
            InstancerBakeState bakeState,
            Vector2I regionCoords,
            RegionData regionData,
            int maxInstances,
            TerrainProcessingContext context)
        {
            // Collect dependencies
            var dependencies = new List<AsyncGpuTask>();

            if (context.FeatureLayerMaskTasks.TryGetValue(layer, out var maskTask))
                dependencies.Add(maskTask);

            if (context.RegionHeightCompositeTasks.TryGetValue(regionCoords, out var heightTask))
                dependencies.Add(heightTask);

            if (context.RegionFeatureApplicationTasks.TryGetValue(regionCoords, out var featureAppTask))
                dependencies.Add(featureAppTask);

            if (context.ExclusionWriteTasks.TryGetValue(regionCoords, out var exclusionTask))
                dependencies.Add(exclusionTask);

            int regionSize = context.RegionSize;
            float worldHeightScale = context.WorldHeightScale;
            var layerInstanceId = bakeState.LayerInstanceId;

            // GET OR CREATE THE BUFFER NOW (on main thread, before generator)
            var instanceBuffer = regionData.GetOrCreateInstanceBuffer(layerInstanceId, maxInstances);

            // RESET THE COUNTER NOW (before compute list creation)
            instanceBuffer.ResetCounter();

            // Ensure exclusion map exists NOW (before generator)
            var exclusionMap = regionData.GetOrCreateExclusionMap(regionSize);

            Func<(Action<long>, List<Rid>, List<string>)> generator = () =>
            {
                // DO NOT call ResetCounter() or GetOrCreateInstanceBuffer() here!
                // Just use the already-prepared resources

                return CreatePlacementCommands(
                    instanceBuffer,
                    bakeState,
                    regionData.HeightMap,
                    exclusionMap,
                    regionCoords,
                    regionSize,
                    worldHeightScale,
                    maxInstances);
            };

            var owners = new List<object> { layer, regionData };

            return new AsyncGpuTask(
                generator,
                null,
                owners,
                $"Instancer placement: {layer.LayerName} @ {regionCoords}",
                dependencies);
        }


        private (Action<long>, List<Rid>, List<string>) CreatePlacementCommands(
            InstanceBuffer instanceBuffer,
            InstancerBakeState state,
            Rid heightMap,
            Rid exclusionMap,
            Vector2I regionCoords,
            int regionSize,
            float worldHeightScale,
            int maxInstances)
        {
            // State and weight diagnostics
            DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.PhaseExecution,
                $"=== MESH ENTRY DEBUG ===");
            DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.PhaseExecution,
                $"Total entries: {state.MeshEntries.Count}, TotalWeight: {state.TotalProbabilityWeight}");

            float cumulative = 0f;
            for (int i = 0; i < state.MeshEntries.Count; i++)
            {
                var entry = state.MeshEntries[i];
                cumulative += entry.ProbabilityWeight / state.TotalProbabilityWeight;
                DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.PhaseExecution,
                    $"  [{i}] MeshAssetId={entry.MeshAssetId}, Weight={entry.ProbabilityWeight}, Cumulative={cumulative:F4}");
            }
            DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.PhaseExecution,
                $"=== END MESH ENTRY DEBUG ===");

            if (!state.DensityMaskRid.IsValid || !heightMap.IsValid || state.MeshEntries.Count == 0)
            {
                DebugManager.Instance?.LogWarning(DEBUG_CLASS_NAME,
                    $"Invalid resources for placement: mask={state.DensityMaskRid.IsValid}, height={heightMap.IsValid}, meshes={state.MeshEntries.Count}");
                return ((l) => { }, new List<Rid>(), new List<string>());
            }
            // End of initial diagnostics

            if (!state.DensityMaskRid.IsValid || !heightMap.IsValid || state.MeshEntries.Count == 0)
            {
                DebugManager.Instance?.LogWarning(DEBUG_CLASS_NAME,
                    $"Invalid resources for placement: mask={state.DensityMaskRid.IsValid}, height={heightMap.IsValid}, meshes={state.MeshEntries.Count}");
                return ((l) => { }, new List<Rid>(), new List<string>());
            }

            var shaderPath = "res://addons/terrain_3d_tools/Shaders/Layers/InstancerPlacement.glsl";
            var operation = new AsyncComputeOperation(shaderPath);

            // Bind input textures as samplers
            operation.BindSamplerWithTexture(0, state.DensityMaskRid);
            operation.BindSamplerWithTexture(1, heightMap);
            operation.BindSamplerWithTexture(2, exclusionMap);

            // Bind output buffers
            operation.BindStorageBuffer(3, instanceBuffer.TransformBuffer);
            operation.BindStorageBuffer(4, instanceBuffer.CountBuffer);

            // Create and bind mesh entry buffer
            var meshEntryData = CreateMeshEntryBuffer(state.MeshEntries, state.TotalProbabilityWeight);
            operation.BindTemporaryStorageBuffer(5, meshEntryData);

            // Calculate region world position
            var regionMin = TerrainCoordinateHelper.RegionMinWorld(regionCoords, regionSize);
            var regionWorldSize = new Vector2(regionSize, regionSize);
            var maskWorldSize = state.MaskWorldMax - state.MaskWorldMin;

            // Calculate cell dimensions
            float cellSize = state.MinimumSpacing;
            int cellsX = Mathf.CeilToInt(regionSize / cellSize);
            int cellsY = Mathf.CeilToInt(regionSize / cellSize);

            // Build push constants
            var pushConstants = GpuUtils.CreatePushConstants()
                .Add(regionMin)
                .Add(regionWorldSize)
                .Add(state.MaskWorldMin)
                .Add(maskWorldSize)
                .Add(worldHeightScale)
                .Add(state.BaseDensity)
                .Add(cellSize)
                .Add(state.ExclusionThreshold)
                .Add((uint)state.Seed)
                .Add((uint)maxInstances)
                .Add(regionSize)
                .Add(state.MeshEntries.Count)
                .Add(cellsX)
                .Add(cellsY)
                .AddPadding(8)
                .Build();

            operation.SetPushConstants(pushConstants);

            // Dispatch: one workgroup per 8x8 cells
            uint groupsX = (uint)((cellsX + 7) / 8);
            uint groupsY = (uint)((cellsY + 7) / 8);

            DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.PhaseExecution,
                $"Dispatch: {groupsX}x{groupsY} groups, {cellsX}x{cellsY} cells, cellSize={cellSize}");

            // Get the base dispatch commands
            var dispatchCommands = operation.CreateDispatchCommands(groupsX, groupsY);

            // Wrap with barrier before and after for buffer access safety
            Action<long> commandsWithBarriers = (computeList) =>
            {
                // Barrier before to ensure input textures are ready
                Gpu.Rd.ComputeListAddBarrier(computeList);

                // Execute the placement dispatch
                dispatchCommands?.Invoke(computeList);

                // Barrier after to ensure buffer writes are complete before any readback
                Gpu.Rd.ComputeListAddBarrier(computeList);
            };

            return (
                commandsWithBarriers,
                operation.GetTemporaryRids(),
                new List<string> { shaderPath });
        }

        /// <summary>
        /// Creates the mesh entry buffer for GPU.
        /// Each entry: meshId(uint), cumulativeWeight, minScale, maxScale, 
        ///             yRotRange, alignNormal(uint), normalStrength, heightOffset
        /// = 8 values per entry, stored as floats with uint bit-casting where needed.
        /// </summary>
        private byte[] CreateMeshEntryBuffer(List<InstancerBakeState.MeshEntrySnapshot> entries, float totalWeight)
        {
            // 8 floats per entry
            var data = new float[entries.Count * 8];
            float cumulative = 0f;

            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                cumulative += e.ProbabilityWeight / totalWeight;

                int offset = i * 8;

                // Store mesh ID as uint bits in float
                byte[] meshIdBytes = BitConverter.GetBytes((uint)e.MeshAssetId);
                data[offset + 0] = BitConverter.ToSingle(meshIdBytes, 0);

                data[offset + 1] = cumulative;
                data[offset + 2] = e.MinScale;
                data[offset + 3] = e.MaxScale;
                data[offset + 4] = e.YRotationRangeRadians;

                // Store align flag as uint bits
                byte[] alignBytes = BitConverter.GetBytes(e.AlignToNormal ? 1u : 0u);
                data[offset + 5] = BitConverter.ToSingle(alignBytes, 0);

                data[offset + 6] = e.NormalAlignmentStrength;
                data[offset + 7] = e.HeightOffset;
            }

            return GpuUtils.FloatArrayToBytes(data);
        }
    }
}