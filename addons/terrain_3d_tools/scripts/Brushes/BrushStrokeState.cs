// /Brushes/BrushStrokeState.cs
using Godot;
using System.Collections.Generic;
using Terrain3DTools.Core;
using Terrain3DTools.Core.Debug;
using Terrain3DTools.Layers;
using Terrain3DTools.Layers.ManualEdit;

namespace Terrain3DTools.Brushes
{
    /// <summary>
    /// Tracks state during an active brush stroke.
    /// Manages bounds tracking for subrect undo and shadow copy for cancel.
    /// 
    /// Key responsibilities:
    /// - Track which regions are affected during a stroke
    /// - Track pixel bounds within each region for minimal undo storage
    /// - Ensure GPU sync at appropriate times
    /// - Generate subrect-based undo patches at stroke end
    /// - Revert to shadow copy on stroke cancel
    /// </summary>
    public class BrushStrokeState
    {
        private const string DEBUG_CLASS_NAME = "BrushStrokeState";

        #region Properties

        /// <summary>
        /// The layer being edited during this stroke.
        /// </summary>
        public ManualEditLayer Layer { get; private set; }

        /// <summary>
        /// The type of edit operation.
        /// </summary>
        public BrushUndoType UndoType { get; private set; }

        /// <summary>
        /// Whether a stroke is currently active.
        /// </summary>
        public bool IsActive { get; private set; }

        /// <summary>
        /// Set to true by smooth brush to affect undo weighting.
        /// </summary>
        public bool IsSmooth { get; set; }

        #endregion

        #region Private Fields

        private Vector3 _lastWorldPos;
        private Dictionary<Vector2I, Rect2I> _affectedBoundsPerRegion = new();
        private HashSet<Vector2I> _affectedRegions = new();
        private bool _hasChanges = false;

        #endregion

        #region Constructor

        public BrushStrokeState()
        {
            DebugManager.Instance?.RegisterClass(DEBUG_CLASS_NAME);
        }

        #endregion

        #region Stroke Lifecycle

        /// <summary>
        /// Begins a new stroke.
        /// Ensures any pending GPU work is complete before modifications start.
        /// </summary>
        /// <param name="layer">The layer to edit</param>
        /// <param name="undoType">The type of edit operation</param>
        public void Begin(ManualEditLayer layer, BrushUndoType undoType)
        {
            // Ensure any pending GPU work is complete before we start
            // This prevents reading stale data or modifying in-flight resources
            AsyncGpuTaskManager.Instance?.SyncIfNeeded();

            Layer = layer;
            UndoType = undoType;
            IsActive = true;
            IsSmooth = false;
            _affectedBoundsPerRegion.Clear();
            _affectedRegions.Clear();
            _hasChanges = false;

            DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.UpdateCycle,
                $"Stroke began: type={undoType}, layer={layer?.LayerName ?? "null"}");
        }

        /// <summary>
        /// Marks a region as affected and expands its bounds based on the brush dab.
        /// Call this during painting - no GPU sync needed during active stroke.
        /// </summary>
        /// <param name="regionCoords">The region being modified</param>
        /// <param name="buffer">The edit buffer for this region</param>
        /// <param name="dabBounds">Pixel bounds of this brush dab within the region</param>
        public void MarkRegionAffected(Vector2I regionCoords, ManualEditBuffer buffer, Rect2I dabBounds)
        {
            if (!IsActive) return;

            _hasChanges = true;

            // First time touching this region
            if (!_affectedRegions.Contains(regionCoords))
            {
                _affectedRegions.Add(regionCoords);

                // Ensure shadow copy exists for cancel support
                EnsureShadowCopyExists(buffer);

                DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.UpdateCycle,
                    $"First touch on region {regionCoords}");
            }

