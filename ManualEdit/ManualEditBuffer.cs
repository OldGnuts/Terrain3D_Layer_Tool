// /Layers/ManualEdit/ManualEditBuffer.cs

using Godot;
using System;
using System.Collections.Generic;
using Terrain3DTools.Core;
using Terrain3DTools.Core.Debug;

namespace Terrain3DTools.Layers.ManualEdit
{
    /// <summary>
    /// Stores manual edit data for a single region.
    /// Each ManualEditLayer can have one buffer per region it has edited.
    /// 
    /// Implements shadow copy pattern for stroke cancellation:
    /// - Working copy: actively modified during brush strokes
    /// - Committed copy: state at last stroke completion
    /// </summary>
    public class ManualEditBuffer
    {
        private const string DEBUG_CLASS_NAME = "ManualEditBuffer";

        #region Constants - Intent-Based Texture Edit Format (Separate Overlay/Base)

        // Intent format bit layout - supports concurrent overlay and base edits
        // Bits 0-4:   Desired overlay ID (0-31)
        // Bit 5:      Overlay edit active
        // Bits 6-13:  Overlay weight (0-255)
        // Bits 14-18: Desired base ID (0-31)
        // Bit 19:     Base edit active
        // Bits 20-27: Base weight (0-255)
        // Bits 28-31: Reserved

        public const int OVERLAY_ID_SHIFT = 0;
        public const uint OVERLAY_ID_MASK = 0x1Fu;
        public const int OVERLAY_ACTIVE_SHIFT = 5;
        public const int OVERLAY_WEIGHT_SHIFT = 6;
        public const uint OVERLAY_WEIGHT_MASK = 0xFFu;
        public const int BASE_ID_SHIFT = 14;
        public const uint BASE_ID_MASK = 0x1Fu;
        public const int BASE_ACTIVE_SHIFT = 19;
        public const int BASE_WEIGHT_SHIFT = 20;
        public const uint BASE_WEIGHT_MASK = 0xFFu;

        #endregion

        #region Properties

        /// <summary>
        /// Height delta texture (working copy). R32F format, values from -1 to +1.
        /// Applied additively to composited height.
        /// </summary>
        public Rid HeightDelta { get; private set; }

        /// <summary>
        /// Texture edit intent data (working copy). R32F storing packed uint32.
        /// Stores user INTENT (target texture, mode, weight) not computed results.
        /// </summary>
        public Rid TextureEdit { get; private set; }

        /// <summary>
        /// Instance exclusion map (working copy). R32F format, 0 = allow procedural, 1 = exclude.
        /// Used to block procedural instance placement.
        /// </summary>
        public Rid InstanceExclusion { get; private set; }

        /// <summary>
        /// Manually placed instances, keyed by mesh asset ID.
        /// These are aggregated with procedural instances during push.
        /// </summary>
        public Dictionary<int, List<Transform3D>> PlacedInstances { get; } = new();

        /// <summary>
        /// Tracks which data types have been modified.
        /// Used for conditional GPU resource allocation.
        /// </summary>
        public ManualEditFlags ActiveEdits { get; private set; } = ManualEditFlags.None;

        /// <summary>
        /// The region size this buffer was created for.
        /// </summary>
        public int RegionSize { get; private set; }

        /// <summary>
        /// Returns true if any GPU resources are allocated.
        /// </summary>
        public bool HasAllocatedResources =>
            HeightDelta.IsValid || TextureEdit.IsValid || InstanceExclusion.IsValid;

        #endregion

        #region Shadow Copy Fields (Private)

        private Rid _heightDeltaCommitted;
        private Rid _textureEditCommitted;
        private Rid _instanceExclusionCommitted;

        #endregion

        #region Initialization

        public ManualEditBuffer(int regionSize)
        {
            RegionSize = regionSize;
            DebugManager.Instance?.RegisterClass(DEBUG_CLASS_NAME);
        }

        #endregion

        #region Lazy Resource Allocation

        /// <summary>
        /// Gets or creates the height delta texture.
        /// Lazy allocation - only created when first height edit occurs.
        /// Also creates the shadow copy for undo support.
        /// </summary>
        public Rid GetOrCreateHeightDelta()
        {
            if (!HeightDelta.IsValid)
            {
                // Create with zeroed initial data - avoids TextureUpdate during compute lists
                HeightDelta = CreateR32FTexture(0f);
                _heightDeltaCommitted = CreateR32FTexture(0f);

                ActiveEdits |= ManualEditFlags.Height;

                DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.GpuResources,
                    $"Created HeightDelta textures (size: {RegionSize})");
            }
            return HeightDelta;
        }

