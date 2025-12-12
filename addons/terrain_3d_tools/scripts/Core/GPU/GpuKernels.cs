// /Core/GPU/GpuKernels.cs
using System;
using System.Collections.Generic;
using Terrain3DTools.Core.Debug;
using Terrain3DTools.Layers;
using Godot;

namespace Terrain3DTools.Core
{
    /// <summary>
    /// Static library of reusable GPU compute operations (kernels).
    /// Provides both command-level and task-level utilities for common GPU operations.
    /// Does not register its own debug class - uses caller's debug context.
    /// </summary>
    public static class GpuKernels
    {
        #region Clear Operations

        /// <summary>
        /// Creates GPU commands to clear a texture to a specific color.
        /// </summary>
        /// <param name="targetTexture">The texture to clear</param>
        /// <param name="clearColor">The color to clear to</param>
        /// <param name="width">Texture width in pixels</param>
        /// <param name="height">Texture height in pixels</param>
        /// <param name="debugClassName">Name of the calling class for debug logging</param>
        /// <returns>Tuple of (GPU commands, temporary RIDs to free, shader path)</returns>
        public static (Action<long> commands, List<Rid> tempRids, string shaderPath) CreateClearCommands(
            Rid targetTexture, 
            Color clearColor, 
            int width, 
            int height,
            string debugClassName = null)
        {
            if (!targetTexture.IsValid)
            {
                if (debugClassName != null)
                {
                    DebugManager.Instance?.LogWarning(debugClassName, 
                        "Clear commands requested for invalid texture");
                }
                return (null, new List<Rid>(), "");
            }

            var shaderPath = "res://addons/terrain_3d_tools/Shaders/Utils/ClearRegion.glsl";
            var op = new AsyncComputeOperation(shaderPath);

            op.BindStorageImage(0, targetTexture);

            var pushConstants = GpuUtils.CreatePushConstants()
                .Add(clearColor)
                .Build();
            op.SetPushConstants(pushConstants);

            uint groupsX = (uint)((width + 7) / 8);
            uint groupsY = (uint)((height + 7) / 8);

            return (op.CreateDispatchCommands(groupsX, groupsY), op.GetTemporaryRids(), shaderPath);
        }

        /// <summary>
        /// Creates a complete AsyncGpuTask to clear a texture.
        /// Useful when you need a standalone clear operation with dependency management.
        /// </summary>
        /// <param name="targetTexture">The texture to clear</param>
        /// <param name="clearColor">The color to clear to</param>
        /// <param name="width">Texture width in pixels</param>
        /// <param name="height">Texture height in pixels</param>
        /// <param name="dependencies">Tasks that must complete before this clear operation</param>
        /// <param name="onComplete">Callback to invoke when clear completes</param>
        /// <param name="owner">Optional owner object for lifetime tracking</param>
        /// <param name="taskName">Descriptive name for the task</param>
        /// <returns>AsyncGpuTask or null if creation failed</returns>
        public static AsyncGpuTask CreateClearTask(
            Rid targetTexture,
            Color clearColor,
            int width,
            int height,
            List<AsyncGpuTask> dependencies = null,
            Action onComplete = null,
            object owner = null,
            string taskName = "Clear Texture")
        {
            var (cmd, rids, shader) = CreateClearCommands(targetTexture, clearColor, width, height);
            if (cmd == null) return null;

            var owners = owner != null ? new List<object> { owner } : new List<object>();

            return new AsyncGpuTask(
                cmd,
                onComplete,
                rids,
                owners,
                taskName,
                dependencies,
                new List<string> { shader });
        }

        #endregion

        #region Stitch Operations

