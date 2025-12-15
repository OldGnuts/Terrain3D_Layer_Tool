// /Core/GPU/Gpu.cs
using Godot;
using System;
using System.Diagnostics;

namespace Terrain3DTools.Core
{
    /// <summary>
    /// A lean, stateless, low-level static class that serves as a direct interface
    /// to Godot's RenderingDevice. Its sole responsibility is to create GPU resources
    /// and execute compute commands.
    ///
    /// This class performs NO caching and holds NO state beyond the RenderingDevice instance.
    /// It is the foundational "engine room" of the GPU interaction layer.
    /// </summary>
    public static class Gpu
    {
        private static readonly RenderingDevice _renderingDevice;
        public static RenderingDevice Rd => _renderingDevice;

        static Gpu()
        {
            _renderingDevice = RenderingServer.CreateLocalRenderingDevice();
            System.Diagnostics.Debug.Assert(_renderingDevice != null, "FATAL: Failed to get or create a RenderingDevice.");
        }

        #region Resource Creation

        /// <summary>
        /// Creates a shader resource from a compiled SPIR-V object.
        /// </summary>
        public static Rid CreateShaderFromSpirV(RDShaderSpirV spirV)
        {
            return _renderingDevice.ShaderCreateFromSpirV(spirV);
        }

        /// <summary>
        /// Creates a compute pipeline for a given shader.
        /// </summary>
        public static Rid CreateComputePipeline(Rid shaderRid)
        {
            return _renderingDevice.ComputePipelineCreate(shaderRid);
        }

        /// <summary>
        /// Creates a texture resource on the GPU based on the specified format and view.
        /// </summary>
        public static Rid CreateTexture(RDTextureFormat format, RDTextureView view)
        {
            return _renderingDevice.TextureCreate(format, view);
        }

        /// <summary>
        /// Convenience overload for creating a 2D texture.
        /// </summary>
        public static Rid CreateTexture2D(uint width, uint height, RenderingDevice.DataFormat format, RenderingDevice.TextureUsageBits usage)
        {
            var rdFormat = new RDTextureFormat
            {
                Width = width,
                Height = height,
                Format = format,
                UsageBits = usage,
                TextureType = RenderingDevice.TextureType.Type2D,
            };
            return _renderingDevice.TextureCreate(rdFormat, new RDTextureView());
        }

        public static Rid CreateTexture2DArray(uint width, uint height, uint layers, RenderingDevice.DataFormat format, RenderingDevice.TextureUsageBits usage)
        {
            var rdFormat = new RDTextureFormat
            {
                Width = width,
                Height = height,
                Depth = 1,      // Depth is 1 for 2D textures
                ArrayLayers = layers, // Number of slices in the array
                Format = format,
                UsageBits = usage,
                TextureType = RenderingDevice.TextureType.Type2DArray, // The key difference
            };
            return _renderingDevice.TextureCreate(rdFormat, new RDTextureView());
        }


        /// <summary>
        /// Creates a sampler resource for sampling textures in a shader.
        /// </summary>
        public static Rid CreateSampler(RDSamplerState samplerState)
        {
            return _renderingDevice.SamplerCreate(samplerState);
        }

        /// <summary>
        /// Creates a uniform set for binding resources to a shader.
        /// </summary>
        public static Rid CreateUniformSet(Godot.Collections.Array<RDUniform> uniforms, Rid shaderRid, uint setIndex = 0)
        {
            return _renderingDevice.UniformSetCreate(uniforms, shaderRid, setIndex);
        }

        /// <summary>
        /// Creates a storage buffer on the GPU.
        /// </summary>
        /// <param name="sizeInBytes">The size of the buffer in bytes.</param>
        /// <param name="data">Optional initial data to upload to the buffer.</param>
        public static Rid CreateStorageBuffer(uint sizeInBytes, byte[] data = null)
        {
            Rid buffer = _renderingDevice.StorageBufferCreate(sizeInBytes, data);
            return buffer;
        }

        public static Rid CreateStorageBuffer<T>(T[] data) where T : struct
        {
            if (data == null || data.Length == 0)
            {
                GD.PrintErr("[Gpu.cs] Cannot create storage buffer from null or empty array.");
                return new Rid();
            }
            byte[] byteArray = GpuUtils.StructArrayToBytes(data);

            if (byteArray.Length == 0)
            {
                GD.PrintErr("[Gpu.cs] Failed to convert struct array to bytes, resulting in a zero-length buffer.");
                return new Rid();
            }

            return _renderingDevice.StorageBufferCreate((uint)byteArray.Length, byteArray);
        }
        #endregion

