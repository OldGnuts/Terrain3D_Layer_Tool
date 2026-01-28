using Godot;
using Terrain3DTools.Core;
using Terrain3DTools.Layers;
using Terrain3DTools.Core.Debug;
using System;
using System.Collections.Generic;

namespace Terrain3DTools.Pipeline
{
    /// <summary>
    /// Pipeline responsible for building layer mask textures from layer parameters and mask arrays.
    /// <para>
    /// Uses <b>Lazy (JIT)</b> task generation. All resource allocation and 
    /// command building is deferred until the AsyncGpuTaskManager prepares the task.
    /// </para>
    /// </summary>
    public static class LayerMaskPipeline
    {
        private const string DEBUG_CLASS_NAME = "LayerMaskPipeline";

        static LayerMaskPipeline()
        {
            DebugManager.Instance?.RegisterClass(DEBUG_CLASS_NAME);
        }

        /// <summary>
        /// Creates a Lazy AsyncGpuTask to update a layer's texture.
        /// </summary>
        public static AsyncGpuTask CreateUpdateLayerTextureTask(
            Rid targetTexture,
            TerrainLayerBase layer,
            int maskWidth,
            int maskHeight,
            Rid heightmapArray,
            Rid metadataBuffer,
            int regionCountInArray,
            List<AsyncGpuTask> dependencies,
            Action onCompleteCallback)
        {
            if (!targetTexture.IsValid)
            {
                DebugManager.Instance?.LogError(DEBUG_CLASS_NAME, "Cannot create task for invalid texture");
                return null;
            }

            // Capture state for closure
            bool layerWasValid = GodotObject.IsInstanceValid(layer);
            Color clearColor = layer.GetLayerType() == LayerType.Height ? Colors.White : Colors.Black;
            bool useFalloff = layer.FalloffApplyMode == FalloffApplication.ApplyToMask;

            // --- GENERATOR FUNCTION (Executed JIT) ---
            Func<(Action<long>, List<Rid>, List<string>)> generator = () =>
            {
                if (!layerWasValid || !GodotObject.IsInstanceValid(layer))
                {
                    return ((l) => { }, new List<Rid>(), new List<string>());
                }

                DebugManager.Instance?.StartTimer(DEBUG_CLASS_NAME, DebugCategory.MaskGeneration,
                    $"GenerateMask:{layer.LayerName}");

                var allGpuCommands = new List<Action<long>>();
                var allTempRids = new List<Rid>();
                var allShaderPaths = new List<string>();

                // 1. Clear
                var (clearCmd, clearRids, clearShaderPath) = GpuKernels.CreateClearCommands(
                    targetTexture, clearColor, maskWidth, maskHeight, DEBUG_CLASS_NAME);

                if (clearCmd != null)
                {
                    allShaderPaths.Add(clearShaderPath);
                    allGpuCommands.Add(clearCmd);
                    allTempRids.AddRange(clearRids);
                }

                // 2. Stitching (if needed)
                Rid localStitchedHeightmap = new Rid();
                if (heightmapArray.IsValid &&
                    regionCountInArray > 0 &&
                    layer.layerHeightVisualizationTextureRID.IsValid &&
                    layer.DoesAnyMaskRequireHeightData())
                {
                    localStitchedHeightmap = layer.layerHeightVisualizationTextureRID;

                    var (scCmd, scRids, scShader) = GpuKernels.CreateClearCommands(
                        localStitchedHeightmap, Colors.Black, maskWidth, maskHeight, DEBUG_CLASS_NAME);
                    if (scCmd != null) { allGpuCommands.Add(scCmd); allTempRids.AddRange(scRids); allShaderPaths.Add(scShader); }

                    var (stitchCmd, stitchRids, stitchShader) = GpuKernels.CreateStitchHeightmapCommands(
                        localStitchedHeightmap,
                        heightmapArray,
                        metadataBuffer,
                        maskWidth,
                        maskHeight,
                        regionCountInArray,
                        DEBUG_CLASS_NAME);

                    if (stitchCmd != null)
                    {
                        allShaderPaths.Add(stitchShader);
                        allGpuCommands.Add(stitchCmd);
                        allTempRids.AddRange(stitchRids);
                    }
                }

                // 3. Apply Masks
                foreach (var mask in layer.Masks)
                {
                    if (mask != null)
                    {
                        var (maskCmd, maskRids, maskShaders) = mask.CreateApplyCommands(
                            targetTexture, maskWidth, maskHeight, localStitchedHeightmap);

                        if (maskCmd != null)
                        {
                            allShaderPaths.AddRange(maskShaders);
                            allGpuCommands.Add(maskCmd);
                            allTempRids.AddRange(maskRids);
                        }
                    }
                }

                // 4. Falloff
                if (useFalloff)
                {
                    var (falloffCmd, falloffRids, falloffShaderPath) = GpuKernels.CreateFalloffCommands(
                        layer, targetTexture, maskWidth, maskHeight);

                    if (falloffCmd != null)
                    {
                        allGpuCommands.Add(falloffCmd);
                        allTempRids.AddRange(falloffRids);
                        allShaderPaths.Add(falloffShaderPath);
                    }
                }

                DebugManager.Instance?.EndTimer(DEBUG_CLASS_NAME, DebugCategory.MaskGeneration,
                    $"GenerateMask:{layer.LayerName}");

                if (allGpuCommands.Count == 0) return ((l) => { }, new List<Rid>(), new List<string>());

                Action<long> combinedGpuCommands = (computeList) =>
                {
                    Gpu.Rd.ComputeListAddBarrier(computeList);
                    for (int i = 0; i < allGpuCommands.Count; i++)
                    {
                        allGpuCommands[i]?.Invoke(computeList);
                        if (i < allGpuCommands.Count - 1)
                        {
                            Gpu.Rd.ComputeListAddBarrier(computeList);
                        }
                    }
                };

                return (combinedGpuCommands, allTempRids, allShaderPaths);
            };
            // --- END GENERATOR ---

            var owners = new List<object> { layer };
            string taskName = layer.GetLayerType() == LayerType.Height ? "Height Mask (Lazy)" : "Texture Mask (Lazy)";

            return new AsyncGpuTask(
                generator,
                onCompleteCallback,
                owners,
                taskName,
                dependencies);
        }

