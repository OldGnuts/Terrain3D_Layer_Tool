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
    /// Runs after exclusion maps are written and manual edits are applied.
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

                foreach (var instancerLayer in allInstancerLayers)
                {
                    var layerRegions = TerrainCoordinateHelper
                        .GetRegionBoundsForLayer(instancerLayer, context.RegionSize)
                        .GetRegionCoords();

                    if (!layerRegions.Contains(regionCoords)) continue;

                    var bakeState = instancerLayer.GetActiveBakeState();
                    if (bakeState == null || bakeState.MeshEntries.Count == 0)
                    {
                        DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.PhaseExecution,
                            $"Skipping '{instancerLayer.LayerName}' - no valid mesh entries");
                        continue;
                    }

                    // Prepare instance buffer before task creation
                    var layerInstanceId = bakeState.LayerInstanceId;
                    var instanceBuffer = regionData.GetOrCreateInstanceBuffer(layerInstanceId, maxInstancesPerRegion);
                    instanceBuffer.ResetCounter();

                    // Ensure exclusion map exists
                    var exclusionMap = regionData.GetOrCreateExclusionMap(context.RegionSize);

                    // Build dependencies
                    var dependencies = BuildDependencies(instancerLayer, regionCoords, context);

                    // Collect read sources
                    var readSources = new List<Rid>();
                    if (bakeState.DensityMaskRid.IsValid)
                        readSources.Add(bakeState.DensityMaskRid);
                    if (regionData.HeightMap.IsValid)
                        readSources.Add(regionData.HeightMap);
                    if (exclusionMap.IsValid)
                        readSources.Add(exclusionMap);

                    // Collect write targets (instance buffers)
                    var writeTargets = new List<Rid>();
                    if (instanceBuffer.TransformBuffer.IsValid)
                        writeTargets.Add(instanceBuffer.TransformBuffer);
                    if (instanceBuffer.CountBuffer.IsValid)
                        writeTargets.Add(instanceBuffer.CountBuffer);

                    var task = CreatePlacementTask(
                        instancerLayer,
                        bakeState,
                        instanceBuffer,
                        exclusionMap,
                        regionCoords,
                        regionData,
                        maxInstancesPerRegion,
                        context);

                    if (task != null)
                    {
                        task.DeclareResources(
                            writes: writeTargets,
                            reads: readSources
                        );

                        var taskKey = (instancerLayer.GetInstanceId(), regionCoords);
                        tasks[taskKey] = task;

                        if (!context.InstancerPlacementTasks.ContainsKey(instancerLayer.GetInstanceId()))
                        {
                            context.InstancerPlacementTasks[instancerLayer.GetInstanceId()] =
                                new Dictionary<Vector2I, AsyncGpuTask>();
                        }
                        context.InstancerPlacementTasks[instancerLayer.GetInstanceId()][regionCoords] = task;

                        context.LayerManager?.RegisterPendingInstanceBuffer(
                            instancerLayer.GetInstanceId(), regionCoords, instanceBuffer);

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

        private List<AsyncGpuTask> BuildDependencies(
            InstancerLayer layer,
            Vector2I regionCoords,
            TerrainProcessingContext context)
        {
            var dependencies = new List<AsyncGpuTask>();

            if (context.FeatureLayerMaskTasks.TryGetValue(layer, out var maskTask))
                dependencies.Add(maskTask);

            if (context.RegionHeightCompositeTasks.TryGetValue(regionCoords, out var heightTask))
                dependencies.Add(heightTask);

            if (context.RegionFeatureApplicationTasks.TryGetValue(regionCoords, out var featureAppTask))
                dependencies.Add(featureAppTask);

            if (context.ExclusionWriteTasks.TryGetValue(regionCoords, out var exclusionTask))
                dependencies.Add(exclusionTask);

            // Depend on manual edit application (critical for exclusion data)
            if (context.ManualEditApplicationTasks.TryGetValue(regionCoords, out var manualEditTask))
                dependencies.Add(manualEditTask);

            return dependencies;
        }

        private AsyncGpuTask CreatePlacementTask(
            InstancerLayer layer,
            InstancerBakeState bakeState,
            InstanceBuffer instanceBuffer,
            Rid exclusionMap,
            Vector2I regionCoords,
            RegionData regionData,
            int maxInstances,
            TerrainProcessingContext context)
        {
            int regionSize = context.RegionSize;
            float worldHeightScale = context.WorldHeightScale;

            Func<(Action<long>, List<Rid>, List<string>)> generator = () =>
            {
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
                $"Instancer Placement: {layer.LayerName} @ {regionCoords}",
                BuildDependencies(layer, regionCoords, context));
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
            DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.PhaseExecution,
                $"Creating placement commands: {state.MeshEntries.Count} mesh entries, TotalWeight: {state.TotalProbabilityWeight}");

            if (!state.DensityMaskRid.IsValid || !heightMap.IsValid || state.MeshEntries.Count == 0)
            {
                DebugManager.Instance?.LogWarning(DEBUG_CLASS_NAME,
                    $"Invalid resources for placement: mask={state.DensityMaskRid.IsValid}, " +
                    $"height={heightMap.IsValid}, meshes={state.MeshEntries.Count}");
                return ((l) => { }, new List<Rid>(), new List<string>());
            }

            var shaderPath = "res://addons/terrain_3d_tools/Shaders/Layers/InstancerPlacement.glsl";
            var operation = new AsyncComputeOperation(shaderPath);

            operation.BindSamplerWithTexture(0, state.DensityMaskRid);
            operation.BindSamplerWithTexture(1, heightMap);
            operation.BindSamplerWithTexture(2, exclusionMap);

            operation.BindStorageBuffer(3, instanceBuffer.TransformBuffer);
            operation.BindStorageBuffer(4, instanceBuffer.CountBuffer);

            var meshEntryData = CreateMeshEntryBuffer(state.MeshEntries, state.TotalProbabilityWeight);
            operation.BindTemporaryStorageBuffer(5, meshEntryData);

            var regionMin = TerrainCoordinateHelper.RegionMinWorld(regionCoords, regionSize);
            var regionWorldSize = new Vector2(regionSize, regionSize);
            var maskWorldSize = state.MaskWorldMax - state.MaskWorldMin;

            float cellSize = state.MinimumSpacing;
            int cellsX = Mathf.CeilToInt(regionSize / cellSize);
            int cellsY = Mathf.CeilToInt(regionSize / cellSize);

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

            uint groupsX = (uint)((cellsX + 7) / 8);
            uint groupsY = (uint)((cellsY + 7) / 8);

            DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.PhaseExecution,
                $"Dispatch: {groupsX}x{groupsY} groups, {cellsX}x{cellsY} cells, cellSize={cellSize}");

            var dispatchCommands = operation.CreateDispatchCommands(groupsX, groupsY);

            Action<long> commandsWithBarriers = (computeList) =>
            {
                dispatchCommands?.Invoke(computeList);
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
        /// </summary>
        private byte[] CreateMeshEntryBuffer(List<InstancerBakeState.MeshEntrySnapshot> entries, float totalWeight)
        {
            var data = new float[entries.Count * 8];
            float cumulative = 0f;

            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                cumulative += e.ProbabilityWeight / totalWeight;

                int offset = i * 8;

                byte[] meshIdBytes = BitConverter.GetBytes((uint)e.MeshAssetId);
                data[offset + 0] = BitConverter.ToSingle(meshIdBytes, 0);

                data[offset + 1] = cumulative;
                data[offset + 2] = e.MinScale;
                data[offset + 3] = e.MaxScale;
                data[offset + 4] = e.YRotationRangeRadians;

                byte[] alignBytes = BitConverter.GetBytes(e.AlignToNormal ? 1u : 0u);
                data[offset + 5] = BitConverter.ToSingle(alignBytes, 0);

                data[offset + 6] = e.NormalAlignmentStrength;
                data[offset + 7] = e.HeightOffset;
            }

            return GpuUtils.FloatArrayToBytes(data);
        }
    }
}