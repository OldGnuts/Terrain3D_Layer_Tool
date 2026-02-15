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
        /// Convenience overload for creating a 2D texture with optional initial data.
        /// </summary>
        /// <param name="width">Texture width in pixels</param>
        /// <param name="height">Texture height in pixels</param>
        /// <param name="format">The data format for the texture</param>
        /// <param name="usage">Usage flags for the texture</param>
        /// <param name="initialData">Optional initial data to populate the texture. 
        /// If null, texture is created uninitialized. If provided, must match texture size.</param>
        public static Rid CreateTexture2D(uint width, uint height,
            RenderingDevice.DataFormat format,
            RenderingDevice.TextureUsageBits usage,
            byte[] initialData = null)
        {
            var rdFormat = new RDTextureFormat
            {
                Width = width,
                Height = height,
                Format = format,
                UsageBits = usage,
                TextureType = RenderingDevice.TextureType.Type2D,
            };

            Godot.Collections.Array<byte[]> data = null;
            if (initialData != null)
            {
                data = new Godot.Collections.Array<byte[]> { initialData };
            }

            return _renderingDevice.TextureCreate(rdFormat, new RDTextureView(), data);
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
        #endregion

        #region Execution
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