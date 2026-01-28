// /Brushes/BrushStrokeState.cs
using Godot;
using System.Collections.Generic;
using Terrain3DTools.Core;
using Terrain3DTools.Layers;
using Terrain3DTools.Layers.ManualEdit;

namespace Terrain3DTools.Brushes
{
    /// <summary>
    /// Tracks state during an active brush stroke.
    /// Handles before-state capture for undo and affected region tracking.
    /// </summary>
    public class BrushStrokeState
    {
        public ManualEditLayer Layer { get; private set; }
        public BrushUndoType UndoType { get; private set; }
        public bool IsActive { get; private set; }

        private Dictionary<Vector2I, byte[]> _beforeStates = new();
        private HashSet<Vector2I> _affectedRegions = new();
        private Vector3 _lastWorldPos;
        private bool _hasChanges = false;

        /// <summary>
        /// Begins a new stroke, clearing previous state.
        /// </summary>
        public void Begin(ManualEditLayer layer, BrushUndoType undoType)
        {
            Layer = layer;
            UndoType = undoType;
            IsActive = true;
            _beforeStates.Clear();
            _affectedRegions.Clear();
            _hasChanges = false;
        }

        /// <summary>
        /// Marks a region as affected and captures its before-state if not already captured.
        /// </summary>
        public void MarkRegionAffected(Vector2I regionCoords, ManualEditBuffer buffer)
        {
            if (!IsActive) return;

            _affectedRegions.Add(regionCoords);
            _hasChanges = true;

            // Capture before state only once per region per stroke
            if (!_beforeStates.ContainsKey(regionCoords))
            {
                byte[] beforeData = CaptureBufferState(buffer, UndoType);
                if (beforeData != null)
                {
                    _beforeStates[regionCoords] = beforeData;
                }
            }
        }

        /// <summary>
        /// Updates the last world position (for stroke interpolation).
        /// </summary>
        public void UpdatePosition(Vector3 worldPos)
        {
            _lastWorldPos = worldPos;
        }

        public Vector3 GetLastPosition() => _lastWorldPos;

        /// <summary>
        /// Ends the stroke and returns undo data.
        /// Returns null if no changes were made.
        /// </summary>
        public BrushUndoData End(string description)
        {
            if (!IsActive || !_hasChanges)
            {
                IsActive = false;
                return null;
            }

            var undoData = new BrushUndoData
            {
                Description = description,
                Layer = Layer,
                UndoType = UndoType,
                BeforeStates = new Dictionary<Vector2I, byte[]>(_beforeStates),
                AffectedRegions = new HashSet<Vector2I>(_affectedRegions)
            };

            // Capture after states
            foreach (var regionCoords in _affectedRegions)
            {
                var buffer = Layer.GetEditBuffer(regionCoords);
                if (buffer != null)
                {
                    byte[] afterData = CaptureBufferState(buffer, UndoType);
                    if (afterData != null)
                    {
                        undoData.AfterStates[regionCoords] = afterData;
                    }
                }
            }

            IsActive = false;
            return undoData;
        }

        /// <summary>
        /// Cancels the stroke without creating undo data.
        /// </summary>
        public void Cancel()
        {
            // Restore before states
            if (IsActive && _hasChanges && Layer != null)
            {
                foreach (var (regionCoords, beforeData) in _beforeStates)
                {
                    var buffer = Layer.GetEditBuffer(regionCoords);
                    if (buffer != null)
                    {
                        RestoreBufferState(buffer, beforeData, UndoType);
                    }
                }
            }

            IsActive = false;
            _beforeStates.Clear();
            _affectedRegions.Clear();
        }

        #region State Capture Helpers

        private byte[] CaptureBufferState(ManualEditBuffer buffer, BrushUndoType type)
        {
            Rid textureRid = GetTextureForUndoType(buffer, type);
            if (!textureRid.IsValid) return null;

            try
            {
                // Only call TextureGetData - no Sync needed for immediate readback
                // The RenderingDevice will handle synchronization internally
                return Gpu.Rd.TextureGetData(textureRid, 0);
            }
            catch (System.Exception ex)
            {
                GD.PrintErr($"[BrushStrokeState] Failed to capture buffer state: {ex.Message}");
                return null;
            }
        }

        private void RestoreBufferState(ManualEditBuffer buffer, byte[] data, BrushUndoType type)
        {
            Rid textureRid = GetTextureForUndoType(buffer, type);
            if (!textureRid.IsValid || data == null) return;

            try
            {
                Gpu.Rd.TextureUpdate(textureRid, 0, data);
            }
            catch (System.Exception ex)
            {
                GD.PrintErr($"[BrushStrokeState] Failed to restore buffer state: {ex.Message}");
            }
        }

        private Rid GetTextureForUndoType(ManualEditBuffer buffer, BrushUndoType type)
        {
            return type switch
            {
                BrushUndoType.Height => buffer.HeightDelta,
                BrushUndoType.Texture => buffer.TextureEdit,
                BrushUndoType.InstanceExclusion => buffer.InstanceExclusion,
                _ => new Rid()
            };
        }

        #endregion
    }
}