            // Expand or create bounds for this region
            if (_affectedBoundsPerRegion.TryGetValue(regionCoords, out var existing))
            {
                _affectedBoundsPerRegion[regionCoords] = existing.Merge(dabBounds);
            }
            else
            {
                _affectedBoundsPerRegion[regionCoords] = dabBounds;
            }
        }

        /// <summary>
        /// Legacy overload for brushes not yet tracking precise bounds.
        /// Falls back to full region bounds.
        /// </summary>
        public void MarkRegionAffected(Vector2I regionCoords, ManualEditBuffer buffer)
        {
            var fullBounds = new Rect2I(0, 0, buffer.RegionSize, buffer.RegionSize);
            MarkRegionAffected(regionCoords, buffer, fullBounds);
        }

        /// <summary>
        /// Updates the last world position for stroke interpolation.
        /// </summary>
        public void UpdatePosition(Vector3 worldPos)
        {
            _lastWorldPos = worldPos;
        }

        /// <summary>
        /// Gets the last recorded world position.
        /// </summary>
        public Vector3 GetLastPosition() => _lastWorldPos;

        /// <summary>
        /// Ends the stroke and captures subrect-based undo data.
        /// Performs GPU sync to ensure all brush work is complete before readback.
        /// </summary>
        /// <param name="description">Human-readable description for undo menu</param>
        /// <returns>Undo data, or null if no changes were made</returns>
        public BrushUndoData End(string description)
        {
            if (!IsActive)
            {
                DebugManager.Instance?.LogWarning(DEBUG_CLASS_NAME,
                    "End called on inactive stroke");
                return null;
            }

            if (!_hasChanges || Layer == null)
            {
                DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.UpdateCycle,
                    "Stroke ended with no changes");
                IsActive = false;
                return null;
            }

            // Sync GPU - all brush work must complete before readback
            AsyncGpuTaskManager.Instance?.SyncIfNeeded();

            var undoData = new BrushUndoData
            {
                Description = description,
                Layer = Layer,
                UndoType = UndoType,
                IsSmooth = IsSmooth,
                AffectedRegions = new HashSet<Vector2I>(_affectedRegions),
                RegionPatches = new Dictionary<Vector2I, RegionUndoPatch>()
            };

            // Capture subrect patches for each affected region
            foreach (var (regionCoords, bounds) in _affectedBoundsPerRegion)
            {
                var buffer = Layer.GetEditBuffer(regionCoords);
                if (buffer == null)
                {
                    DebugManager.Instance?.LogWarning(DEBUG_CLASS_NAME,
                        $"No buffer found for region {regionCoords} at stroke end");
                    continue;
                }

                // Pad bounds slightly for edge safety (brush falloff, etc.)
                var paddedBounds = PadBounds(bounds, 2, buffer.RegionSize);

                var patch = CreatePatch(buffer, paddedBounds);
                if (patch != null)
                {
                    undoData.RegionPatches[regionCoords] = patch;

                    DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.UpdateCycle,
                        $"Created patch for region {regionCoords}: {paddedBounds.Size.X}x{paddedBounds.Size.Y} pixels");
                }

                // Commit working → shadow for this region
                CommitBuffer(buffer);
            }

            IsActive = false;

            DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.UpdateCycle,
                $"Stroke ended: {undoData.RegionPatches.Count} region patches created");

            return undoData;
        }

        /// <summary>
        /// Cancels the stroke, reverting all affected regions to their shadow copy state.
        /// </summary>
        public void Cancel()
        {
            if (!IsActive)
            {
                DebugManager.Instance?.LogWarning(DEBUG_CLASS_NAME,
                    "Cancel called on inactive stroke");
                return;
            }

            if (Layer == null)
            {
                IsActive = false;
                return;
            }

            // Sync GPU before reverting
            AsyncGpuTaskManager.Instance?.SyncIfNeeded();

            foreach (var regionCoords in _affectedRegions)
            {
                var buffer = Layer.GetEditBuffer(regionCoords);
                if (buffer != null)
                {
                    RevertBuffer(buffer);
                    DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.UpdateCycle,
                        $"Reverted region {regionCoords}");
                }
            }

            IsActive = false;
            _affectedBoundsPerRegion.Clear();
            _affectedRegions.Clear();

            DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.UpdateCycle,
                "Stroke cancelled");
        }

        #endregion

        #region Buffer Operations by Type

        private void EnsureShadowCopyExists(ManualEditBuffer buffer)
        {
            // Calling GetOrCreate ensures both working and shadow copies exist
            switch (UndoType)
            {
                case BrushUndoType.Height:
                    buffer.GetOrCreateHeightDelta();
                    break;
                case BrushUndoType.Texture:
                    buffer.GetOrCreateTextureEdit();
                    break;
                case BrushUndoType.InstanceExclusion:
                    buffer.GetOrCreateInstanceExclusion();
                    break;
                // InstancePlacement doesn't use texture buffers
            }
        }

        private RegionUndoPatch CreatePatch(ManualEditBuffer buffer, Rect2I bounds)
        {
            var patch = new RegionUndoPatch { Bounds = bounds };

            switch (UndoType)
            {
                case BrushUndoType.Height:
                    patch.BeforeData = buffer.ReadCommittedHeightSubrect(bounds);
                    patch.AfterData = buffer.ReadWorkingHeightSubrect(bounds);
                    break;

                case BrushUndoType.Texture:
                    patch.BeforeDataUint = buffer.ReadCommittedTextureSubrect(bounds);
                    patch.AfterDataUint = buffer.ReadWorkingTextureSubrect(bounds);
                    break;

                case BrushUndoType.InstanceExclusion:
                    patch.BeforeData = buffer.ReadCommittedExclusionSubrect(bounds);
                    patch.AfterData = buffer.ReadWorkingExclusionSubrect(bounds);
                    break;

                case BrushUndoType.InstancePlacement:
                    // Instance placement uses record-based undo, not patches
                    return null;

                default:
                    return null;
            }

            return patch;
        }

        private void CommitBuffer(ManualEditBuffer buffer)
        {
            switch (UndoType)
            {
                case BrushUndoType.Height:
                    buffer.CommitHeightDelta();
                    break;
                case BrushUndoType.Texture:
                    buffer.CommitTextureEdit();
                    break;
                case BrushUndoType.InstanceExclusion:
                    buffer.CommitInstanceExclusion();
                    break;
            }
        }

        private void RevertBuffer(ManualEditBuffer buffer)
        {
            switch (UndoType)
            {
                case BrushUndoType.Height:
                    buffer.RevertHeightDelta();
                    break;
                case BrushUndoType.Texture:
                    buffer.RevertTextureEdit();
                    break;
                case BrushUndoType.InstanceExclusion:
                    buffer.RevertInstanceExclusion();
                    break;
            }
        }

        #endregion

        #region Helpers

        /// <summary>
        /// Pads bounds by a given amount, clamping to region size.
        /// </summary>
        private Rect2I PadBounds(Rect2I bounds, int padding, int regionSize)
        {
            int minX = Mathf.Max(0, bounds.Position.X - padding);
            int minY = Mathf.Max(0, bounds.Position.Y - padding);
            int maxX = Mathf.Min(regionSize, bounds.End.X + padding);
            int maxY = Mathf.Min(regionSize, bounds.End.Y + padding);

            return new Rect2I(minX, minY, maxX - minX, maxY - minY);
        }

        #endregion
    }
}