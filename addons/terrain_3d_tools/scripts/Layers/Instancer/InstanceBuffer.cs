// /Layers/Instancer/InstanceBuffer.cs
using Godot;
using System;
using System.Collections.Generic;
using Terrain3DTools.Core;
using Terrain3DTools.Core.Debug;

namespace Terrain3DTools.Layers.Instancer
{
    /// <summary>
    /// Manages GPU buffers for instance transform data.
    /// Each buffer holds transforms for all mesh types from one layer in one region.
    /// Format per instance: 12 floats (mat3x4) + 1 uint (mesh index packed as float) = 13 floats
    /// </summary>
    public class InstanceBuffer
    {
        private const string DEBUG_CLASS_NAME = "InstanceBuffer";
        private const int FLOATS_PER_INSTANCE = 13;
        private const int BYTES_PER_INSTANCE = FLOATS_PER_INSTANCE * sizeof(float);

        public ulong LayerInstanceId { get; }
        public int MaxInstances { get; private set; }

        public Rid TransformBuffer { get; private set; }
        public Rid CountBuffer { get; private set; }

        // Readback results
        public bool HasReadbackData { get; private set; }
        public float[] RawTransformData { get; private set; }
        public int InstanceCount { get; private set; }

        public InstanceBuffer(ulong layerInstanceId, int maxInstances)
        {
            LayerInstanceId = layerInstanceId;
            MaxInstances = maxInstances;
            Allocate();

            DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.GpuResources,
                $"Created InstanceBuffer for layer {layerInstanceId} with max {maxInstances} instances");
        }

        private void Allocate()
        {
            // Transform buffer: array of (mat3x4 + meshIndex) per instance
            uint bufferSize = (uint)(MaxInstances * BYTES_PER_INSTANCE);
            TransformBuffer = Gpu.Rd.StorageBufferCreate(bufferSize);

            // Count buffer: single uint atomic counter, initialized to 0
            CountBuffer = Gpu.Rd.StorageBufferCreate(sizeof(uint), new byte[sizeof(uint)]);

            HasReadbackData = false;
            RawTransformData = null;
            InstanceCount = 0;
        }

        public void Resize(int newMaxInstances)
        {
            if (newMaxInstances <= MaxInstances) return;

            DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.GpuResources,
                $"Resizing InstanceBuffer from {MaxInstances} to {newMaxInstances}");

            Free();
            MaxInstances = newMaxInstances;
            Allocate();
        }

        /// <summary>
        /// Resets the atomic counter to 0. Call before each placement dispatch.
        /// </summary>
        public void ResetCounter()
        {
            if (CountBuffer.IsValid)
            {
                Gpu.Rd.BufferUpdate(CountBuffer, 0, sizeof(uint), new byte[sizeof(uint)]);
            }
            HasReadbackData = false;
            InstanceCount = 0;
        }

        /// <summary>
        /// Reads back the transform data from GPU. Call after GPU work completes.
        /// </summary>
        public void Readback()
        {
            if (!CountBuffer.IsValid || !TransformBuffer.IsValid)
            {
                DebugManager.Instance?.LogWarning(DEBUG_CLASS_NAME,
                    "Cannot readback - buffers invalid");
                HasReadbackData = false;
                return;
            }

            // Read count
            byte[] countBytes = Gpu.Rd.BufferGetData(CountBuffer, 0, sizeof(uint));
            InstanceCount = (int)BitConverter.ToUInt32(countBytes, 0);
            InstanceCount = Math.Min(InstanceCount, MaxInstances);

            DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.GpuResources,
                $"Readback: {InstanceCount} instances (max {MaxInstances})");

            if (InstanceCount > 0)
            {
                // Read transform data
                uint dataSize = (uint)(InstanceCount * BYTES_PER_INSTANCE);
                byte[] transformBytes = Gpu.Rd.BufferGetData(TransformBuffer, 0, dataSize);
                RawTransformData = new float[InstanceCount * FLOATS_PER_INSTANCE];
                Buffer.BlockCopy(transformBytes, 0, RawTransformData, 0, (int)dataSize);
            }
            else
            {
                RawTransformData = Array.Empty<float>();
            }

            HasReadbackData = true;
        }

        /// <summary>
        /// Extracts Transform3D array for a specific mesh index from the raw data.
        /// </summary>
        public Transform3D[] GetTransformsForMesh(int meshIndex)
        {
            if (!HasReadbackData || RawTransformData == null || RawTransformData.Length == 0)
                return Array.Empty<Transform3D>();

            var transforms = new List<Transform3D>();

            for (int i = 0; i < InstanceCount; i++)
            {
                int offset = i * FLOATS_PER_INSTANCE;

                // Mesh index is stored as uint bits in a float at offset + 12
                uint storedMeshIndex = BitConverter.ToUInt32(
                    BitConverter.GetBytes(RawTransformData[offset + 12]), 0);

                if ((int)storedMeshIndex == meshIndex)
                {
                    // Reconstruct Transform3D from mat3x4 (column-major)
                    // Godot Transform3D: Basis (3 Vector3 columns) + Origin (Vector3)
                    var transform = new Transform3D(
                        new Basis(
                            new Vector3(RawTransformData[offset + 0], RawTransformData[offset + 1], RawTransformData[offset + 2]),
                            new Vector3(RawTransformData[offset + 4], RawTransformData[offset + 5], RawTransformData[offset + 6]),
                            new Vector3(RawTransformData[offset + 8], RawTransformData[offset + 9], RawTransformData[offset + 10])
                        ),
                        new Vector3(RawTransformData[offset + 3], RawTransformData[offset + 7], RawTransformData[offset + 11])
                    );
                    transforms.Add(transform);
                }
            }

            return transforms.ToArray();
        }

        /// <summary>
        /// Gets all unique mesh indices present in the buffer.
        /// </summary>
        public HashSet<int> GetPresentMeshIndices()
        {
            var indices = new HashSet<int>();
            if (!HasReadbackData || RawTransformData == null) return indices;

            for (int i = 0; i < InstanceCount; i++)
            {
                int offset = i * FLOATS_PER_INSTANCE;
                uint meshIndex = BitConverter.ToUInt32(
                    BitConverter.GetBytes(RawTransformData[offset + 12]), 0);
                indices.Add((int)meshIndex);
            }

            return indices;
        }

        public void Free()
        {
            // Queue for deferred cleanup instead of immediate free
            // This prevents double-free and ensures GPU is done with the buffers
            if (TransformBuffer.IsValid)
            {
                AsyncGpuTaskManager.Instance?.QueueCleanup(TransformBuffer);
                TransformBuffer = new Rid();
            }
            if (CountBuffer.IsValid)
            {
                AsyncGpuTaskManager.Instance?.QueueCleanup(CountBuffer);
                CountBuffer = new Rid();
            }
            RawTransformData = null;
            HasReadbackData = false;
            InstanceCount = 0;

            DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.GpuResources,
                $"Queued InstanceBuffer for cleanup, layer {LayerInstanceId}");
        }
    }
}