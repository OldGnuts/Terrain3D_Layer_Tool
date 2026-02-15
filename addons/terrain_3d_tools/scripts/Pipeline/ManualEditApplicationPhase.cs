// /Pipeline/ManualEditApplicationPhase.cs

using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using Terrain3DTools.Core;
using Terrain3DTools.Core.Debug;
using Terrain3DTools.Layers;
using Terrain3DTools.Layers.ManualEdit;
using Terrain3DTools.Utils;

namespace Terrain3DTools.Pipeline
{
    /// <summary>
    /// Phase 9: Applies manual edits to region height and texture data.
    /// Runs after blend smoothing and before instancer placement.
    /// Manual edits are applied on top of composited/processed data.
    /// </summary>
    public class ManualEditApplicationPhase : IProcessingPhase
    {
        private const string DEBUG_CLASS_NAME = "ManualEditApplicationPhase";

        public ManualEditApplicationPhase()
        {
            DebugManager.Instance?.RegisterClass(DEBUG_CLASS_NAME);
        }

        public Dictionary<object, AsyncGpuTask> Execute(TerrainProcessingContext context)
        {
            var tasks = new Dictionary<object, AsyncGpuTask>();

            var manualEditLayers = FindManualEditLayers(context);

            if (manualEditLayers.Count == 0)
            {
                DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.PhaseExecution,
                    "No ManualEditLayers found - skipping phase");
                return tasks;
            }

            foreach (var layer in manualEditLayers)
            {
                layer.SetRegionSize(context.RegionSize);
            }

            DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.PhaseExecution,
                $"Processing {manualEditLayers.Count} ManualEditLayer(s) for {context.AllDirtyRegions.Count} dirty region(s)");

            foreach (var regionCoords in context.AllDirtyRegions)
            {
                if (!context.CurrentlyActiveRegions.Contains(regionCoords))
                    continue;

                var layersWithEdits = manualEditLayers
                    .Where(l => l.HasEditsInRegion(regionCoords))
                    .ToList();

                if (layersWithEdits.Count == 0)
                    continue;

                DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.PhaseExecution,
                    $"Region {regionCoords} has edits from {layersWithEdits.Count} layer(s)");

                var dependencies = BuildDependencies(regionCoords, context);

                var regionData = context.RegionMapManager.GetRegionData(regionCoords);
                if (regionData == null) continue;

                // Collect write targets
                var writeTargets = new List<Rid>();
                if (regionData.HeightMap.IsValid)
                    writeTargets.Add(regionData.HeightMap);
                if (regionData.ControlMap.IsValid)
                    writeTargets.Add(regionData.ControlMap);

                // Check if any layer modifies exclusion
                bool modifiesExclusion = layersWithEdits.Any(l => l.InstanceEditingEnabled);
                if (modifiesExclusion)
                {
                    var exclusionMap = regionData.GetOrCreateExclusionMap(context.RegionSize);
                    if (exclusionMap.IsValid)
                        writeTargets.Add(exclusionMap);
                }

                // Collect read sources from edit buffers
                var readSources = new List<Rid>();
                foreach (var layer in layersWithEdits)
                {
                    foreach (var rid in layer.GetEditBufferReadSources(regionCoords))
                    {
                        if (rid.IsValid)
                            readSources.Add(rid);
                    }
                }

                var task = CreateManualEditApplicationTaskLazy(
                    regionCoords,
                    layersWithEdits,
                    dependencies,
                    context);