        /// <summary>
        /// Creates a Lazy AsyncGpuTask to update a feature layer's texture.
        /// </summary>
        public static AsyncGpuTask CreateUpdateFeatureLayerTextureTask(
            Rid targetTexture,
            FeatureLayer featureLayer,
            int maskWidth,
            int maskHeight,
            Rid heightmapArray,
            Rid metadataBuffer,
            int regionCountInArray,
            List<AsyncGpuTask> dependencies,
            Action onCompleteCallback)
        {
            if (!targetTexture.IsValid) return null;

            if (featureLayer is PathLayer pathLayer)
            {
                return CreatePathLayerMaskTaskLazy(
                    targetTexture, pathLayer, maskWidth, maskHeight, heightmapArray, metadataBuffer, regionCountInArray, dependencies, onCompleteCallback);
            }
            else if (featureLayer is InstancerLayer instancerLayer)
            {
                return CreateInstancerLayerMaskTaskLazy(
                    targetTexture, instancerLayer, maskWidth, maskHeight, heightmapArray, metadataBuffer, regionCountInArray, dependencies, onCompleteCallback);
            }
            else
            {
                DebugManager.Instance?.LogWarning(DEBUG_CLASS_NAME,
                    $"Unknown feature layer type: {featureLayer.GetType().Name}");
                return null;
            }
        }

        /// <summary>
        /// Creates a Lazy mask generation task specifically for path layers.
        /// <para>
        /// Uses GetActiveBakeState() to ensure the same state captured in 
        /// PrepareMaskResources() is used by all phases of the pipeline.
        /// </para>
        /// </summary>
        private static AsyncGpuTask CreatePathLayerMaskTaskLazy(
            Rid targetTexture,
            PathLayer pathLayer,
            int maskWidth,
            int maskHeight,
            Rid heightmapArray,
            Rid metadataBuffer,
            int regionCountInArray,
            List<AsyncGpuTask> dependencies,
            Action onCompleteCallback)
        {
            bool layerWasValid = GodotObject.IsInstanceValid(pathLayer);

            // USE UNIFIED STATE FROM PrepareMaskResources() ---
            // Do NOT call CaptureBakeState() here - that would create divergent state.
            // GetActiveBakeState() returns the state captured during Phase 4.
            var bakeState = layerWasValid ? pathLayer.GetActiveBakeState() : null;

            if (bakeState == null)
            {
                DebugManager.Instance?.LogWarning(DEBUG_CLASS_NAME,
                    $"PathLayer '{pathLayer?.LayerName}' has no active bake state - skipping mask generation");
                return null;
            }

            // Log the state version for debugging
            DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.MaskGeneration,
                $"CreatePathLayerMaskTaskLazy using state version {bakeState.CycleVersion} for '{pathLayer.LayerName}'");

