// /Core/Pipeline/LayerMaskPipeline.cs
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
    /// This subsystem orchestrates the multi-step process of generating the final texture data
    /// that represents a layer's influence pattern (height deltas, texture blend, or feature influence).
    /// 
    /// Pipeline stages:
    /// 1. Clear texture to default value
    /// 2. (Optional) Stitch heightmap context for masks that need terrain data
    /// 3. Apply each mask in sequence
    /// 4. Apply falloff attenuation
    /// </summary>
    public static class LayerMaskPipeline
    {
        private const string DEBUG_CLASS_NAME = "LayerMaskPipeline";

        static LayerMaskPipeline()
        {
            DebugManager.Instance?.RegisterClass(DEBUG_CLASS_NAME);
        }

        /// <summary>
        /// Creates an AsyncGpuTask to update a layer's texture from its masks and parameters.
        /// This is the main entry point for height and texture layer mask generation.
        /// </summary>
        /// <param name="targetTexture">The layer's texture RID to write to</param>
        /// <param name="layer">The layer containing masks and parameters</param>
        /// <param name="maskWidth">Output texture width</param>
        /// <param name="maskHeight">Output texture height</param>
        /// <param name="heightmapArray">Optional texture array of region heightmaps for context</param>
        /// <param name="metadataBuffer">Optional metadata buffer for heightmap stitching</param>
        /// <param name="regionCountInArray">Number of regions in the heightmap array</param>
        /// <param name="dependencies">Tasks that must complete before this layer processes</param>
        /// <param name="onCompleteCallback">Callback to invoke when processing completes</param>
        /// <returns>AsyncGpuTask or null if creation failed</returns>
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
                DebugManager.Instance?.LogError(DEBUG_CLASS_NAME,
                    "Cannot create task for an invalid target texture");
                return null;
            }

            DebugManager.Instance?.StartTimer(DEBUG_CLASS_NAME, DebugCategory.MaskGeneration,
                $"CreateMaskTask:{layer.LayerName}");

            var allGpuCommands = new List<Action<long>>();
            var allTempRids = new List<Rid>();
            var allShaderPaths = new List<string>();
            int operationCount = 0;

            Color clearColor = layer.GetLayerType() == LayerType.Height ? Colors.White : Colors.Black;

            var (clearCmd, clearRids, clearShaderPath) = GpuKernels.CreateClearCommands(
                targetTexture, clearColor, maskWidth, maskHeight, DEBUG_CLASS_NAME);

            if (clearCmd != null)
            {
                allShaderPaths.Add(clearShaderPath);
                allGpuCommands.Add(clearCmd);
                allTempRids.AddRange(clearRids);
                operationCount++;

                DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.MaskSetup,
                    $"Layer '{layer.LayerName}' - Added clear operation");
            }

            Rid stitchedHeightmap = new Rid();
            if (heightmapArray.IsValid &&
                regionCountInArray > 0 &&
                layer.layerHeightVisualizationTextureRID.IsValid &&
                layer.DoesAnyMaskRequireHeightData())
            {
                stitchedHeightmap = layer.layerHeightVisualizationTextureRID;

                DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.ShaderOperations,
                    $"Generating height data from {regionCountInArray} overlapping regions for layer.");

                var (stitchClearCmd, stitchClearRids, stitchClearShaderPath) = GpuKernels.CreateClearCommands(
                    stitchedHeightmap, Colors.Black, maskWidth, maskHeight, DEBUG_CLASS_NAME);

                if (stitchClearCmd != null)
                {
                    allShaderPaths.Add(stitchClearShaderPath);
                    allGpuCommands.Add(stitchClearCmd);
                    allTempRids.AddRange(stitchClearRids);
                    operationCount++;
                }

                var (stitchCmd, stitchRids, stitchShaderPath) = GpuKernels.CreateStitchHeightmapCommands(
                    stitchedHeightmap,
                    heightmapArray,
                    metadataBuffer,
                    maskWidth,
                    maskHeight,
                    regionCountInArray,
                    DEBUG_CLASS_NAME);

                if (stitchCmd != null)
                {
                    allShaderPaths.Add(stitchShaderPath);
                    allGpuCommands.Add(stitchCmd);
                    allTempRids.AddRange(stitchRids);
                    operationCount++;

                    allTempRids.Add(heightmapArray);
                    allTempRids.Add(metadataBuffer);

                    DebugManager.Instance.Log(DEBUG_CLASS_NAME, DebugCategory.ShaderOperations,
                        $"Layer '{layer.LayerName}' - Stitched heightmap for mask processing");
                }
            }

            int maskCount = layer.Masks.Count;
            if (maskCount > 0)
            {
                DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.MaskGeneration,
                    $"Layer '{layer.LayerName}' - Processing {maskCount} mask(s)");
            }

            foreach (var mask in layer.Masks)
            {
                if (mask != null)
                {
                    var (maskCmd, maskRids, maskShaderPaths) = mask.CreateApplyCommands(
                        targetTexture, maskWidth, maskHeight, stitchedHeightmap);

                    if (maskCmd != null)
                    {
                        foreach (string shader in maskShaderPaths)
                        {
                            allShaderPaths.Add(shader);
                        }
                        allGpuCommands.Add(maskCmd);
                        allTempRids.AddRange(maskRids);
                        operationCount++;

                        DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.MaskGeneration,
                            $"Layer '{layer.LayerName}' - Added mask operation: {mask.GetType().Name}");
                    }
                }
            }

            var (falloffCmd, falloffRids, falloffShaderPath) = GpuKernels.CreateFalloffCommands(
                layer, targetTexture, maskWidth, maskHeight);

            if (falloffCmd != null)
            {
                allGpuCommands.Add(falloffCmd);
                allTempRids.AddRange(falloffRids);
                allShaderPaths.Add(falloffShaderPath);
                operationCount++;

                DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.MaskBlending,
                    $"Layer '{layer.LayerName}' - Added falloff operation (mode: {layer.FalloffMode}, strength: {layer.FalloffStrength})");
            }

            if (allGpuCommands.Count == 0)
            {
                DebugManager.Instance?.LogWarning(DEBUG_CLASS_NAME,
                    $"Layer '{layer.LayerName}' - No GPU commands generated");
                DebugManager.Instance?.EndTimer(DEBUG_CLASS_NAME, DebugCategory.MaskGeneration,
                    $"CreateMaskTask:{layer.LayerName}");
                return null;
            }

            Action<long> combinedGpuCommands = (computeList) =>
            {
                if (allGpuCommands.Count == 0) return;

                // Barrier BEFORE first command (critical!)
                // Ensures any previous work on this texture is complete
                Gpu.Rd.ComputeListAddBarrier(computeList);

                for (int i = 0; i < allGpuCommands.Count; i++)
                {
                    try
                    {
                        allGpuCommands[i]?.Invoke(computeList);
                        
                        // Barrier AFTER each command (except last)
                        if (i < allGpuCommands.Count - 1)
                        {
                            Gpu.Rd.ComputeListAddBarrier(computeList);
                        }
                    }
                    catch (Exception ex)
                    {
                        DebugManager.Instance?.LogError(DEBUG_CLASS_NAME,
                            $"Layer '{layer.LayerName}' - Failed to execute GPU command {i}: {ex.Message}");
                        break;
                    }
                }
            };

            var owners = new List<object> { layer };
            string taskName = layer.GetLayerType() == LayerType.Height ? "Height Layer Mask" : "Texture Layer Mask";

            DebugManager.Instance?.EndTimer(DEBUG_CLASS_NAME, DebugCategory.MaskGeneration,
                $"CreateMaskTask:{layer.LayerName}");

            DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.PerformanceMetrics,
                $"Layer '{layer.LayerName}' - Mask task created: {operationCount} operations, {allTempRids.Count} temp resources, {allShaderPaths.Count} shaders");

            return new AsyncGpuTask(
                combinedGpuCommands,
                onCompleteCallback,
                allTempRids,
                owners,
                taskName,
                dependencies,
                allShaderPaths);
        }

        /// <summary>
        /// Creates an AsyncGpuTask to update a feature layer's texture.
        /// Feature layers have specialized processing that differs from height/texture layers.
        /// </summary>
        /// <param name="targetTexture">The feature layer's texture RID</param>
        /// <param name="featureLayer">The feature layer to process</param>
        /// <param name="maskWidth">Output texture width</param>
        /// <param name="maskHeight">Output texture height</param>
        /// <param name="dependencies">Tasks that must complete before processing</param>
        /// <param name="onCompleteCallback">Callback to invoke when complete</param>
        /// <returns>AsyncGpuTask or null if creation failed</returns>
        public static AsyncGpuTask CreateUpdateFeatureLayerTextureTask(
            Rid targetTexture,
            FeatureLayer featureLayer,
            int maskWidth,
            int maskHeight,
            List<AsyncGpuTask> dependencies,
            Action onCompleteCallback)
        {
            if (!targetTexture.IsValid)
            {
                DebugManager.Instance?.LogError(DEBUG_CLASS_NAME,
                    "Cannot create feature layer task for invalid target texture");
                return null;
            }

            DebugManager.Instance?.StartTimer(DEBUG_CLASS_NAME, DebugCategory.MaskGeneration,
                $"CreateFeatureMask:{featureLayer.LayerName}");

            AsyncGpuTask task = null;

            if (featureLayer is PathLayer pathLayer)
            {
                task = CreatePathLayerMaskTask(
                    targetTexture, pathLayer, maskWidth, maskHeight, dependencies, onCompleteCallback);
            }
            else
            {
                DebugManager.Instance?.LogWarning(DEBUG_CLASS_NAME,
                    $"Unknown feature layer type: {featureLayer.GetType().Name}");
            }

            DebugManager.Instance?.EndTimer(DEBUG_CLASS_NAME, DebugCategory.MaskGeneration,
                $"CreateFeatureMask:{featureLayer.LayerName}");

            return task;
        }

        /// <summary>
        /// Creates a mask generation task specifically for path layers.
        /// Path layers generate both height data and influence masks through specialized shaders.
        /// </summary>
        private static AsyncGpuTask CreatePathLayerMaskTask(
            Rid targetTexture,
            PathLayer pathLayer,
            int maskWidth,
            int maskHeight,
            List<AsyncGpuTask> dependencies,
            Action onCompleteCallback)
        {
            var allGpuCommands = new List<Action<long>>();
            var allTempRids = new List<Rid>();
            var allShaderPaths = new List<string>();
            int operationCount = 0;

            DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.MaskSetup,
                $"PathLayer '{pathLayer.LayerName}' - Creating path mask task");

            var (clearCmd, clearRids, clearShaderPath) = GpuKernels.CreateClearCommands(
                targetTexture, Colors.Black, maskWidth, maskHeight, DEBUG_CLASS_NAME);

            if (clearCmd != null)
            {
                allGpuCommands.Add(clearCmd);
                allTempRids.AddRange(clearRids);
                allShaderPaths.Add(clearShaderPath);
                operationCount++;
            }

            var (heightCmd, heightRids, heightShaderPaths) = pathLayer.CreatePathHeightDataCommands();

            if (heightCmd != null)
            {
                allGpuCommands.Add(heightCmd);
                allTempRids.AddRange(heightRids);
                allShaderPaths.AddRange(heightShaderPaths);
                operationCount++;

                DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.MaskGeneration,
                    $"PathLayer '{pathLayer.LayerName}' - Added height data generation");
            }

            var (pathCmd, pathRids, pathShaderPaths) = pathLayer.CreatePathMaskCommands();

            if (pathCmd != null)
            {
                allGpuCommands.Add(pathCmd);
                allTempRids.AddRange(pathRids);
                allShaderPaths.AddRange(pathShaderPaths);
                operationCount++;

                DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.MaskGeneration,
                    $"PathLayer '{pathLayer.LayerName}' - Added path rasterization");
            }

            int maskCount = pathLayer.Masks.Count;
            if (maskCount > 0)
            {
                DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.MaskGeneration,
                    $"PathLayer '{pathLayer.LayerName}' - Processing {maskCount} user mask(s)");
            }

            foreach (var mask in pathLayer.Masks)
            {
                if (mask != null)
                {
                    var (maskCmd, maskRids, maskShaderPaths) = mask.CreateApplyCommands(
                        targetTexture, maskWidth, maskHeight, new Rid());

                    if (maskCmd != null)
                    {
                        allGpuCommands.Add(maskCmd);
                        allTempRids.AddRange(maskRids);
                        foreach (string shader in maskShaderPaths)
                        {
                            allShaderPaths.Add(shader);
                        }
                        operationCount++;
                    }
                }
            }

            var (falloffCmd, falloffRids, falloffShaderPath) = GpuKernels.CreateFalloffCommands(
                pathLayer, targetTexture, maskWidth, maskHeight);

            if (falloffCmd != null)
            {
                allGpuCommands.Add(falloffCmd);
                allTempRids.AddRange(falloffRids);
                allShaderPaths.Add(falloffShaderPath);
                operationCount++;

                DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.MaskBlending,
                    $"PathLayer '{pathLayer.LayerName}' - Added falloff");
            }

            if (allGpuCommands.Count == 0)
            {
                DebugManager.Instance?.LogWarning(DEBUG_CLASS_NAME,
                    $"PathLayer '{pathLayer.LayerName}' - No GPU commands generated");
                return null;
            }

            Action<long> combinedGpuCommands = GpuCommandBuilder.CombineCommands(
                allGpuCommands, false, DEBUG_CLASS_NAME);

            DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.PerformanceMetrics,
                $"PathLayer '{pathLayer.LayerName}' - Path mask task created: {operationCount} operations, {allTempRids.Count} temp resources");

            var owners = new List<object> { pathLayer };

            return new AsyncGpuTask(
                combinedGpuCommands,
                onCompleteCallback,
                allTempRids,
                owners,
                "PathLayer Mask Generation",
                dependencies,
                allShaderPaths);
        }
    }
}