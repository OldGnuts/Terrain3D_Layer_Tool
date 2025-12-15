// /Layers/Path/Enums/PathEnums.cs
namespace Terrain3DTools.Layers.Path
{
    /// <summary>
    /// High-level path type that determines available settings and default profile.
    /// </summary>
    public enum PathType
    {
        Road,       // Raised, flat surface with shoulders
        River,      // Carved channel with banks
        Stream,     // Narrow carved channel, more terrain conformance
        Trail,      // Soft path, high terrain conformance
        Trench,     // Deep carved channel with steep walls
        Ridge,      // Raised spine
        Canal,      // Flat-bottomed carved channel
        Custom      // User-defined profile
    }

    /// <summary>
    /// Zone type within a path profile. Determines behavior and defaults.
    /// </summary>
    public enum ZoneType
    {
        Center,     // Primary surface (road surface, river bed, trail center)
        Inner,      // Secondary surface (road lanes, deep channel)
        Shoulder,   // Sides of roads, banks of rivers
        Edge,       // Soft transition to terrain
        Wall,       // Steep sides (trenches, canyons)
        Rim,        // Raised edge (embankment top, levee)
        Slope,      // Graded transition
        Transition  // Pure blend zone (no modification, just falloff)
    }

    /// <summary>
    /// How a zone's height modification is applied.
    /// </summary>
    public enum HeightBlendMode
    {
        Replace,    // Set to absolute height (path Y + offset)
        Add,        // Add to existing terrain
        Subtract,   // Subtract from existing terrain
        Min,        // Take minimum (carve only where higher)
        Max,        // Take maximum (raise only where lower)
        Blend       // Weighted blend with terrain
    }

    /// <summary>
    /// How a zone's texture is applied.
    /// </summary>
    public enum TextureBlendMode
    {
        Replace,    // Fully replace terrain texture
        Blend,      // Blend with existing based on strength
        Overlay,    // Use as overlay texture
        None        // Don't modify texture in this zone
    }

    /// <summary>
    /// Noise application target.
    /// </summary>
    public enum NoiseTarget
    {
        Height,
        Texture,
        Both
    }

    /// <summary>
    /// Detail placement alignment mode.
    /// </summary>
    public enum DetailAlignment
    {
        PathDirection,      // Align to path tangent
        PathPerpendicular,  // Align perpendicular to path
        TerrainNormal,      // Align to terrain slope
        WorldUp,            // Always upright
        Random              // Random rotation
    }

    public enum QuickPathType
    {
        Straight,
        SCurve,
        Loop
    }
}