        /// <summary>
        /// Creates GPU commands to stitch multiple region heightmaps into a single texture.
        /// Used for creating visualization textures from region data.
        /// </summary>
        /// <param name="targetTexture">The output texture to stitch into</param>
        /// <param name="heightmapArray">Texture array containing region heightmaps</param>
        /// <param name="metadataBuffer">Buffer containing stitch metadata (offsets, sizes, etc)</param>
        /// <param name="width">Output texture width</param>
        /// <param name="height">Output texture height</param>
        /// <param name="regionCount">Number of regions in the array</param>
        /// <param name="debugClassName">Name of the calling class for debug logging</param>
        /// <returns>Tuple of (GPU commands, temporary RIDs to free, shader path)</returns>
        public static (Action<long> commands, List<Rid> tempRids, string shaderPath) CreateStitchHeightmapCommands(
            Rid targetTexture,
            Rid heightmapArray,
            Rid metadataBuffer,
            int width,
            int height,
            int regionCount,
            string debugClassName = null)
        {
            var shaderPath = "res://addons/terrain_3d_tools/Shaders/Pipeline/stitch_heightmap.glsl";

            if (!targetTexture.IsValid || !heightmapArray.IsValid || !metadataBuffer.IsValid)
            {
                if (debugClassName != null)
                {
                    DebugManager.Instance?.LogError(debugClassName,
                        $"Invalid RIDs for stitch: target={targetTexture.IsValid}, array={heightmapArray.IsValid}, metadata={metadataBuffer.IsValid}");
                }
                return (null, new List<Rid>(), shaderPath);
            }

            var operation = new AsyncComputeOperation(shaderPath);

            try
            {
                operation.BindSamplerWithTextureArray(0, heightmapArray);
                operation.BindStorageBuffer(1, metadataBuffer);
                operation.BindStorageImage(2, targetTexture);
                operation.SetPushConstants(new byte[0]);

                uint groupsX = (uint)((width + 7) / 8);
                uint groupsY = (uint)((height + 7) / 8);

                return (
                    operation.CreateDispatchCommands(groupsX, groupsY, (uint)regionCount),
                    operation.GetTemporaryRids(),
                    shaderPath);
            }
            catch (Exception ex)
            {
                if (debugClassName != null)
                {
                    DebugManager.Instance?.LogError(debugClassName,
                        $"Failed to create stitch commands: {ex.Message}");
                }
                return (null, new List<Rid>(), shaderPath);
            }
        }

        /// <summary>
        /// Creates a complete visualization update task for a layer.
        /// Stages heightmaps from regions, clears the target, stitches them together, and updates the visualizer.
        /// This is a high-level convenience method that encapsulates the full visualization pipeline.
        /// </summary>
        /// <param name="layer">The layer to create visualization for</param>
        /// <param name="regionMapManager">Manager for accessing region data</param>
        /// <param name="regionSize">Size of each region in pixels</param>
        /// <param name="dependencies">Tasks that must complete before visualization (typically region composites)</param>
        /// <param name="debugClassName">Name of the calling class for debug logging</param>
        /// <returns>AsyncGpuTask or null if creation failed</returns>
        public static AsyncGpuTask CreateVisualizationTask(
            TerrainLayerBase layer,
            RegionMapManager regionMapManager,
            int regionSize,
            List<AsyncGpuTask> dependencies,
            string debugClassName = null)
        {
            if (!layer.layerHeightVisualizationTextureRID.IsValid)
            {
                if (debugClassName != null)
                {
                    DebugManager.Instance?.LogWarning(debugClassName,
                        $"Layer '{layer.LayerName}' has invalid visualization texture");
                }
                return null;
            }

            var (stagingTask, stagingResult) = HeightDataStager.StageHeightDataForLayerAsync(
                layer, regionMapManager, regionSize, dependencies);

            if (stagingTask == null || !stagingResult.IsValid)
            {
                if (debugClassName != null)
                {
                    DebugManager.Instance?.LogWarning(debugClassName,
                        $"Failed to stage height data for '{layer.LayerName}'");
                }
                return null;
            }

            AsyncGpuTaskManager.Instance.AddTask(stagingTask);

            var stitchDeps = new List<AsyncGpuTask> { stagingTask };
            
            Action onComplete = () =>
            {
                if (GodotObject.IsInstanceValid(layer))
                    layer.Visualizer?.Update();
            };

            var commands = new List<Action<long>>();
            var tempRids = new List<Rid>();
            var shaders = new List<string>();

            var (clearCmd, clearRids, clearShader) = CreateClearCommands(
                layer.layerHeightVisualizationTextureRID,
                Colors.Black,
                layer.Size.X,
                layer.Size.Y,
                debugClassName);

            if (clearCmd != null)
            {
                commands.Add(clearCmd);
                tempRids.AddRange(clearRids);
                shaders.Add(clearShader);
            }

            var (stitchCmd, stitchRids, stitchShader) = CreateStitchHeightmapCommands(
                layer.layerHeightVisualizationTextureRID,
                stagingResult.HeightmapArrayRid,
                stagingResult.MetadataBufferRid,
                layer.Size.X,
                layer.Size.Y,
                stagingResult.ActiveRegionCount,
                debugClassName);

            if (stitchCmd != null)
            {
                commands.Add(stitchCmd);
                tempRids.AddRange(stitchRids);
                shaders.Add(stitchShader);
            }

            tempRids.Add(stagingResult.HeightmapArrayRid);
            tempRids.Add(stagingResult.MetadataBufferRid);

            return new AsyncGpuTask(
                GpuCommandBuilder.CombineCommands(commands),
                onComplete,
                tempRids,
                new List<object>(),
                $"Visualization: {layer.LayerName}",
                stitchDeps,
                shaders);
        }

