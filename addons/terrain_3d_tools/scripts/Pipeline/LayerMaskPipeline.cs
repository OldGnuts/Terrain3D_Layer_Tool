// /Pipeline/LayerMaskPipeline.cs
using Godot;
using Terrain3DTools.Core;
using Terrain3DTools.Layers;
using Terrain3DTools.Core.Debug;
using System;
using System.Collections.Generic;

namespace Terrain3DTools.Pipeline
{
    public static class LayerMaskPipeline
    {
        private const string DEBUG_CLASS_NAME = "LayerMaskPipeline";

        static LayerMaskPipeline()
        {
            DebugManager.Instance?.RegisterClass(DEBUG_CLASS_NAME);
        }

        public static AsyncGpuTask CreateUpdateLayerTextureTask(
            Rid targetTexture, TerrainLayerBase layer, int maskWidth, int maskHeight,
            Rid heightmapArray, Rid metadataBuffer, int regionCountInArray,
            List<AsyncGpuTask> dependencies,
            Action onCompleteCallback
        )
        {
            if (!targetTexture.IsValid)
            {
                DebugManager.Instance?.LogError(DEBUG_CLASS_NAME,
                    "Cannot create task for an invalid target texture");
                return null;
            }

            DebugManager.Instance?.StartTimer(DEBUG_CLASS_NAME, DebugCategory.MaskGeneration,
                $"CreateMaskTask:{layer.LayerName}");

            // This holds actions that accept a compute list handle.
            var allGpuCommands = new List<Action<long>>();
            var allTempRids = new List<Rid>();
            var allShaderPaths = new List<string>();

            int operationCount = 0;

            // STEP 1: CLEAR TEXTURE
            Color clearColor = layer.GetLayerType() == LayerType.Height ? Colors.White : Colors.Black;

            var (clearCmd, clearRids, clearShaderPath) = CreateClearCommands(targetTexture, maskWidth, maskHeight, clearColor);
            if (clearCmd != null)
            {
                allShaderPaths.Add(clearShaderPath);
                allGpuCommands.Add(clearCmd);
                allTempRids.AddRange(clearRids);
                operationCount++;

                DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.MaskSetup,
                    $"Layer '{layer.LayerName}' - Added clear operation");
            }

            // STEP 2: PRE-STITCHING HEIGHTMAP (for texture layers that need height data, and layer visualztion)
            Rid stitchedHeightmap = new Rid();
            if (heightmapArray.IsValid && regionCountInArray > 0 && layer.layerHeightVisualizationTextureRID.IsValid)
            {
                stitchedHeightmap = layer.layerHeightVisualizationTextureRID;

                // 2a. Clear the visualization texture first
                var (stitchClearCmd, stitchClearRids, stitchClearShaderPath) = CreateClearCommands(stitchedHeightmap, maskWidth, maskHeight, Colors.Black);
                if (stitchClearCmd != null)
                {
                    allShaderPaths.Add(stitchClearShaderPath);
                    allGpuCommands.Add(stitchClearCmd);
                    allTempRids.AddRange(stitchClearRids);
                    operationCount++;
                }

                // 2b. Run the stitch compute shader
                var (stitchCmd, stitchRids, stitchShaderPath) = CreateStitchHeightmapCommands(
                    stitchedHeightmap,
                    heightmapArray,
                    metadataBuffer,
                    maskWidth,
                    maskHeight,
                    regionCountInArray
                );

                if (stitchCmd != null)
                {
                    allShaderPaths.Add(stitchShaderPath);
                    allGpuCommands.Add(stitchCmd);
                    allTempRids.AddRange(stitchRids);
                    operationCount++;

                    // Clean up the staging resources created by the Stager
                    allTempRids.Add(heightmapArray);
                    allTempRids.Add(metadataBuffer);

                    if (DebugManager.Instance != null)
                    {
                        DebugManager.Instance.Log(DEBUG_CLASS_NAME, DebugCategory.MaskSetup,
                        $"Layer '{layer.LayerName}' - Stitched heightmap for visualization/processing");
                    }
                }
            }

