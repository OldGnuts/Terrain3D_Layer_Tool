using Godot;
using System;

namespace Terrain3DWrapper
{
    #region Exceptions

    /// <summary>
    /// Base exception for Terrain3D operations
    /// </summary>
    public class Terrain3DException : Exception
    {
        public Terrain3DException(string message) : base(message) { }
        public Terrain3DException(string message, Exception innerException) : base(message, innerException) { }
    }

    /// <summary>
    /// Exception thrown when a region operation fails
    /// </summary>
    public class RegionException : Terrain3DException
    {
        public Vector2I RegionPosition { get; }

        public RegionException(Vector2I regionPos, string message) : base($"Region {regionPos}: {message}")
        {
            RegionPosition = regionPos;
        }
    }

    #endregion
}