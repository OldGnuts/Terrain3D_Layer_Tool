// /Brushes/BrushSettings.cs
using Godot;

namespace Terrain3DTools.Brushes
{
    public enum TextureBrushMode
    {
        /// <summary>
        /// Sets overlay texture and increases blend toward overlay (255).
        /// </summary>
        PaintOverlay,

        /// <summary>
        /// Sets base texture and decreases blend toward base (0).
        /// </summary>
        PaintBase,

        /// <summary>
        /// Only adjusts blend value without changing texture IDs.
        /// Positive strength increases blend, negative decreases.
        /// </summary>
        AdjustBlend,

        /// <summary>
        /// Sets both base and overlay textures with specific blend.
        /// Uses TextureId for overlay, SecondaryTextureId for base.
        /// </summary>
        FullReplace
    }
    /// <summary>
    /// Shared settings for terrain brushes.
    /// Can be displayed in inspector when a ManualEditLayer is selected.
    /// </summary>
    [GlobalClass, Tool]
    public partial class BrushSettings : Resource
    {
        #region Size and Shape

        [Export(PropertyHint.Range, "1,500,1")]
        public float Size { get; set; } = 50f;

        [Export(PropertyHint.Range, "0,1,0.01")]
        public float Strength { get; set; } = 0.5f;

        [Export(PropertyHint.Range, "0,1,0.01")]
        public float Falloff { get; set; } = 0.5f;

        [Export]
        public BrushShape Shape { get; set; } = BrushShape.Circle;

        #endregion

        #region Height Brush Settings

        [ExportGroup("Height Brush")]

        [Export(PropertyHint.Range, "-1,1,0.01")]
        public float HeightDelta { get; set; } = 0.1f;

        #endregion

        #region Texture Brush Settings

        [ExportGroup("Texture Brush")]

        [Export(PropertyHint.Range, "0,31,1")]
        public int TextureId { get; set; } = 0;

        [Export(PropertyHint.Range, "0,31,1")]
        public int SecondaryTextureId { get; set; } = 0;

        [Export]
        public TextureBrushMode TextureMode { get; set; } = TextureBrushMode.PaintOverlay;

        [Export(PropertyHint.Range, "0,255,1")]
        public int TargetBlend { get; set; } = 255;

        /// <summary>
        /// When true, blend accumulates with each stroke. When false, blend jumps to target.
        /// </summary>
        [Export]
        public bool AccumulateBlend { get; set; } = true;

        /// <summary>
        /// How much blend changes per stroke when AccumulateBlend is true.
        /// </summary>
        [Export(PropertyHint.Range, "1,50,1")]
        public int BlendStep { get; set; } = 10;

        #endregion
        #region Instance Brush Settings

        [ExportGroup("Instance Brush")]

        [Export(PropertyHint.Range, "0,100,1")]
        public int InstanceMeshId { get; set; } = 0;

        [Export(PropertyHint.Range, "0.1,10,0.1")]
        public float InstanceScale { get; set; } = 1.0f;

        [Export]
        public bool RandomRotation { get; set; } = true;

        [Export(PropertyHint.Range, "0,1,0.01")]
        public float RandomScaleVariation { get; set; } = 0.1f;

        [Export(PropertyHint.Range, "0,45,1")]
        public float InstanceRandomTilt { get; set; } = 0f;

        [Export]
        public bool InstanceAlignToNormal { get; set; } = false;

        [Export(PropertyHint.Range, "-10,10,0.1")]
        public float InstanceVerticalOffset { get; set; } = 0f;

        /// <summary>
        /// When true, dragging places multiple instances (scatter mode).
        /// When false, only single click placement.
        /// </summary>
        [Export]
        public bool InstanceScatterMode { get; set; } = false;

        /// <summary>
        /// Number of instances to place per scatter operation.
        /// </summary>
        [Export(PropertyHint.Range, "1,50,1")]
        public int InstanceScatterCount { get; set; } = 5;

        /// <summary>
        /// Minimum distance between scattered instances.
        /// </summary>
        [Export(PropertyHint.Range, "0.5,50,0.5")]
        public float InstanceMinDistance { get; set; } = 2.0f;

        /// <summary>
        /// When true, instances won't be placed on steep slopes.
        /// </summary>
        [Export]
        public bool InstanceSlopeLimitEnabled { get; set; } = false;

        /// <summary>
        /// Maximum slope in degrees for instance placement.
        /// </summary>
        [Export(PropertyHint.Range, "0,90,1")]
        public float InstanceMaxSlope { get; set; } = 45f;

        [ExportSubgroup("Exclusion")]

        [Export(PropertyHint.Range, "1,100,1")]
        public float ExclusionBrushSize { get; set; } = 10f;

        /// <summary>
        /// When true, brush adds exclusion (blocks instances).
        /// When false, brush removes exclusion (allows instances).
        /// </summary>
        [Export]
        public bool ExclusionMode { get; set; } = true;

        /// <summary>
        /// When true, exclusion builds up gradually with strokes.
        /// When false, exclusion is applied directly based on strength.
        /// </summary>
        [Export]
        public bool ExclusionAccumulate { get; set; } = true;

        #endregion

        #region Helpers

        /// <summary>
        /// Calculates falloff multiplier for a distance from brush center.
        /// </summary>
        public float CalculateFalloff(float normalizedDistance)
        {
            if (normalizedDistance >= 1f) return 0f;
            if (normalizedDistance <= 0f) return 1f;
            if (Falloff <= 0f) return 1f;

            // Falloff starts at (1 - Falloff) of the radius
            float falloffStart = 1f - Falloff;

            if (normalizedDistance <= falloffStart)
                return 1f;

            // Smooth falloff from falloffStart to 1.0
            float t = (normalizedDistance - falloffStart) / Falloff;
            return 1f - (t * t * (3f - 2f * t)); // Smoothstep
        }

        #endregion
    }

    public enum BrushShape
    {
        Circle,
        Square
    }

    public enum TextureBrushSlot
    {
        Base,
        Overlay,
        Both
    }
}