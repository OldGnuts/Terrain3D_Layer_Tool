using Godot;
using System;

namespace Terrain3DWrapper
{
    #region Enums

    /// <summary>
    /// Terrain map types for update operations
    /// </summary>
    [Flags]
    public enum TerrainMapType
    {
        Height = 1 << 0,
        Control = 1 << 1,
        Color = 1 << 2,
        All = Height | Control | Color
    }

    /// <summary>
    /// Terrain3D map types
    /// </summary>
    public enum MapType
    {
        Height = 0,
        Control = 1,
        Color = 2,
        Max = 3
    }

    #endregion
}