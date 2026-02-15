// /Brushes/BrushUndoData.cs
using Godot;
using System.Collections.Generic;
using Terrain3DTools.Layers;

namespace Terrain3DTools.Brushes
{
    /// <summary>
    /// Types of brush operations for undo categorization.
    /// </summary>
    public enum BrushUndoType
    {
        Height,
        Texture,
        InstanceExclusion,
        InstancePlacement
    }

    /// <summary>
    /// Stores undo data for a brush stroke.
    /// </summary>
    public class BrushUndoData
    {
        /// <summary>
        /// Human-readable description for undo menu.
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// The layer that was edited.
        /// </summary>
        public ManualEditLayer Layer { get; set; }

        /// <summary>
        /// Type of edit operation.
        /// </summary>
        public BrushUndoType UndoType { get; set; }

        /// <summary>
        /// Whether this was a smooth operation (affects undo weighting).
        /// </summary>
        public bool IsSmooth { get; set; }

        /// <summary>
        /// All regions affected by this stroke.
        /// </summary>
        public HashSet<Vector2I> AffectedRegions { get; set; }

        /// <summary>
        /// Subrect patches per region containing before/after data.
        /// Used for Height, Texture, and InstanceExclusion undo types.
        /// </summary>
        public Dictionary<Vector2I, RegionUndoPatch> RegionPatches { get; set; }

        /// <summary>
        /// Instances placed during this stroke (for InstancePlacement undo).
        /// </summary>
        public List<InstanceRecord> PlacedInstances { get; set; }

        /// <summary>
        /// Instances removed during this stroke (for InstancePlacement undo).
        /// </summary>
        public List<InstanceRecord> RemovedInstances { get; set; }
    }

    /// <summary>
    /// Stores before/after data for a subrect within a region.
    /// </summary>
    public class RegionUndoPatch
    {
        /// <summary>
        /// The pixel bounds within the region.
        /// </summary>
        public Rect2I Bounds { get; set; }

        /// <summary>
        /// Before-stroke float data (for height/exclusion).
        /// </summary>
        public float[] BeforeData { get; set; }

        /// <summary>
        /// After-stroke float data (for height/exclusion).
        /// </summary>
        public float[] AfterData { get; set; }

        /// <summary>
        /// Before-stroke uint data (for texture edits).
        /// </summary>
        public uint[] BeforeDataUint { get; set; }

        /// <summary>
        /// After-stroke uint data (for texture edits).
        /// </summary>
        public uint[] AfterDataUint { get; set; }
    }

    /// <summary>
    /// Records a single instance placement/removal for undo.
    /// </summary>
    public class InstanceRecord
    {
        public Vector2I RegionCoords { get; set; }
        public int MeshId { get; set; }
        public Transform3D Transform { get; set; }
    }
}