            // STEP 3: MASK APPLICATION
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
                    var (maskCmd, maskRids, maskShaderPath) = mask.CreateApplyCommands(targetTexture, maskWidth, maskHeight, stitchedHeightmap);
                    if (maskCmd != null)
                    {
                        foreach (string s in maskShaderPath)
                        {
                            allShaderPaths.Add(s);
                        }
                        allGpuCommands.Add(maskCmd);
                        allTempRids.AddRange(maskRids);
                        operationCount++;

                        DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.MaskGeneration,
                            $"Layer '{layer.LayerName}' - Added mask operation: {mask.GetType().Name}");
                    }
                }
            }

            // STEP 4: FALLOFF APPLICATION
            var (falloffCmd, falloffRids, falloffShaderPath) = CreateFalloffCommands(layer, targetTexture, maskWidth, maskHeight);
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

            // The combined command accepts the computeList and passes it to each sub-command.
            Action<long> combinedGpuCommands = (computeList) =>
            {
                if (allGpuCommands.Count == 0) return;

                // Execute all commands with proper barriers
                for (int i = 0; i < allGpuCommands.Count; i++)
                {
                    // Add barrier between operations
                    {
                        Gpu.Rd.ComputeListAddBarrier(computeList);
                    }

                    try
                    {
                        allGpuCommands[i]?.Invoke(computeList);
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
            string taskName = layer.GetLayerType() == LayerType.Height ? "Height" : "Texture";

            DebugManager.Instance?.EndTimer(DEBUG_CLASS_NAME, DebugCategory.MaskGeneration,
                $"CreateMaskTask:{layer.LayerName}");

            DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.PerformanceMetrics,
                $"Layer '{layer.LayerName}' - Mask task created: {operationCount} operations, {allTempRids.Count} temp resources, {allShaderPaths.Count} shaders");

            return new AsyncGpuTask(combinedGpuCommands, onCompleteCallback, allTempRids, owners, taskName, dependencies, allShaderPaths);
        }

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

            // Feature layers may need special handling based on type
            if (featureLayer is PathLayer pathLayer)
            {
                var task = CreatePathLayerMaskTask(targetTexture, pathLayer, maskWidth, maskHeight, dependencies, onCompleteCallback);

                DebugManager.Instance?.EndTimer(DEBUG_CLASS_NAME, DebugCategory.MaskGeneration,
                    $"CreateFeatureMask:{featureLayer.LayerName}");

                return task;
            }

            DebugManager.Instance?.LogWarning(DEBUG_CLASS_NAME,
                $"Unknown feature layer type: {featureLayer.GetType().Name}");
            DebugManager.Instance?.EndTimer(DEBUG_CLASS_NAME, DebugCategory.MaskGeneration,
                $"CreateFeatureMask:{featureLayer.LayerName}");

            return null;
        }

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

            // Step 1: Clear mask texture
            var (clearCmd, clearRids, clearShaderPath) = CreateClearCommands(
                targetTexture, maskWidth, maskHeight, Colors.Black);

            if (clearCmd != null)
            {
                allGpuCommands.Add(clearCmd);
                allTempRids.AddRange(clearRids);
                allShaderPaths.Add(clearShaderPath);
                operationCount++;
            }

            // Step 2: Generate height data texture (separate from mask)
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

            // Step 3: Rasterize path influence into mask
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

            // Step 4: Apply user masks (only affects influence, not height)
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
                        foreach (string s in maskShaderPaths)
                        {
                            allShaderPaths.Add(s);
                        }
                        operationCount++;
                    }
                }
            }

            // Step 5: Apply falloff (only affects influence, not height)
            var (falloffCmd, falloffRids, falloffShaderPath) = CreateFalloffCommands(
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

            Action<long> combinedGpuCommands = (computeList) =>
            {
                for (int i = 0; i < allGpuCommands.Count; i++)
                {
                    if (i > 0)
                    {
                        Gpu.Rd.ComputeListAddBarrier(computeList);
                    }

                    try
                    {
                        allGpuCommands[i]?.Invoke(computeList);
                    }
                    catch (Exception ex)
                    {
                        DebugManager.Instance?.LogError(DEBUG_CLASS_NAME,
                            $"PathLayer '{pathLayer.LayerName}' - Failed to execute path mask command {i}: {ex.Message}");
                        break;
                    }
                }
            };

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

        private static (Action<long>, List<Rid>, string) CreateStitchHeightmapCommands(Rid stitchedHeightmap, Rid heightmapArray, Rid metadataBuffer, int maskWidth, int maskHeight, int regionCountInArray)
        {
            var shaderPath = "res://addons/terrain_3d_tools/Shaders/Pipeline/stitch_heightmap.glsl";
            var operation = new AsyncComputeOperation(shaderPath);

            if (!heightmapArray.IsValid)
            {
                DebugManager.Instance?.LogError(DEBUG_CLASS_NAME,
                    "Invalid heightmapArray for stitch operation");
                return (null, new List<Rid>(), shaderPath);
            }
            if (!metadataBuffer.IsValid)
            {
                DebugManager.Instance?.LogError(DEBUG_CLASS_NAME,
                    "Invalid metadataBuffer for stitch operation");
                return (null, new List<Rid>(), shaderPath);
            }
            if (!stitchedHeightmap.IsValid)
            {
                DebugManager.Instance?.LogError(DEBUG_CLASS_NAME,
                    "Invalid stitchedHeightmap for stitch operation");
                return (null, new List<Rid>(), shaderPath);
            }

            try
            {
                operation.BindSamplerWithTextureArray(0, heightmapArray);  // CombinedSampler at binding 0
                operation.BindStorageBuffer(1, metadataBuffer);             // StorageBuffer at binding 1  
                operation.BindStorageImage(2, stitchedHeightmap);           // Image at binding 2

                operation.SetPushConstants(new byte[0]);
                uint groupsX = (uint)((maskWidth + 7) / 8);
                uint groupsY = (uint)((maskHeight + 7) / 8);

                var dispatchCmd = operation.CreateDispatchCommands(groupsX, groupsY, (uint)regionCountInArray);

                return (dispatchCmd, operation.GetTemporaryRids(), shaderPath);
            }
            catch (Exception ex)
            {
                DebugManager.Instance?.LogError(DEBUG_CLASS_NAME,
                    $"Failed to create stitch heightmap commands: {ex.Message}");
                return (null, new List<Rid>(), shaderPath);
            }
        }

        private static (Action<long>, List<Rid>, string) CreateFalloffCommands(TerrainLayerBase layer, Rid layerTex, int maskWidth, int maskHeight)
        {
            if (layer.FalloffMode == FalloffType.None || layer.FalloffStrength <= 0.0f)
                return (null, new List<Rid>(), "");

            var shaderPath = "res://addons/terrain_3d_tools/Shaders/Masks/falloff.glsl";
            var operation = new AsyncComputeOperation(shaderPath);

            operation.BindStorageImage(0, layerTex);

            const int Resolution = 256;
            var curve = layer.FalloffCurve ?? new Curve();
            if (curve.PointCount == 0) { curve.AddPoint(new Vector2(0, 0)); curve.AddPoint(new Vector2(1, 1)); }
            curve.Bake();

            float[] curveValues = new float[Resolution];
            for (int i = 0; i < Resolution; i++)
            {
                float t = (float)i / (Resolution - 1);
                curveValues[i] = Mathf.Clamp(curve.SampleBaked(t), 0f, 1f);
            }

            int pointCount = Resolution;
            byte[] pointCountBytes = BitConverter.GetBytes(pointCount);
            byte[] valuesBytes = GpuUtils.FloatArrayToBytes(curveValues);
            byte[] bufferBytes = new byte[pointCountBytes.Length + valuesBytes.Length];
            Buffer.BlockCopy(pointCountBytes, 0, bufferBytes, 0, pointCountBytes.Length);
            Buffer.BlockCopy(valuesBytes, 0, bufferBytes, pointCountBytes.Length, valuesBytes.Length);

            operation.BindTemporaryStorageBuffer(1, bufferBytes);

            var pushConstants = GpuUtils.CreatePushConstants()
                .Add((int)layer.FalloffMode)
                .Add(layer.FalloffStrength)
                .Add((float)layer.Size.X)
                .Add((float)layer.Size.Y)
                .Build();
            operation.SetPushConstants(pushConstants);

            uint groupsX = (uint)((maskWidth + 7) / 8);
            uint groupsY = (uint)((maskHeight + 7) / 8);

            return (operation.CreateDispatchCommands(groupsX, groupsY), operation.GetTemporaryRids(), shaderPath);
        }

        private static (Action<long>, List<Rid>, string) CreateClearCommands(Rid targetTexture, int width, int height, Color clearColor)
        {
            if (!targetTexture.IsValid)
            {
                DebugManager.Instance?.LogError(DEBUG_CLASS_NAME,
                    "Cannot create clear commands for invalid texture");
                return (null, new List<Rid>(), "");
            }

            var shaderPath = "res://addons/terrain_3d_tools/Shaders/Utils/ClearRegion.glsl";
            var operation = new AsyncComputeOperation(shaderPath);

            operation.BindStorageImage(0, targetTexture);

            var pushConstants = GpuUtils.CreatePushConstants().Add(clearColor).Build();
            operation.SetPushConstants(pushConstants);

            uint groupsX = (uint)((width + 7) / 8);
            uint groupsY = (uint)((height + 7) / 8);

            return (operation.CreateDispatchCommands(groupsX, groupsY), operation.GetTemporaryRids(), shaderPath);
        }
    }
}