        #region Data Transfer
        /// <summary>
        /// Updates a region of a texture with new data from the CPU.
        /// </summary>
        public static void TextureUpdate(Rid texture, uint layer, byte[] data)
        {
            if (texture.IsValid && data != null && data.Length > 0)
            {
                _renderingDevice.TextureUpdate(texture, layer, data);
            }
        }

        /// <summary>
        /// Retrieves the raw byte data of a texture from the GPU. This is a blocking operation.
        /// </summary>
        public static byte[] TextureGetData(Rid texture, uint layer)
        {
            return _renderingDevice.TextureGetData(texture, layer);
        }

        /// <summary>
        /// Updates a storage buffer with new data from the CPU.
        /// </summary>
        public static void BufferUpdate(Rid buffer, uint offset, byte[] data)
        {
            if (buffer.IsValid && data != null && data.Length > 0)
            {
                _renderingDevice.BufferUpdate(buffer, offset, (uint)data.Length, data);
            }
        }

        /// <summary>
        /// Adds a command to an existing compute list to copy one 2D texture to another.
        /// This is a high-performance, GPU-native operation.
        /// </summary>
        public static void AddCopyTextureCommand(long computeList, Rid sourceTexture, Rid destinationTexture, uint width, uint height)
        {
            if (!sourceTexture.IsValid || !destinationTexture.IsValid)
            {
                GD.PrintErr("[Gpu.cs] Invalid RID provided for texture copy command.");
                return;
            }

            _renderingDevice.ComputeListAddBarrier(computeList);

            var fromPos = new Vector3(0, 0, 0);
            var toPos = new Vector3(0, 0, 0);
            var size = new Vector3(width, height, 1);

            // Note: In Godot 4.3+, TextureCopy is the direct method.
            // This is the correct way to add a transfer operation to a compute list.
            _renderingDevice.TextureCopy(
                fromTexture: sourceTexture,
                toTexture: destinationTexture,
                fromPos: fromPos,
                toPos: toPos,
                size: size,
                srcMipmap: 0,
                dstMipmap: 0,
                srcLayer: 0,
                dstLayer: 0
            );

            _renderingDevice.ComputeListAddBarrier(computeList);
        }

        /// <summary>
        /// Adds a command to an existing compute list to copy a 2D texture into one slice of a texture array.
        /// This function does NOT submit or sync; it only queues the command.
        /// </summary>
        /// <param name="computeList">The active compute list to add the command to.</param>
        /// <param name="sourceTexture">The texture to copy from.</param>
        /// <param name="destinationArray">The texture array to copy to.</param>
        /// <param name="sliceIndex">The destination slice/layer within the array.</param>
        /// <param name="width">The width of the texture.</param>
        /// <param name="height">The height of the texture.</param>
        public static void AddCopyTextureToArraySliceCommand(long computeList, Rid sourceTexture, Rid destinationArray, int sliceIndex, uint width, uint height)
        {
            if (!sourceTexture.IsValid || !destinationArray.IsValid)
            {
                GD.PrintErr("[Gpu.cs] Invalid RID provided for texture copy command.");
                return;
            }

            // This function no longer begins, ends, submits, or syncs.
            // It just adds the core copy command to the provided list.
            _renderingDevice.ComputeListAddBarrier(computeList);

            var fromPos = new Vector3(0, 0, 0);
            var toPos = new Vector3(0, 0, 0);
            var size = new Vector3(width, height, 1);
            
            _renderingDevice.TextureCopy(
                fromTexture: sourceTexture,
                toTexture: destinationArray,
                fromPos: fromPos,
                toPos: toPos,
                size: size,
                srcMipmap: 0,
                dstMipmap: 0,
                srcLayer: 0,
                dstLayer: (uint)sliceIndex
            );

            _renderingDevice.ComputeListAddBarrier(computeList);
        }

        public static void TextureUpdate(Rid texture, uint layer, byte[] data, Rect2I rect)
        {
            if (texture.IsValid && data != null && data.Length > 0)
            {
                // Note: Godot 4.3+ has an overload with Rect2I, which is more efficient.
                // If using an older version, you might need to use the version without the rect.
                _renderingDevice.TextureUpdate(texture, layer, data);
            }
        }
        #endregion

