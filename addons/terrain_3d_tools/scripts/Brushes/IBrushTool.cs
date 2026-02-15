// /Brushes/IBrushTool.cs
using Godot;
using Terrain3DTools.Core;
using Terrain3DTools.Layers;

namespace Terrain3DTools.Brushes
{
    /// <summary>
    /// Interface for all brush tools.
    /// </summary>
    public interface IBrushTool
    {
        string ToolName { get; }
        bool IsStrokeActive { get; }

        void BeginStroke(ManualEditLayer layer, Vector3 worldPos, BrushSettings settings, BrushFastPathContext fastPath);
        void ContinueStroke(ManualEditLayer layer, Vector3 worldPos, BrushSettings settings, BrushFastPathContext fastPath);
        BrushUndoData EndStroke(ManualEditLayer layer);
        void CancelStroke();
    }

    /// <summary>
    /// Context passed to brush tools for fast path operations.
    /// Provides access to region data without coupling tools to managers.
    /// </summary>
    public class BrushFastPathContext
    {
        /// <summary>
        /// Gets the RegionData for a given region coordinate.
        /// Returns null if region doesn't exist.
        /// </summary>
        public System.Func<Vector2I, RegionData> GetRegionData { get; set; }

        /// <summary>
        /// Called after dabs are dispatched to track dirty regions for throttled push.
        /// </summary>
        public System.Action<Vector2I> MarkRegionDirty { get; set; }

        /// <summary>
        /// The region size in pixels.
        /// </summary>
        public int RegionSize { get; set; }

        /// <summary>
        /// Whether fast path is enabled (false during undo/redo operations).
        /// </summary>
        public bool Enabled { get; set; } = true;
    }
}