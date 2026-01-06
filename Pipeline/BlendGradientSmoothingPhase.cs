using Godot;
using System;
using System.Collections.Generic;
using Terrain3DTools.Core;
using Terrain3DTools.Core.Debug;
using System.Linq;
using Terrain3DTools.Utils;

namespace Terrain3DTools.Pipeline
{
    /// <summary>
    /// Phase 7: Smooths blend gradients in control maps to reduce harsh texture boundaries.
    /// <para>
    /// This phase uses a <b>Lazy (JIT)</b> execution model.
    /// Resources are allocated strictly at execution time to manage VRAM usage.
    /// </para>
    /// </summary>
    public class BlendGradientSmoothingPhase : IProcessingPhase
    {
        private const string DEBUG_CLASS_NAME = "BlendGradientSmoothingPhase";
        private const string SHADER_PATH = "res://addons/terrain_3d_tools/Shaders/Pipeline/BlendGradientSmoothing.glsl";

        public BlendGradientSmoothingPhase()
        {
            DebugManager.Instance?.RegisterClass(DEBUG_CLASS_NAME);
        }

        public Dictionary<object, AsyncGpuTask> Execute(TerrainProcessingContext context)
        {
            var tasks = new Dictionary<object, AsyncGpuTask>();

            // Skip if smoothing is disabled
            if (!context.EnableBlendSmoothing) return tasks;

            DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.PhaseExecution,
                $"Smoothing blend gradients for {context.AllDirtyRegions.Count} region(s) " +
                $"(passes={context.SmoothingPasses}, strength={context.SmoothingStrength:F2})");

            foreach (var regionCoords in context.AllDirtyRegions)
            {
                if (!context.CurrentlyActiveRegions.Contains(regionCoords)) continue;

                // Build dependencies
                var dependencies = new List<AsyncGpuTask>();
                if (context.RegionTextureCompositeTasks.TryGetValue(regionCoords, out var textureTask))
                    dependencies.Add(textureTask);
                if (context.RegionFeatureApplicationTasks.TryGetValue(regionCoords, out var featureTask))
                    dependencies.Add(featureTask);

                // If no dependencies exist, this region wasn't modified by any texture/feature layer
                if (dependencies.Count == 0) continue;

                var currentRegionCoords = regionCoords;
                Action onComplete = () =>
                {
                    // Aggregated logging is handled by manager, detailed logging only on request
                    // DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.RegionCompositing, 
                    //    $"Blend smoothing complete for region {currentRegionCoords}");
                };

                var task = CreateSmoothingTaskLazy(
                    regionCoords,
                    dependencies,
                    onComplete,
                    context);

                if (task != null)
                {
                    context.BlendSmoothingTasks[regionCoords] = task;
                    tasks[regionCoords] = task;
                    AsyncGpuTaskManager.Instance.AddTask(task);
                }
            }

            return tasks;
        }