        #region Execution
        /// <summary>
        /// Dispatches a compute operation asynchronously.
        /// </summary>
        /// <param name="pipeline">The compute pipeline to execute.</param>
        /// <param name="uniformSet">The uniform set with bound resources.</param>
        /// <param name="pushConstants">The push constant data, if any.</param>
        /// <param name="xGroups">Number of workgroups in the X dimension.</param>
        /// <param name="yGroups">Number of workgroups in the Y dimension.</param>
        /// <param name="zGroups">Number of workgroups in the Z dimension.</param>
        public static void Dispatch(Rid pipeline, Rid uniformSet, byte[] pushConstants, uint xGroups, uint yGroups, uint zGroups)
        {
            long computeList = _renderingDevice.ComputeListBegin();
            _renderingDevice.ComputeListBindComputePipeline(computeList, pipeline);
            if (pushConstants != null && pushConstants.Length > 0)
            {
                _renderingDevice.ComputeListSetPushConstant(computeList, pushConstants, (uint)pushConstants.Length);
            }
            if (uniformSet.IsValid)
            {
                _renderingDevice.ComputeListBindUniformSet(computeList, uniformSet, 0);
            }
            _renderingDevice.ComputeListDispatch(computeList, xGroups, yGroups, zGroups);
            _renderingDevice.ComputeListEnd();

            _renderingDevice.Submit();
        }

        /// <summary>
        /// Dispatches a compute operation and blocks the thread until it completes.
        /// </summary>
        public static void DispatchAndWait(Rid pipeline, Rid uniformSet, byte[] pushConstants, uint xGroups, uint yGroups, uint zGroups)
        {
            Dispatch(pipeline, uniformSet, pushConstants, xGroups, yGroups, zGroups);
            _renderingDevice.Sync();
        }

        public static void AddDispatchToComputeList_WithLog(long computeList, Rid pipeline, string shaderPath, Rid uniformSet, byte[] pushConstants, uint xGroups, uint yGroups, uint zGroups)
        {
            // This log will be our ground truth.
            GD.Print($"[GPU_DISPATCH] Binding pipeline {pipeline.Id} for shader '{shaderPath}'");

            // Now call the original method
            AddDispatchToComputeList(computeList, pipeline, uniformSet, pushConstants, xGroups, yGroups, zGroups);
        }

        /// <summary>
        /// Async method to build a command list for later execution
        /// </summary>
        /// <param name="computeList"></param>
        /// <param name="pipeline"></param>
        /// <param name="uniformSet"></param>
        /// <param name="pushConstants"></param>
        /// <param name="xGroups"></param>
        /// <param name="yGroups"></param>
        /// <param name="zGroups"></param>
        public static void AddDispatchToComputeList(long computeList, Rid pipeline, Rid uniformSet, byte[] pushConstants, uint xGroups, uint yGroups, uint zGroups, uint setIndex = 0)
        {
            Rd.ComputeListBindComputePipeline(computeList, pipeline);
            
            // Always set the push constant. If the array is null or empty, this effectively
            // clears the push constant for the current dispatch, preventing state from
            // leaking from previous dispatches in the same command list.
            Rd.ComputeListSetPushConstant(computeList, pushConstants ?? new byte[0], (uint)(pushConstants?.Length ?? 0));

            if (uniformSet.IsValid)
            {
                Rd.ComputeListBindUniformSet(computeList, uniformSet, setIndex);
            }
            else
            {
                GD.PrintErr("FATAL: Attempted to dispatch with a NULL uniform set. This should not happen.");
                return; // Abort this dispatch
            }

            Rd.ComputeListDispatch(computeList, xGroups, yGroups, zGroups);
        }

        #endregion

        #region Lifecycle
        /// <summary>
        /// Frees any RenderingDevice resource identified by its RID.
        /// </summary>
        public static void FreeRid(Rid rid)
        {
            if (rid.IsValid)
            {
                _renderingDevice.FreeRid(rid);
            }
        }


        // Expose the core RenderingDevice methods for batch control.
        public static long ComputeListBegin() => _renderingDevice.ComputeListBegin();
        public static void ComputeListEnd() => _renderingDevice.ComputeListEnd();
        public static void Submit() => _renderingDevice.Submit();
        /// <summary>
        /// Submits all queued commands to the GPU and blocks until they are finished.
        /// Essential for synchronizing CPU and GPU state.
        /// </summary>
        public static void Sync() => _renderingDevice.Sync();
        #endregion
    }
}