        #endregion

        #region Falloff Operations

        /// <summary>
        /// Creates GPU commands to apply falloff to a layer texture.
        /// Falloff attenuates the layer's influence based on distance from center using a curve.
        /// </summary>
        /// <param name="layer">The layer containing falloff parameters</param>
        /// <param name="layerTexture">The texture to apply falloff to (modified in place)</param>
        /// <param name="width">Texture width in pixels</param>
        /// <param name="height">Texture height in pixels</param>
        /// <returns>Tuple of (GPU commands, temporary RIDs to free, shader path)</returns>
        public static (Action<long> commands, List<Rid> tempRids, string shaderPath) CreateFalloffCommands(
            TerrainLayerBase layer,
            Rid layerTexture,
            int width,
            int height)
        {
            if (layer.FalloffMode == FalloffType.None || layer.FalloffStrength <= 0.0f)
                return (null, new List<Rid>(), "");

            var shaderPath = "res://addons/terrain_3d_tools/Shaders/Masks/falloff.glsl";
            var operation = new AsyncComputeOperation(shaderPath);

            operation.BindStorageImage(0, layerTexture);

            const int Resolution = 256;
            var curve = layer.FalloffCurve ?? new Curve();
            if (curve.PointCount == 0)
            {
                curve.AddPoint(new Vector2(0, 0));
                curve.AddPoint(new Vector2(1, 1));
            }
            curve.Bake();

            float[] curveValues = new float[Resolution];
            for (int i = 0; i < Resolution; i++)
            {
                float t = (float)i / (Resolution - 1);
                curveValues[i] = Mathf.Clamp(curve.SampleBaked(t), 0f, 1f);
            }

            byte[] bufferBytes = new byte[4 + curveValues.Length * 4];
            BitConverter.GetBytes(Resolution).CopyTo(bufferBytes, 0);
            Buffer.BlockCopy(GpuUtils.FloatArrayToBytes(curveValues), 0, bufferBytes, 4, curveValues.Length * 4);

            operation.BindTemporaryStorageBuffer(1, bufferBytes);

            var pushConstants = GpuUtils.CreatePushConstants()
                .Add((int)layer.FalloffMode)
                .Add(layer.FalloffStrength)
                .Add((float)layer.Size.X)
                .Add((float)layer.Size.Y)
                .Build();
            operation.SetPushConstants(pushConstants);

            uint groupsX = (uint)((width + 7) / 8);
            uint groupsY = (uint)((height + 7) / 8);

            return (
                operation.CreateDispatchCommands(groupsX, groupsY),
                operation.GetTemporaryRids(),
                shaderPath);
        }

        #endregion
    }
}