        /// <summary>
        /// Creates a Lazy AsyncGpuTask.
        /// The Uniform Buffer creation and Shader binding happen inside the generator.
        /// </summary>
        private AsyncGpuTask CreateSmoothingTaskLazy(
            Vector2I regionCoords,
            List<AsyncGpuTask> dependencies,
            Action onComplete,
            TerrainProcessingContext context)
        {
            // Capture primitive values for closure
            int regionSize = context.RegionSize;
            int passes = context.SmoothingPasses;
            
            // --- GENERATOR FUNCTION (Executed JIT) ---
            Func<(Action<long>, List<Rid>, List<string>)> generator = () =>
            {
                // Verify region data exists at execution time
                var regionData = context.RegionMapManager.GetRegionData(regionCoords);
                if (regionData == null || !regionData.ControlMap.IsValid)
                {
                    DebugManager.Instance?.LogWarning(DEBUG_CLASS_NAME, 
                        $"JIT Prep Skipped: Region {regionCoords} has invalid control map");
                    return ((l) => { }, new List<Rid>(), new List<string>());
                }

                DebugManager.Instance?.StartTimer(DEBUG_CLASS_NAME, DebugCategory.GpuResources, 
                    $"JIT_Prep:{regionCoords}");

                var allCommands = new List<Action<long>>();
                var allTempRids = new List<Rid>();
                var allShaderPaths = new List<string>();

                // 1. Create Settings Buffer (Allocation)
                // Note: We do NOT add this to allTempRids yet. See cleanup logic below.
                Rid settingsBufferRid = CreateSettingsBuffer(context);
                if (!settingsBufferRid.IsValid)
                {
                    DebugManager.Instance?.LogError(DEBUG_CLASS_NAME, 
                        $"Failed to create settings buffer for region {regionCoords}");
                    return ((l) => { }, new List<Rid>(), new List<string>());
                }

                uint groupsX = (uint)((regionSize + 7) / 8);
                uint groupsY = (uint)((regionSize + 7) / 8);

                var pushConstants = GpuUtils.CreatePushConstants()
                    .Add((uint)regionSize)
                    .Add((uint)regionSize)
                    .Add(0u).Add(0u) // padding
                    .Build();

                // 2. Create Dispatches for each pass
                for (int pass = 0; pass < passes; pass++)
                {
                    var operation = new AsyncComputeOperation(SHADER_PATH);

                    // Binding 0: Control map (read/write)
                    operation.BindStorageImage(0, regionData.ControlMap);
                    
                    // Binding 1: Settings uniform buffer
                    operation.BindUniformBuffer(1, settingsBufferRid);
                    
                    operation.SetPushConstants(pushConstants);

                    var cmd = operation.CreateDispatchCommands(groupsX, groupsY);
                    if (cmd != null)
                    {
                        allCommands.Add(cmd);
                        
                        // Add the UniformSets created by the operation to cleanup list.
                        // These depend on settingsBufferRid.
                        allTempRids.AddRange(operation.GetTemporaryRids());
                        
                        // Only add shader path once
                        if (pass == 0) allShaderPaths.Add(SHADER_PATH);
                    }
                    else
                    {
                        DebugManager.Instance?.LogError(DEBUG_CLASS_NAME, 
                            $"Failed to create dispatch for region {regionCoords} pass {pass}");
                    }
                }

                // 3. Add Settings Buffer to Cleanup List LAST
                // CRITICAL FIX: The Uniform Buffer must be freed AFTER the UniformSets that use it.
                // Godot/Vulkan will auto-invalidate Sets if the Buffer dies first, causing 
                // "Invalid ID" errors when we try to free the Sets later.
                // By adding it last, the Graveyard (FIFO) will free it last.
                allTempRids.Add(settingsBufferRid);

                DebugManager.Instance?.EndTimer(DEBUG_CLASS_NAME, DebugCategory.GpuResources, 
                    $"JIT_Prep:{regionCoords}");

                if (allCommands.Count == 0) return ((l) => { }, new List<Rid>(), new List<string>());

                // 4. Combine Commands with Barriers
                Action<long> combinedCommands = (computeList) =>
                {
                    // Barrier before start to ensure previous phases are done
                    Gpu.Rd.ComputeListAddBarrier(computeList);

                    for (int i = 0; i < allCommands.Count; i++)
                    {
                        allCommands[i]?.Invoke(computeList);

                        // Barrier between passes to ensure read-after-write consistency
                        if (i < allCommands.Count - 1)
                        {
                            Gpu.Rd.ComputeListAddBarrier(computeList);
                        }
                    }
                };

                return (combinedCommands, allTempRids, allShaderPaths);
            };

            // Used for resource ownership tracking
            var regionDataRef = context.RegionMapManager.GetRegionData(regionCoords);
            var owners = new List<object> { regionDataRef };

            return new AsyncGpuTask(
                generator,
                onComplete,
                owners,
                $"Blend smoothing: Region {regionCoords} ({passes} passes)",
                dependencies);
        }

        private Rid CreateSettingsBuffer(TerrainProcessingContext context)
        {
            var data = new List<byte>();
            data.AddRange(BitConverter.GetBytes(context.SmoothingStrength));
            data.AddRange(BitConverter.GetBytes(context.IsolationThreshold));
            data.AddRange(BitConverter.GetBytes((float)context.IsolationBlendTarget));
            data.AddRange(BitConverter.GetBytes(context.IsolationStrength));
            data.AddRange(BitConverter.GetBytes((uint)context.MinBlendForSmoothing));
            data.AddRange(BitConverter.GetBytes(context.ConsiderSwappedPairs ? 1u : 0u));
            data.AddRange(BitConverter.GetBytes(0u)); // pad
            data.AddRange(BitConverter.GetBytes(0u)); // pad

            return Gpu.Rd.UniformBufferCreate((uint)data.Count, data.ToArray());
        }
    }
}