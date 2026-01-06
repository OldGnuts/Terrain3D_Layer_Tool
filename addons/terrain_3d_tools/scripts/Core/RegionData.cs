// /Core/RegionData.cs
using Godot;
using System.Collections.Generic;
using Terrain3DTools.Layers.Instancer;

namespace Terrain3DTools.Core
{
    /// <summary>
    /// Encapsulates all GPU resources for a single terrain region.
    /// </summary>
    public class RegionData
    {
        // Existing terrain data
        public Rid HeightMap { get; set; }
        public Rid ControlMap { get; set; }
        public Rid ColorMap { get; set; }

        // Exclusion map for instance placement (lazy-initialized)
        private Rid _exclusionMap;
        private bool _exclusionMapNeedsClearing = true;

        // Instance buffers keyed by layer instance ID
        private readonly Dictionary<ulong, InstanceBuffer> _instanceBuffers = new();

        #region Exclusion Map

        public bool HasExclusionMap => _exclusionMap.IsValid;
        public bool ExclusionMapNeedsClearing => _exclusionMapNeedsClearing;

        /// <summary>
        /// Gets or creates the exclusion map for this region.
        /// </summary>
        public Rid GetOrCreateExclusionMap(int regionSize)
        {
            if (!_exclusionMap.IsValid)
            {
                _exclusionMap = Gpu.CreateTexture2D(
                    (uint)regionSize, (uint)regionSize,
                    RenderingDevice.DataFormat.R32Sfloat,
                    RenderingDevice.TextureUsageBits.StorageBit |
                    RenderingDevice.TextureUsageBits.SamplingBit |
                    RenderingDevice.TextureUsageBits.CanCopyFromBit |
                    RenderingDevice.TextureUsageBits.CanUpdateBit);
                _exclusionMapNeedsClearing = true;
            }
            return _exclusionMap;
        }

        /// <summary>
        /// Marks the exclusion map as needing to be cleared before next use.
        /// </summary>
        public void MarkExclusionMapForClearing()
        {
            _exclusionMapNeedsClearing = true;
        }

        /// <summary>
        /// Marks the exclusion map as cleared.
        /// </summary>
        public void ClearExclusionMapFlag()
        {
            _exclusionMapNeedsClearing = false;
        }

        #endregion

        #region Instance Buffers

        /// <summary>
        /// Gets or creates an instance buffer for the specified layer.
        /// </summary>
        public InstanceBuffer GetOrCreateInstanceBuffer(ulong layerInstanceId, int maxInstances)
        {
            if (!_instanceBuffers.TryGetValue(layerInstanceId, out var buffer))
            {
                buffer = new InstanceBuffer(layerInstanceId, maxInstances);
                _instanceBuffers[layerInstanceId] = buffer;
            }
            else if (buffer.MaxInstances < maxInstances)
            {
                buffer.Resize(maxInstances);
            }
            return buffer;
        }

        /// <summary>
        /// Gets an existing instance buffer, or null if not present.
        /// </summary>
        public InstanceBuffer GetInstanceBuffer(ulong layerInstanceId)
        {
            _instanceBuffers.TryGetValue(layerInstanceId, out var buffer);
            return buffer;
        }

        /// <summary>
        /// Sets an instance buffer for a layer, replacing any existing one.
        /// </summary>
        public void SetInstanceBuffer(ulong layerInstanceId, InstanceBuffer buffer)
        {
            if (_instanceBuffers.TryGetValue(layerInstanceId, out var existingBuffer))
            {
                // Only free if it's a different buffer instance
                if (existingBuffer != buffer)
                {
                    existingBuffer.Free();
                }
            }
            _instanceBuffers[layerInstanceId] = buffer;
        }

        /// <summary>
        /// Removes and frees an instance buffer.
        /// </summary>
        public void RemoveInstanceBuffer(ulong layerInstanceId)
        {
            if (_instanceBuffers.TryGetValue(layerInstanceId, out var buffer))
            {
                buffer.Free();
                _instanceBuffers.Remove(layerInstanceId);
            }
        }

        /// <summary>
        /// Gets all instance buffers for this region.
        /// </summary>
        public IReadOnlyDictionary<ulong, InstanceBuffer> InstanceBuffers => _instanceBuffers;

        /// <summary>
        /// Gets all layer IDs that have instance buffers in this region.
        /// </summary>
        public IEnumerable<ulong> GetInstanceLayerIds() => _instanceBuffers.Keys;

        #endregion

        #region Cleanup

        public void FreeAll()
        {
            if (HeightMap.IsValid) Gpu.FreeRid(HeightMap);
            if (ControlMap.IsValid) Gpu.FreeRid(ControlMap);
            if (ColorMap.IsValid) Gpu.FreeRid(ColorMap);
            if (_exclusionMap.IsValid) Gpu.FreeRid(_exclusionMap);

            foreach (var buffer in _instanceBuffers.Values)
            {
                buffer.Free();
            }
            _instanceBuffers.Clear();

            HeightMap = new Rid();
            ControlMap = new Rid();
            ColorMap = new Rid();
            _exclusionMap = new Rid();
        }

        #endregion
    }
}