                if (task != null)
                {
                    task.DeclareResources(
                        writes: writeTargets,
                        reads: readSources
                    );

                    tasks[regionCoords] = task;
                    context.ManualEditApplicationTasks[regionCoords] = task;
                    AsyncGpuTaskManager.Instance.AddTask(task);

                    DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.PhaseExecution,
                        $"Created manual edit task for region {regionCoords}");
                }
            }

            DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.PerformanceMetrics,
                $"Phase complete - {tasks.Count} manual edit tasks created");

            return tasks;
        }

        private List<ManualEditLayer> FindManualEditLayers(TerrainProcessingContext context)
        {
            var result = new List<ManualEditLayer>();
            var allRegions = context.CurrentlyActiveRegions;
            var seenLayerIds = new HashSet<ulong>();

            foreach (var regionCoords in allRegions)
            {
                var tieredLayers = context.RegionDependencyManager.GetTieredLayersForRegion(regionCoords);
                if (tieredLayers == null) continue;

                foreach (var layer in tieredLayers.FeatureLayers)
                {
                    if (layer is ManualEditLayer mel &&
                        GodotObject.IsInstanceValid(mel) &&
                        seenLayerIds.Add(mel.GetInstanceId()))
                    {
                        result.Add(mel);
                    }
                }
            }

            foreach (var layer in context.DirtyFeatureLayers)
            {
                if (layer is ManualEditLayer mel &&
                    GodotObject.IsInstanceValid(mel) &&
                    seenLayerIds.Add(mel.GetInstanceId()))
                {
                    result.Add(mel);
                }
            }

            return result;
        }

        private List<AsyncGpuTask> BuildDependencies(
            Vector2I regionCoords,
            TerrainProcessingContext context)
        {
            var dependencies = new List<AsyncGpuTask>();

            if (context.RegionHeightCompositeTasks.TryGetValue(regionCoords, out var heightTask))
                dependencies.Add(heightTask);

            if (context.RegionTextureCompositeTasks.TryGetValue(regionCoords, out var textureTask))
                dependencies.Add(textureTask);

            if (context.RegionFeatureApplicationTasks.TryGetValue(regionCoords, out var featureTask))
                dependencies.Add(featureTask);

            if (context.BlendSmoothingTasks.TryGetValue(regionCoords, out var smoothTask))
                dependencies.Add(smoothTask);

            // Also depend on exclusion write if present
            if (context.ExclusionWriteTasks.TryGetValue(regionCoords, out var exclusionTask))
                dependencies.Add(exclusionTask);

            return dependencies;
        }

        private AsyncGpuTask CreateManualEditApplicationTaskLazy(
            Vector2I regionCoords,
            List<ManualEditLayer> layersWithEdits,
            List<AsyncGpuTask> dependencies,
            TerrainProcessingContext context)
        {
            int regionSize = context.RegionSize;

            var layerBufferPairs = new List<(ManualEditLayer layer, ManualEditBuffer buffer)>();
            foreach (var layer in layersWithEdits)
            {
                var buffer = layer.GetEditBuffer(regionCoords);
                if (buffer != null && buffer.HasAllocatedResources)
                {
                    layerBufferPairs.Add((layer, buffer));
                }
            }

            if (layerBufferPairs.Count == 0)
            {
                return null;
            }

            Func<(Action<long>, List<Rid>, List<string>)> generator = () =>
            {
                var regionData = context.RegionMapManager.GetRegionData(regionCoords);
                if (regionData == null)
                {
                    DebugManager.Instance?.LogWarning(DEBUG_CLASS_NAME,
                        $"No region data for {regionCoords}");
                    return ((l) => { }, new List<Rid>(), new List<string>());
                }

                var allCommands = new List<Action<long>>();
                var allTempRids = new List<Rid>();
                var allShaderPaths = new List<string>();

                foreach (var (layer, buffer) in layerBufferPairs)
                {
                    if (!GodotObject.IsInstanceValid(layer))
                        continue;

                    if (layer.HeightEditingEnabled && buffer.HeightDelta.IsValid)
                    {
                        var (heightCmd, heightRids, heightShaders) = CreateHeightEditCommands(
                            regionCoords, regionData, buffer, regionSize);

                        if (heightCmd != null)
                        {
                            allCommands.Add(heightCmd);
                            allTempRids.AddRange(heightRids);
                            allShaderPaths.AddRange(heightShaders);
                        }
                    }

                    if (layer.TextureEditingEnabled && buffer.TextureEdit.IsValid)
                    {
                        var (textureCmd, textureRids, textureShaders) = CreateTextureEditCommands(
                            regionCoords, regionData, buffer, regionSize);

                        if (textureCmd != null)
                        {
                            allCommands.Add(textureCmd);
                            allTempRids.AddRange(textureRids);
                            allShaderPaths.AddRange(textureShaders);
                        }
                    }

                    if (layer.InstanceEditingEnabled && buffer.InstanceExclusion.IsValid)
                    {
                        var (exclusionCmd, exclusionRids, exclusionShaders) = CreateInstanceExclusionCommands(
                            regionCoords, regionData, buffer, regionSize);

                        if (exclusionCmd != null)
                        {
                            allCommands.Add(exclusionCmd);
                            allTempRids.AddRange(exclusionRids);
                            allShaderPaths.AddRange(exclusionShaders);
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

                DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.GpuDispatches,
                    $"Generated {allCommands.Count} command(s) for region {regionCoords}");

                return (combinedCommands, allTempRids, allShaderPaths);
            };

            Action onComplete = () =>
            {
                context.RegionMapManager.RefreshRegionPreview(regionCoords);

                DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.PhaseExecution,
                    $"Manual edit application complete for region {regionCoords}");
            };

            var regionDataRef = context.RegionMapManager.GetRegionData(regionCoords);
            var owners = new List<object> { regionDataRef };
            owners.AddRange(layersWithEdits);

            return new AsyncGpuTask(
                generator,
                onComplete,
                owners,
                $"Manual Edit: Region {regionCoords}",
                dependencies);
        }

        #region GPU Command Generators

        private (Action<long> commands, List<Rid> tempRids, List<string> shaders) CreateHeightEditCommands(
            Vector2I regionCoords,
            RegionData regionData,
            ManualEditBuffer editBuffer,
            int regionSize)
        {
            const string SHADER_PATH = "res://addons/terrain_3d_tools/Shaders/ManualEdit/apply_height_edit.glsl";

            var tempRids = new List<Rid>();
            var shaderPaths = new List<string> { SHADER_PATH };

            var op = new AsyncComputeOperation(SHADER_PATH);
            if (!op.IsValid())
            {
                DebugManager.Instance?.LogError(DEBUG_CLASS_NAME,
                    "Failed to create height edit compute operation");
                return (null, tempRids, new List<string>());
            }

            op.BindStorageImage(0, regionData.HeightMap);
            op.BindStorageImage(1, editBuffer.HeightDelta);

            var pushConstants = GpuUtils.CreatePushConstants()
                .Add(regionSize)
                .AddPadding(12)
                .Build();
            op.SetPushConstants(pushConstants);

            uint groupsX = (uint)((regionSize + 7) / 8);
            uint groupsY = (uint)((regionSize + 7) / 8);

            Action<long> commands = op.CreateDispatchCommands(groupsX, groupsY);
            tempRids.AddRange(op.GetTemporaryRids());

            return (commands, tempRids, shaderPaths);
        }

        private (Action<long> commands, List<Rid> tempRids, List<string> shaders) CreateTextureEditCommands(
           Vector2I regionCoords,
           RegionData regionData,
           ManualEditBuffer editBuffer,
           int regionSize)
        {
            const string SHADER_PATH = "res://addons/terrain_3d_tools/Shaders/ManualEdit/apply_texture_edit.glsl";

            var tempRids = new List<Rid>();
            var shaderPaths = new List<string> { SHADER_PATH };

            var op = new AsyncComputeOperation(SHADER_PATH);
            if (!op.IsValid())
            {
                DebugManager.Instance?.LogError(DEBUG_CLASS_NAME,
                    "Failed to create texture edit compute operation");
                return (null, tempRids, new List<string>());
            }

            op.BindStorageImage(0, regionData.ControlMap);
            op.BindStorageImage(1, editBuffer.TextureEdit);

            // Apply push constants: 8 values × 4 bytes = 32 bytes (16-byte aligned)
            // Use default threshold values matching BrushSettings defaults
            var pushConstants = GpuUtils.CreatePushConstants()
                .Add(regionSize)              // 0: u_region_size
                .Add(40)                      // 4: overlay_min_visible_blend (default)
                .Add(215)                     // 8: base_max_visible_blend (default)
                .Add(128)                     // 12: base_override_threshold (default)
                .Add(128)                     // 16: overlay_override_threshold (default)
                .Add(2.0f)                    // 20: blend_reduction_rate (default)
                .AddPadding(8)                // 24-31: padding to 32 bytes
                .Build();

            op.SetPushConstants(pushConstants);

            uint groupsX = (uint)((regionSize + 7) / 8);
            uint groupsY = (uint)((regionSize + 7) / 8);

            Action<long> commands = op.CreateDispatchCommands(groupsX, groupsY);
            tempRids.AddRange(op.GetTemporaryRids());

            return (commands, tempRids, shaderPaths);
        }

        private (Action<long> commands, List<Rid> tempRids, List<string> shaders) CreateInstanceExclusionCommands(
            Vector2I regionCoords,
            RegionData regionData,
            ManualEditBuffer editBuffer,
            int regionSize)
        {
            const string SHADER_PATH = "res://addons/terrain_3d_tools/Shaders/ManualEdit/apply_instance_exclusion.glsl";

            var tempRids = new List<Rid>();
            var shaderPaths = new List<string> { SHADER_PATH };

            var exclusionMap = regionData.GetOrCreateExclusionMap(regionSize);
            if (!exclusionMap.IsValid)
            {
                DebugManager.Instance?.LogError(DEBUG_CLASS_NAME,
                    $"Failed to get/create exclusion map for region {regionCoords}");
                return (null, tempRids, new List<string>());
            }

            var op = new AsyncComputeOperation(SHADER_PATH);
            if (!op.IsValid())
            {
                DebugManager.Instance?.LogError(DEBUG_CLASS_NAME,
                    "Failed to create instance exclusion compute operation");
                return (null, tempRids, new List<string>());
            }

            op.BindStorageImage(0, exclusionMap);
            op.BindStorageImage(1, editBuffer.InstanceExclusion);

            var pushConstants = GpuUtils.CreatePushConstants()
                .Add(regionSize)
                .AddPadding(12)
                .Build();
            op.SetPushConstants(pushConstants);

            uint groupsX = (uint)((regionSize + 7) / 8);
            uint groupsY = (uint)((regionSize + 7) / 8);

            Action<long> commands = op.CreateDispatchCommands(groupsX, groupsY);
            tempRids.AddRange(op.GetTemporaryRids());

            return (commands, tempRids, shaderPaths);
        }

        #endregion
    }
}