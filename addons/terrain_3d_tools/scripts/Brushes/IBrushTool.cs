// /Brushes/IBrushTool.cs
using Godot;
using System.Collections.Generic;
using Terrain3DTools.Layers;

namespace Terrain3DTools.Brushes
{
    /// <summary>
    /// Interface for terrain brush tools.
    /// </summary>
    public interface IBrushTool
    {
        /// <summary>
        /// Display name for the tool.
        /// </summary>
        string ToolName { get; }

        /// <summary>
        /// Called when a brush stroke begins (mouse down).
        /// </summary>
        void BeginStroke(ManualEditLayer layer, Vector3 worldPos, BrushSettings settings);

        /// <summary>
        /// Called during brush stroke (mouse move while pressed).
        /// </summary>
        void ContinueStroke(ManualEditLayer layer, Vector3 worldPos, BrushSettings settings);

        /// <summary>
        /// Called when brush stroke ends (mouse up).
        /// Returns undo action data, or null if no changes were made.
        /// </summary>
        BrushUndoData EndStroke(ManualEditLayer layer);

        /// <summary>
        /// Cancels the current stroke without applying undo data.
        /// </summary>
        void CancelStroke();

        /// <summary>
        /// Returns true if a stroke is currently in progress.
        /// </summary>
        bool IsStrokeActive { get; }
    }

    /// <summary>
    /// Data needed to undo/redo a brush stroke.
    /// </summary>
    public class BrushUndoData
    {
        public string Description { get; set; }
        public ManualEditLayer Layer { get; set; }

        /// <summary>
        /// Per-region before states (GPU texture data).
        /// Key: region coords, Value: byte array of texture data before stroke.
        /// </summary>
        public Dictionary<Vector2I, byte[]> BeforeStates { get; set; } = new();

        /// <summary>
        /// Per-region after states (GPU texture data).
        /// Key: region coords, Value: byte array of texture data after stroke.
        /// </summary>
        public Dictionary<Vector2I, byte[]> AfterStates { get; set; } = new();

        /// <summary>
        /// Which edit type this undo data is for.
        /// </summary>
        public BrushUndoType UndoType { get; set; }

        /// <summary>
        /// Regions that were affected by this stroke.
        /// </summary>
        public HashSet<Vector2I> AffectedRegions { get; set; } = new();

        /// <summary>
        /// List of instances placed during this stroke (for instance placement undo).
        /// </summary>
        public List<PlacedInstanceRecord> PlacedInstances { get; set; }

        /// <summary>
        /// List of instances removed during this stroke (for instance erase undo).
        /// </summary>
        public List<PlacedInstanceRecord> RemovedInstances { get; set; }
    }

    public enum BrushUndoType
    {
        Height,
        Texture,
        InstanceExclusion,
        InstancePlacement
    }
}