        /// <summary>
        /// Gets or creates the texture edit intent map.
        /// Lazy allocation - only created when first texture edit occurs.
        /// </summary>
        public Rid GetOrCreateTextureEdit()
        {
            if (!TextureEdit.IsValid)
            {
                // Create with zeroed initial data (no active intent)
                TextureEdit = CreateR32FTexture(0f);
                _textureEditCommitted = CreateR32FTexture(0f);

                ActiveEdits |= ManualEditFlags.Texture;

                DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.GpuResources,
                    $"Created TextureEdit textures (size: {RegionSize})");
            }
            return TextureEdit;
        }

        /// <summary>
        /// Gets or creates the instance exclusion map.
        /// Lazy allocation - only created when first exclusion edit occurs.
        /// </summary>
        public Rid GetOrCreateInstanceExclusion()
        {
            if (!InstanceExclusion.IsValid)
            {
                // Create with zeroed initial data (allow all procedural instances)
                InstanceExclusion = CreateR32FTexture(0f);
                _instanceExclusionCommitted = CreateR32FTexture(0f);

                ActiveEdits |= ManualEditFlags.InstanceExclusion;

                DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.GpuResources,
                    $"Created InstanceExclusion textures (size: {RegionSize})");
            }
            return InstanceExclusion;
        }

        /// <summary>
        /// Creates an R32F texture with initial data, avoiding the need for TextureUpdate.
        /// </summary>
        private Rid CreateR32FTexture(float initialValue = 0f)
        {
            int pixelCount = RegionSize * RegionSize;
            byte[] initialData = GpuUtils.CreateFilledFloatBuffer(pixelCount, initialValue);

            return Gpu.CreateTexture2D(
                (uint)RegionSize, (uint)RegionSize,
                RenderingDevice.DataFormat.R32Sfloat,
                RenderingDevice.TextureUsageBits.StorageBit |
                RenderingDevice.TextureUsageBits.SamplingBit |
                RenderingDevice.TextureUsageBits.CanCopyFromBit |
                RenderingDevice.TextureUsageBits.CanCopyToBit |
                RenderingDevice.TextureUsageBits.CanUpdateBit,
                initialData);
        }

        #endregion

        #region Shadow Copy Operations - Height

        /// <summary>
        /// Commits current height delta state to shadow copy.
        /// Call at stroke end after successful completion.
        /// </summary>
        public void CommitHeightDelta()
        {
            if (HeightDelta.IsValid && _heightDeltaCommitted.IsValid)
            {
                CopyTexture(HeightDelta, _heightDeltaCommitted);
                DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.UpdateCycle,
                    "Committed HeightDelta");
            }
        }

        /// <summary>
        /// Reverts height delta working state from shadow copy.
        /// Call on stroke cancel.
        /// </summary>
        public void RevertHeightDelta()
        {
            if (HeightDelta.IsValid && _heightDeltaCommitted.IsValid)
            {
                CopyTexture(_heightDeltaCommitted, HeightDelta);
                DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.UpdateCycle,
                    "Reverted HeightDelta");
            }
        }

        /// <summary>
        /// Reads a subrect from the committed (before-stroke) height state.
        /// </summary>
        public float[] ReadCommittedHeightSubrect(Rect2I bounds)
        {
            if (!_heightDeltaCommitted.IsValid)
            {
                // First stroke in this region - committed state is all zeros
                return new float[bounds.Size.X * bounds.Size.Y];
            }
            return ReadFloatSubrect(_heightDeltaCommitted, bounds);
        }

        /// <summary>
        /// Reads a subrect from the working (current) height state.
        /// </summary>
        public float[] ReadWorkingHeightSubrect(Rect2I bounds)
        {
            if (!HeightDelta.IsValid)
                return null;
            return ReadFloatSubrect(HeightDelta, bounds);
        }

        /// <summary>
        /// Writes a subrect to the working height state.
        /// Used for undo/redo.
        /// </summary>
        public void WriteWorkingHeightSubrect(Rect2I bounds, float[] data)
        {
            if (!HeightDelta.IsValid || data == null)
                return;
            WriteFloatSubrect(HeightDelta, bounds, data);
        }

        #endregion

        #region Shadow Copy Operations - Texture Intent

        /// <summary>
        /// Commits current texture edit intent state to shadow copy.
        /// </summary>
        public void CommitTextureEdit()
        {
            if (TextureEdit.IsValid && _textureEditCommitted.IsValid)
            {
                CopyTexture(TextureEdit, _textureEditCommitted);
                DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.UpdateCycle,
                    "Committed TextureEdit");
            }
        }

        /// <summary>
        /// Reverts texture edit intent working state from shadow copy.
        /// </summary>
        public void RevertTextureEdit()
        {
            if (TextureEdit.IsValid && _textureEditCommitted.IsValid)
            {
                CopyTexture(_textureEditCommitted, TextureEdit);
                DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.UpdateCycle,
                    "Reverted TextureEdit");
            }
        }

        /// <summary>
        /// Reads a subrect from the committed texture intent state.
        /// </summary>
        public uint[] ReadCommittedTextureSubrect(Rect2I bounds)
        {
            if (!_textureEditCommitted.IsValid)
            {
                return new uint[bounds.Size.X * bounds.Size.Y];
            }
            return ReadUintSubrect(_textureEditCommitted, bounds);
        }

        /// <summary>
        /// Reads a subrect from the working texture intent state.
        /// </summary>
        public uint[] ReadWorkingTextureSubrect(Rect2I bounds)
        {
            if (!TextureEdit.IsValid)
                return null;
            return ReadUintSubrect(TextureEdit, bounds);
        }

        /// <summary>
        /// Writes a subrect to the working texture intent state.
        /// </summary>
        public void WriteWorkingTextureSubrect(Rect2I bounds, uint[] data)
        {
            if (!TextureEdit.IsValid || data == null)
                return;
            WriteUintSubrect(TextureEdit, bounds, data);
        }

        #endregion

        #region Shadow Copy Operations - Exclusion

        /// <summary>
        /// Commits current exclusion state to shadow copy.
        /// </summary>
        public void CommitInstanceExclusion()
        {
            if (InstanceExclusion.IsValid && _instanceExclusionCommitted.IsValid)
            {
                CopyTexture(InstanceExclusion, _instanceExclusionCommitted);
                DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.UpdateCycle,
                    "Committed InstanceExclusion");
            }
        }

        /// <summary>
        /// Reverts exclusion working state from shadow copy.
        /// </summary>
        public void RevertInstanceExclusion()
        {
            if (InstanceExclusion.IsValid && _instanceExclusionCommitted.IsValid)
            {
                CopyTexture(_instanceExclusionCommitted, InstanceExclusion);
                DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.UpdateCycle,
                    "Reverted InstanceExclusion");
            }
        }

        /// <summary>
        /// Reads a subrect from the committed exclusion state.
        /// </summary>
        public float[] ReadCommittedExclusionSubrect(Rect2I bounds)
        {
            if (!_instanceExclusionCommitted.IsValid)
            {
                return new float[bounds.Size.X * bounds.Size.Y];
            }
            return ReadFloatSubrect(_instanceExclusionCommitted, bounds);
        }

        /// <summary>
        /// Reads a subrect from the working exclusion state.
        /// </summary>
        public float[] ReadWorkingExclusionSubrect(Rect2I bounds)
        {
            if (!InstanceExclusion.IsValid)
                return null;
            return ReadFloatSubrect(InstanceExclusion, bounds);
        }

        /// <summary>
        /// Writes a subrect to the working exclusion state.
        /// </summary>
        public void WriteWorkingExclusionSubrect(Rect2I bounds, float[] data)
        {
            if (!InstanceExclusion.IsValid || data == null)
                return;
            WriteFloatSubrect(InstanceExclusion, bounds, data);
        }

        #endregion

        #region Instance Management

        /// <summary>
        /// Adds a manually placed instance.
        /// </summary>
        public void AddInstance(int meshId, Transform3D transform)
        {
            if (!PlacedInstances.ContainsKey(meshId))
            {
                PlacedInstances[meshId] = new List<Transform3D>();
            }
            PlacedInstances[meshId].Add(transform);
            ActiveEdits |= ManualEditFlags.InstancePlacement;
        }

        /// <summary>
        /// Removes a manually placed instance by index.
        /// Returns true if removed successfully.
        /// </summary>
        public bool RemoveInstance(int meshId, int index)
        {
            if (PlacedInstances.TryGetValue(meshId, out var list) && index >= 0 && index < list.Count)
            {
                list.RemoveAt(index);
                if (list.Count == 0)
                {
                    PlacedInstances.Remove(meshId);
                }
                return true;
            }
            return false;
        }

        /// <summary>
        /// Removes the closest instance to a world position within a threshold.
        /// Returns the removed transform, or null if none found.
        /// </summary>
        public Transform3D? RemoveClosestInstance(int meshId, Vector3 worldPos, float threshold)
        {
            if (!PlacedInstances.TryGetValue(meshId, out var list) || list.Count == 0)
                return null;

            int closestIdx = -1;
            float closestDist = threshold;

            for (int i = 0; i < list.Count; i++)
            {
                float dist = list[i].Origin.DistanceTo(worldPos);
                if (dist < closestDist)
                {
                    closestDist = dist;
                    closestIdx = i;
                }
            }

            if (closestIdx >= 0)
            {
                var removed = list[closestIdx];
                list.RemoveAt(closestIdx);
                if (list.Count == 0)
                {
                    PlacedInstances.Remove(meshId);
                }
                return removed;
            }

            return null;
        }

        /// <summary>
        /// Gets all manually placed instances for a mesh ID.
        /// </summary>
        public IReadOnlyList<Transform3D> GetInstances(int meshId)
        {
            if (PlacedInstances.TryGetValue(meshId, out var list))
                return list;
            return Array.Empty<Transform3D>();
        }

        #endregion

        #region Subrect Operations

        private float[] ReadFloatSubrect(Rid texture, Rect2I bounds)
        {
            // Ensure any pending GPU work is complete before reading
            AsyncGpuTaskManager.Instance?.SyncIfNeeded();

            byte[] fullData = Gpu.Rd.TextureGetData(texture, 0);
            float[] fullFloats = GpuUtils.BytesToFloatArray(fullData);

            int width = bounds.Size.X;
            int height = bounds.Size.Y;
            float[] result = new float[width * height];

            for (int y = 0; y < height; y++)
            {
                int srcIndex = (bounds.Position.Y + y) * RegionSize + bounds.Position.X;
                int dstIndex = y * width;

                if (srcIndex >= 0 && srcIndex + width <= fullFloats.Length)
                {
                    Array.Copy(fullFloats, srcIndex, result, dstIndex, width);
                }
            }

            return result;
        }

        private void WriteFloatSubrect(Rid texture, Rect2I bounds, float[] subrectData)
        {
            // Ensure any pending GPU work is complete before modifying
            AsyncGpuTaskManager.Instance?.SyncIfNeeded();

            byte[] fullData = Gpu.Rd.TextureGetData(texture, 0);
            float[] fullFloats = GpuUtils.BytesToFloatArray(fullData);

            int width = bounds.Size.X;
            int height = bounds.Size.Y;

            for (int y = 0; y < height; y++)
            {
                int dstIndex = (bounds.Position.Y + y) * RegionSize + bounds.Position.X;
                int srcIndex = y * width;

                if (dstIndex >= 0 && dstIndex + width <= fullFloats.Length &&
                    srcIndex + width <= subrectData.Length)
                {
                    Array.Copy(subrectData, srcIndex, fullFloats, dstIndex, width);
                }
            }

            byte[] newBytes = GpuUtils.FloatArrayToBytes(fullFloats);
            Gpu.Rd.TextureUpdate(texture, 0, newBytes);
        }

        private uint[] ReadUintSubrect(Rid texture, Rect2I bounds)
        {
            // Ensure any pending GPU work is complete before reading
            AsyncGpuTaskManager.Instance?.SyncIfNeeded();

            byte[] fullData = Gpu.Rd.TextureGetData(texture, 0);
            uint[] fullUints = GpuUtils.BytesToUIntArray(fullData);

            int width = bounds.Size.X;
            int height = bounds.Size.Y;
            uint[] result = new uint[width * height];

            for (int y = 0; y < height; y++)
            {
                int srcIndex = (bounds.Position.Y + y) * RegionSize + bounds.Position.X;
                int dstIndex = y * width;

                if (srcIndex >= 0 && srcIndex + width <= fullUints.Length)
                {
                    Array.Copy(fullUints, srcIndex, result, dstIndex, width);
                }
            }

            return result;
        }

        private void WriteUintSubrect(Rid texture, Rect2I bounds, uint[] subrectData)
        {
            // Ensure any pending GPU work is complete before modifying
            AsyncGpuTaskManager.Instance?.SyncIfNeeded();

            byte[] fullData = Gpu.Rd.TextureGetData(texture, 0);
            uint[] fullUints = GpuUtils.BytesToUIntArray(fullData);

            int width = bounds.Size.X;
            int height = bounds.Size.Y;

            for (int y = 0; y < height; y++)
            {
                int dstIndex = (bounds.Position.Y + y) * RegionSize + bounds.Position.X;
                int srcIndex = y * width;

                if (dstIndex >= 0 && dstIndex + width <= fullUints.Length &&
                    srcIndex + width <= subrectData.Length)
                {
                    Array.Copy(subrectData, srcIndex, fullUints, dstIndex, width);
                }
            }

            byte[] newBytes = GpuUtils.UIntArrayToBytes(fullUints);
            Gpu.Rd.TextureUpdate(texture, 0, newBytes);
        }

        #endregion

        #region Texture Utilities

        /// <summary>
        /// Copies one texture to another. Ensures GPU sync before copying.
        /// </summary>
        private void CopyTexture(Rid source, Rid dest)
        {
            if (!source.IsValid || !dest.IsValid) return;

            // Ensure any pending GPU work is complete before transfer operation
            AsyncGpuTaskManager.Instance?.SyncIfNeeded();

            Gpu.Rd.TextureCopy(
                source,
                dest,
                Vector3.Zero,
                Vector3.Zero,
                new Vector3(RegionSize, RegionSize, 1),
                0, 0, 0, 0
            );
        }

        #endregion

        #region Cleanup

        /// <summary>
        /// Frees all GPU resources.
        /// </summary>
        public void Free()
        {
            // Free working copies
            if (HeightDelta.IsValid)
            {
                Gpu.FreeRid(HeightDelta);
                HeightDelta = new Rid();
            }

            if (TextureEdit.IsValid)
            {
                Gpu.FreeRid(TextureEdit);
                TextureEdit = new Rid();
            }

            if (InstanceExclusion.IsValid)
            {
                Gpu.FreeRid(InstanceExclusion);
                InstanceExclusion = new Rid();
            }

            // Free shadow copies
            if (_heightDeltaCommitted.IsValid)
            {
                Gpu.FreeRid(_heightDeltaCommitted);
                _heightDeltaCommitted = new Rid();
            }

            if (_textureEditCommitted.IsValid)
            {
                Gpu.FreeRid(_textureEditCommitted);
                _textureEditCommitted = new Rid();
            }

            if (_instanceExclusionCommitted.IsValid)
            {
                Gpu.FreeRid(_instanceExclusionCommitted);
                _instanceExclusionCommitted = new Rid();
            }

            PlacedInstances.Clear();
            ActiveEdits = ManualEditFlags.None;

            DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.Cleanup,
                "ManualEditBuffer freed");
        }

        #endregion

        #region Static Encoding Helpers - Intent Format

        /// <summary>
        /// Encodes texture edit intent with separate overlay and base fields.
        /// </summary>
        public static uint EncodeTextureIntent(
            uint overlayId, bool overlayActive, uint overlayWeight,
            uint baseId, bool baseActive, uint baseWeight)
        {
            uint packed = 0u;

            packed |= (overlayId & OVERLAY_ID_MASK) << OVERLAY_ID_SHIFT;
            if (overlayActive) packed |= 1u << OVERLAY_ACTIVE_SHIFT;
            packed |= (overlayWeight & OVERLAY_WEIGHT_MASK) << OVERLAY_WEIGHT_SHIFT;

            packed |= (baseId & BASE_ID_MASK) << BASE_ID_SHIFT;
            if (baseActive) packed |= 1u << BASE_ACTIVE_SHIFT;
            packed |= (baseWeight & BASE_WEIGHT_MASK) << BASE_WEIGHT_SHIFT;

            return packed;
        }

        /// <summary>
        /// Decodes texture edit intent with separate overlay and base fields.
        /// </summary>
        public static void DecodeTextureIntent(
            uint packed,
            out uint overlayId, out bool overlayActive, out uint overlayWeight,
            out uint baseId, out bool baseActive, out uint baseWeight)
        {
            overlayId = (packed >> OVERLAY_ID_SHIFT) & OVERLAY_ID_MASK;
            overlayActive = ((packed >> OVERLAY_ACTIVE_SHIFT) & 1u) != 0u;
            overlayWeight = (packed >> OVERLAY_WEIGHT_SHIFT) & OVERLAY_WEIGHT_MASK;

            baseId = (packed >> BASE_ID_SHIFT) & BASE_ID_MASK;
            baseActive = ((packed >> BASE_ACTIVE_SHIFT) & 1u) != 0u;
            baseWeight = (packed >> BASE_WEIGHT_SHIFT) & BASE_WEIGHT_MASK;
        }

        #endregion
    }

    /// <summary>
    /// Flags indicating which edit types are active in a ManualEditBuffer.
    /// </summary>
    [Flags]
    public enum ManualEditFlags
    {
        None = 0,
        Height = 1 << 0,
        Texture = 1 << 1,
        InstanceExclusion = 1 << 2,
        InstancePlacement = 1 << 3,

        All = Height | Texture | InstanceExclusion | InstancePlacement
    }
}