            // --- GENERATOR FUNCTION (Executed JIT) ---
            Func<(Action<long>, List<Rid>, List<string>)> generator = () =>
            {
                if (!layerWasValid || !GodotObject.IsInstanceValid(pathLayer) || bakeState == null)
                {
                    return ((l) => { }, new List<Rid>(), new List<string>());
                }

                DebugManager.Instance?.StartTimer(DEBUG_CLASS_NAME, DebugCategory.MaskGeneration,
                    $"GeneratePathMask:{pathLayer.LayerName}");

                var allGpuCommands = new List<Action<long>>();
                var allTempRids = new List<Rid>();
                var allShaderPaths = new List<string>();

                // 1. Clear
                var (clearCmd, clearRids, clearShaderPath) = GpuKernels.CreateClearCommands(
                    targetTexture, Colors.Black, maskWidth, maskHeight, DEBUG_CLASS_NAME);

                if (clearCmd != null)
                {
                    allGpuCommands.Add(clearCmd);
                    allTempRids.AddRange(clearRids);
                    allShaderPaths.Add(clearShaderPath);
                }

                // 2. Path Geometry Generation (SDF + Mask)
                // Pass the captured state - NOT the PathLayer instance
                var (pathCmd, pathRids, pathShaderPaths) = pathLayer.CreatePathMaskCommandsFromState(bakeState);

                if (pathCmd != null)
                {
                    allGpuCommands.Add(pathCmd);
                    allTempRids.AddRange(pathRids);
                    allShaderPaths.AddRange(pathShaderPaths);
                }

                // 3. User Masks
                foreach (var mask in pathLayer.Masks)
                {
                    if (mask != null)
                    {
                        var (maskCmd, maskRids, maskShaders) = mask.CreateApplyCommands(
                            targetTexture, maskWidth, maskHeight, new Rid());

                        if (maskCmd != null)
                        {
                            allGpuCommands.Add(maskCmd);
                            allTempRids.AddRange(maskRids);
                            allShaderPaths.AddRange(maskShaders);
                        }
                    }
                }

                // 4. Falloff
                var (falloffCmd, falloffRids, falloffShaderPath) = GpuKernels.CreateFalloffCommands(
                    pathLayer, targetTexture, maskWidth, maskHeight);

                if (falloffCmd != null)
                {
                    allGpuCommands.Add(falloffCmd);
                    allTempRids.AddRange(falloffRids);
                    allShaderPaths.Add(falloffShaderPath);
                }

                DebugManager.Instance?.EndTimer(DEBUG_CLASS_NAME, DebugCategory.MaskGeneration,
                    $"GeneratePathMask:{pathLayer.LayerName}");

                if (allGpuCommands.Count == 0) return ((l) => { }, new List<Rid>(), new List<string>());

                Action<long> combinedGpuCommands = (computeList) =>
                {
                    Gpu.Rd.ComputeListAddBarrier(computeList);
                    for (int i = 0; i < allGpuCommands.Count; i++)
                    {
                        allGpuCommands[i]?.Invoke(computeList);
                        if (i < allGpuCommands.Count - 1) Gpu.Rd.ComputeListAddBarrier(computeList);
                    }
                };

                return (combinedGpuCommands, allTempRids, allShaderPaths);
            };

            var owners = new List<object> { pathLayer };
            return new AsyncGpuTask(generator, onCompleteCallback, owners, "PathLayer Mask (Lazy)", dependencies);
        }



        /// <summary>
        /// Creates a Lazy mask generation task for instancer layers.
        /// Generates a density mask from the layer's mask stack.
        /// </summary>
        private static AsyncGpuTask CreateInstancerLayerMaskTaskLazy(
            Rid targetTexture,
            InstancerLayer instancerLayer,
            int maskWidth,
            int maskHeight,
            Rid heightmapArray,
            Rid metadataBuffer,
            int regionCountInArray,
            List<AsyncGpuTask> dependencies,
            Action onCompleteCallback)
        {
            bool layerWasValid = GodotObject.IsInstanceValid(instancerLayer);

            // Capture bake state for thread safety
            var bakeState = layerWasValid ? instancerLayer.GetActiveBakeState() : null;

            if (bakeState == null)
            {
                DebugManager.Instance?.LogWarning(DEBUG_CLASS_NAME,
                    $"InstancerLayer '{instancerLayer?.LayerName}' has no active bake state - skipping mask generation");
                return null;
            }

            DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.MaskGeneration,
                $"CreateInstancerLayerMaskTaskLazy for '{instancerLayer.LayerName}'");

