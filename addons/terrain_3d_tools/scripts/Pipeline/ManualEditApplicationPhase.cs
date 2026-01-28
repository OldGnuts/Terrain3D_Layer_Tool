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
    /// 
    /// Manual edits are applied on top of composited/processed data,
    /// ensuring they persist through upstream layer regeneration.
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

            // Find all ManualEditLayers in the scene
            var manualEditLayers = FindManualEditLayers(context);
            
            if (manualEditLayers.Count == 0)
            {
                DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.PhaseExecution,
                    "No ManualEditLayers found - skipping phase");
                return tasks;
            }

            // Set region size on all manual edit layers
            foreach (var layer in manualEditLayers)
            {
                layer.SetRegionSize(context.RegionSize);
            }

            DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.PhaseExecution,
                $"Processing {manualEditLayers.Count} ManualEditLayer(s) for {context.AllDirtyRegions.Count} dirty region(s)");

            // Process each dirty region
            foreach (var regionCoords in context.AllDirtyRegions)
            {
                if (!context.CurrentlyActiveRegions.Contains(regionCoords))
                    continue;

                // Collect all layers that have edits in this region
                var layersWithEdits = manualEditLayers
                    .Where(l => l.HasEditsInRegion(regionCoords))
                    .ToList();

                if (layersWithEdits.Count == 0)
                    continue;

                DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.PhaseExecution,
                    $"Region {regionCoords} has edits from {layersWithEdits.Count} layer(s)");

                // Build dependencies
                var dependencies = BuildDependencies(regionCoords, context);

                // Create the task
                var task = CreateManualEditApplicationTaskLazy(
                    regionCoords,
                    layersWithEdits,
                    dependencies,
                    context);

                if (task != null)
                {
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

        /// <summary>
        /// Finds all valid ManualEditLayers in the layer collection.
        /// </summary>
        private List<ManualEditLayer> FindManualEditLayers(TerrainProcessingContext context)
        {
            var result = new List<ManualEditLayer>();

            // Get layers from the dependency manager's feature layers for each region
            // This ensures we only consider layers that are part of the active terrain system
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

            // Also check DirtyFeatureLayers for newly added layers
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

        /// <summary>
        /// Builds dependency list for manual edit application.
        /// Manual edits depend on all previous compositing and processing phases.
        /// </summary>
        private List<AsyncGpuTask> BuildDependencies(
            Vector2I regionCoords,
            TerrainProcessingContext context)
        {
            var dependencies = new List<AsyncGpuTask>();

            // Depend on height composite
            if (context.RegionHeightCompositeTasks.TryGetValue(regionCoords, out var heightTask))
            {
                dependencies.Add(heightTask);
            }

            // Depend on texture composite
            if (context.RegionTextureCompositeTasks.TryGetValue(regionCoords, out var textureTask))
            {
                dependencies.Add(textureTask);
            }

            // Depend on feature application (paths, etc.)
            if (context.RegionFeatureApplicationTasks.TryGetValue(regionCoords, out var featureTask))
            {
                dependencies.Add(featureTask);
            }

            // Depend on blend smoothing if enabled
            if (context.BlendSmoothingTasks.TryGetValue(regionCoords, out var smoothTask))
            {
                dependencies.Add(smoothTask);
            }

            return dependencies;
        }

        /// <summary>
        /// Creates a lazy GPU task to apply manual edits to a region.
        /// </summary>
        private AsyncGpuTask CreateManualEditApplicationTaskLazy(
            Vector2I regionCoords,
            List<ManualEditLayer> layersWithEdits,
            List<AsyncGpuTask> dependencies,
            TerrainProcessingContext context)
        {
            int regionSize = context.RegionSize;

            // Capture layer states for the closure
            // We capture references to the edit buffers now (main thread)
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

            // Generator function - executed when task is prepared
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

                // Process each layer's edits in order
                foreach (var (layer, buffer) in layerBufferPairs)
                {
                    if (!GodotObject.IsInstanceValid(layer))
                        continue;

                    // Apply height edits
                    if (layer.HeightEditingEnabled && buffer.HeightDelta.IsValid)
                    {
                        var (heightCmd, heightRids, heightShaders) = CreateHeightEditCommands(
                            regionCoords,
                            regionData,
                            buffer,
                            regionSize);

                        if (heightCmd != null)
                        {
                            allCommands.Add(heightCmd);
                            allTempRids.AddRange(heightRids);
                            allShaderPaths.AddRange(heightShaders);
                        }
                    }

                    // Apply texture edits
                    if (layer.TextureEditingEnabled && buffer.TextureEdit.IsValid)
                    {
                        var (textureCmd, textureRids, textureShaders) = CreateTextureEditCommands(
                            regionCoords,
                            regionData,
                            buffer,
                            regionSize);

                        if (textureCmd != null)
                        {
                            allCommands.Add(textureCmd);
                            allTempRids.AddRange(textureRids);
                            allShaderPaths.AddRange(textureShaders);
                        }
                    }

                    // Apply instance exclusion
                    if (layer.InstanceEditingEnabled && buffer.InstanceExclusion.IsValid)
                    {
                        var (exclusionCmd, exclusionRids, exclusionShaders) = CreateInstanceExclusionCommands(
                            regionCoords,
                            regionData,
                            buffer,
                            regionSize);

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

                // Combine all commands with barriers
                Action<long> combinedCommands = (computeList) =>
                {
                    Gpu.Rd.ComputeListAddBarrier(computeList);
                    
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

            // Completion callback
            Action onComplete = () =>
            {
                context.RegionMapManager.RefreshRegionPreview(regionCoords);
                
                DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.PhaseExecution,
                    $"Manual edit application complete for region {regionCoords}");
            };

            // Build owners list for resource tracking
            var regionDataRef = context.RegionMapManager.GetRegionData(regionCoords);
            var owners = new List<object> { regionDataRef };
            owners.AddRange(layersWithEdits);

            return new AsyncGpuTask(
                generator,
                onComplete,
                owners,
                $"Manual edit application: Region {regionCoords}",
                dependencies);
        }

        #region GPU Command Generators

        /// <summary>
        /// Creates GPU commands to apply height edits.
        /// Height edits are additive (-1 to +1 range).
        /// </summary>
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

            // Bind resources
            op.BindStorageImage(0, regionData.HeightMap);      // In/Out: composited height
            op.BindStorageImage(1, editBuffer.HeightDelta);    // In: height delta (-1 to +1)

            // Push constants
            var pushConstants = GpuUtils.CreatePushConstants()
                .Add(regionSize)      // u_region_size
                .AddPadding(12)       // Padding to 16 bytes
                .Build();
            op.SetPushConstants(pushConstants);

            // Dispatch dimensions
            uint groupsX = (uint)((regionSize + 7) / 8);
            uint groupsY = (uint)((regionSize + 7) / 8);

            Action<long> commands = op.CreateDispatchCommands(groupsX, groupsY);
            tempRids.AddRange(op.GetTemporaryRids());

            return (commands, tempRids, shaderPaths);
        }

        /// <summary>
        /// Creates GPU commands to apply texture edits.
        /// Texture edits selectively override base/overlay/blend values.
        /// </summary>
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

            // Bind resources
            op.BindStorageImage(0, regionData.ControlMap);     // In/Out: composited control
            op.BindStorageImage(1, editBuffer.TextureEdit);    // In: edit data (packed format)

            // Push constants
            var pushConstants = GpuUtils.CreatePushConstants()
                .Add(regionSize)      // u_region_size
                .AddPadding(12)       // Padding to 16 bytes
                .Build();
            op.SetPushConstants(pushConstants);

            // Dispatch dimensions
            uint groupsX = (uint)((regionSize + 7) / 8);
            uint groupsY = (uint)((regionSize + 7) / 8);

            Action<long> commands = op.CreateDispatchCommands(groupsX, groupsY);
            tempRids.AddRange(op.GetTemporaryRids());

            return (commands, tempRids, shaderPaths);
        }

        /// <summary>
        /// Creates GPU commands to apply instance exclusion edits.
        /// Combines with existing exclusion map (max operation).
        /// </summary>
        private (Action<long> commands, List<Rid> tempRids, List<string> shaders) CreateInstanceExclusionCommands(
            Vector2I regionCoords,
            RegionData regionData,
            ManualEditBuffer editBuffer,
            int regionSize)
        {
            const string SHADER_PATH = "res://addons/terrain_3d_tools/Shaders/ManualEdit/apply_instance_exclusion.glsl";

            var tempRids = new List<Rid>();
            var shaderPaths = new List<string> { SHADER_PATH };

            // Ensure region has an exclusion map
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

            // Bind resources
            op.BindStorageImage(0, exclusionMap);                   // In/Out: region exclusion
            op.BindStorageImage(1, editBuffer.InstanceExclusion);  // In: manual exclusion

            // Push constants
            var pushConstants = GpuUtils.CreatePushConstants()
                .Add(regionSize)      // u_region_size
                .AddPadding(12)       // Padding to 16 bytes
                .Build();
            op.SetPushConstants(pushConstants);

            // Dispatch dimensions
            uint groupsX = (uint)((regionSize + 7) / 8);
            uint groupsY = (uint)((regionSize + 7) / 8);

            Action<long> commands = op.CreateDispatchCommands(groupsX, groupsY);
            tempRids.AddRange(op.GetTemporaryRids());

            return (commands, tempRids, shaderPaths);
        }

        #endregion
    }
}