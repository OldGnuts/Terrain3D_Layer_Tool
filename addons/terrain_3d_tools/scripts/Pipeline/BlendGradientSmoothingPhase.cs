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
    /// Phase 8: Smooths blend gradients in control maps to reduce harsh texture boundaries.
    /// Uses lazy (JIT) execution to manage VRAM usage.
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

            if (!context.EnableBlendSmoothing) return tasks;

            DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.PhaseExecution,
                $"Smoothing blend gradients for {context.AllDirtyRegions.Count} region(s) " +
                $"(passes={context.SmoothingPasses}, strength={context.SmoothingStrength:F2})");

            foreach (var regionCoords in context.AllDirtyRegions)
            {
                if (!context.CurrentlyActiveRegions.Contains(regionCoords)) continue;

                var dependencies = new List<AsyncGpuTask>();
                if (context.RegionTextureCompositeTasks.TryGetValue(regionCoords, out var textureTask))
                    dependencies.Add(textureTask);
                if (context.RegionFeatureApplicationTasks.TryGetValue(regionCoords, out var featureTask))
                    dependencies.Add(featureTask);

                if (dependencies.Count == 0) continue;

                var currentRegionCoords = regionCoords;
                Action onComplete = () => { };

                var regionData = context.RegionMapManager.GetRegionData(regionCoords);
                if (regionData == null || !regionData.ControlMap.IsValid) continue;

                var task = CreateSmoothingTaskLazy(
                    regionCoords,
                    dependencies,
                    onComplete,
                    context);

                if (task != null)
                {
                    // ControlMap is both read and written (in-place modification)
                    task.DeclareResources(
                        writes: new[] { regionData.ControlMap },
                        reads: new[] { regionData.ControlMap }
                    );

                    context.BlendSmoothingTasks[regionCoords] = task;
                    tasks[regionCoords] = task;
                    AsyncGpuTaskManager.Instance.AddTask(task);
                }
            }

            return tasks;
        }

        private AsyncGpuTask CreateSmoothingTaskLazy(
            Vector2I regionCoords,
            List<AsyncGpuTask> dependencies,
            Action onComplete,
            TerrainProcessingContext context)
        {
            int regionSize = context.RegionSize;
            int passes = context.SmoothingPasses;
            
            Func<(Action<long>, List<Rid>, List<string>)> generator = () =>
            {
                var regionData = context.RegionMapManager.GetRegionData(regionCoords);
                if (regionData == null || !regionData.ControlMap.IsValid)
                {
                    DebugManager.Instance?.LogWarning(DEBUG_CLASS_NAME, 
                        $"Region {regionCoords} has invalid control map");
                    return ((l) => { }, new List<Rid>(), new List<string>());
                }

                DebugManager.Instance?.StartTimer(DEBUG_CLASS_NAME, DebugCategory.GpuResources, 
                    $"JIT_Prep:{regionCoords}");

                var allCommands = new List<Action<long>>();
                var allTempRids = new List<Rid>();
                var allShaderPaths = new List<string>();

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
                    .Add(0u).Add(0u)
                    .Build();

                for (int pass = 0; pass < passes; pass++)
                {
                    var operation = new AsyncComputeOperation(SHADER_PATH);

                    operation.BindStorageImage(0, regionData.ControlMap);
                    operation.BindUniformBuffer(1, settingsBufferRid);
                    operation.SetPushConstants(pushConstants);

                    var cmd = operation.CreateDispatchCommands(groupsX, groupsY);
                    if (cmd != null)
                    {
                        allCommands.Add(cmd);
                        allTempRids.AddRange(operation.GetTemporaryRids());
                        
                        if (pass == 0) allShaderPaths.Add(SHADER_PATH);
                    }
                    else
                    {
                        DebugManager.Instance?.LogError(DEBUG_CLASS_NAME, 
                            $"Failed to create dispatch for region {regionCoords} pass {pass}");
                    }
                }

                allTempRids.Add(settingsBufferRid);

                DebugManager.Instance?.EndTimer(DEBUG_CLASS_NAME, DebugCategory.GpuResources, 
                    $"JIT_Prep:{regionCoords}");

                if (allCommands.Count == 0) 
                    return ((l) => { }, new List<Rid>(), new List<string>());

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

            var regionDataRef = context.RegionMapManager.GetRegionData(regionCoords);
            var owners = new List<object> { regionDataRef };

            return new AsyncGpuTask(
                generator,
                onComplete,
                owners,
                $"Blend Smoothing: Region {regionCoords} ({passes} passes)",
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
            data.AddRange(BitConverter.GetBytes(0u));
            data.AddRange(BitConverter.GetBytes(0u));

            return Gpu.Rd.UniformBufferCreate((uint)data.Count, data.ToArray());
        }
    }
}