            // --- GENERATOR FUNCTION (Executed JIT) ---
            Func<(Action<long>, List<Rid>, List<string>)> generator = () =>
            {
                if (!layerWasValid || !GodotObject.IsInstanceValid(instancerLayer))
                {
                    return ((l) => { }, new List<Rid>(), new List<string>());
                }

                DebugManager.Instance?.StartTimer(DEBUG_CLASS_NAME, DebugCategory.MaskGeneration,
                    $"GenerateInstancerMask:{instancerLayer.LayerName}");

                var allGpuCommands = new List<Action<long>>();
                var allTempRids = new List<Rid>();
                var allShaderPaths = new List<string>();

                // 1. Clear to black (0 density by default)
                var (clearCmd, clearRids, clearShaderPath) = GpuKernels.CreateClearCommands(
                    targetTexture, Colors.Black, maskWidth, maskHeight, DEBUG_CLASS_NAME);

                if (clearCmd != null)
                {
                    allGpuCommands.Add(clearCmd);
                    allTempRids.AddRange(clearRids);
                    allShaderPaths.Add(clearShaderPath);
                }

                // 2. Stitching (if needed)
                Rid localStitchedHeightmap = new Rid();
                if (heightmapArray.IsValid &&
                    regionCountInArray > 0 &&
                    instancerLayer.layerHeightVisualizationTextureRID.IsValid &&
                    instancerLayer.DoesAnyMaskRequireHeightData())
                {
                    localStitchedHeightmap = instancerLayer.layerHeightVisualizationTextureRID;

                    var (scCmd, scRids, scShader) = GpuKernels.CreateClearCommands(
                        localStitchedHeightmap, Colors.Black, maskWidth, maskHeight, DEBUG_CLASS_NAME);
                    if (scCmd != null) { allGpuCommands.Add(scCmd); allTempRids.AddRange(scRids); allShaderPaths.Add(scShader); }

                    var (stitchCmd, stitchRids, stitchShader) = GpuKernels.CreateStitchHeightmapCommands(
                        localStitchedHeightmap,
                        heightmapArray,
                        metadataBuffer,
                        maskWidth,
                        maskHeight,
                        regionCountInArray,
                        DEBUG_CLASS_NAME);

                    if (stitchCmd != null)
                    {
                        allShaderPaths.Add(stitchShader);
                        allGpuCommands.Add(stitchCmd);
                        allTempRids.AddRange(stitchRids);
                    }
                }

                // 3. Fill with white (full density) as base - masks will subtract/modify
                var (fillCmd, fillRids, fillShaderPath) = GpuKernels.CreateClearCommands(
                    targetTexture, Colors.Black, maskWidth, maskHeight, DEBUG_CLASS_NAME);

                if (fillCmd != null)
                {
                    allGpuCommands.Add(fillCmd);
                    allTempRids.AddRange(fillRids);
                    allShaderPaths.Add(fillShaderPath);
                }

                // 4. Apply user masks (slope, noise, etc.) to create density map
                foreach (var mask in instancerLayer.Masks)
                {
                    if (mask != null)
                    {
                        var (maskCmd, maskRids, maskShaders) = mask.CreateApplyCommands(
                            targetTexture, maskWidth, maskHeight, localStitchedHeightmap);

                        if (maskCmd != null)
                        {
                            allGpuCommands.Add(maskCmd);
                            allTempRids.AddRange(maskRids);
                            allShaderPaths.AddRange(maskShaders);
                        }
                    }
                }

                // 5. Apply falloff
                if (instancerLayer.FalloffApplyMode == FalloffApplication.ApplyToMask)
                {
                    var (falloffCmd, falloffRids, falloffShaderPath) = GpuKernels.CreateFalloffCommands(
                        instancerLayer, targetTexture, maskWidth, maskHeight);

                    if (falloffCmd != null)
                    {
                        allGpuCommands.Add(falloffCmd);
                        allTempRids.AddRange(falloffRids);
                        allShaderPaths.Add(falloffShaderPath);
                    }
                }

                DebugManager.Instance?.EndTimer(DEBUG_CLASS_NAME, DebugCategory.MaskGeneration,
                    $"GenerateInstancerMask:{instancerLayer.LayerName}");

                if (allGpuCommands.Count == 0) return ((l) => { }, new List<Rid>(), new List<string>());

                Action<long> combinedGpuCommands = (computeList) =>
                {
                    Gpu.Rd.ComputeListAddBarrier(computeList);
                    for (int i = 0; i < allGpuCommands.Count; i++)
                    {
                        allGpuCommands[i]?.Invoke(computeList);
                        if (i < allGpuCommands.Count - 1)
                        {
                            Gpu.Rd.ComputeListAddBarrier(computeList);
                        }
                    }
                };

                return (combinedGpuCommands, allTempRids, allShaderPaths);
            };

            var owners = new List<object> { instancerLayer };
            return new AsyncGpuTask(generator, onCompleteCallback, owners, "Instancer Mask (Lazy)", dependencies);
        }
    }
}