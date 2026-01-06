// /Utils/Extensions.cs
using Godot;
using System.Collections.Generic;

namespace Terrain3DTools.Utils
{
    /// <summary>
    /// A static class to hold useful extension methods for core Godot types.
    /// </summary>
    public static class Terrain3DToolsExtensions
    {
        /// <summary>
        /// An extension method for Rect2I that iterates over every integer coordinate
        /// within its bounds.
        /// </summary>
        /// <param name="rect">The rectangle to iterate over.</param>
        /// <returns>An enumerable of Vector2I coordinates.</returns>
        public static IEnumerable<Vector2I> GetRegionCoords(this Rect2I rect)
        {
            // Note: Rect2I.End is exclusive, so the loop condition is correct.
            for (int x = rect.Position.X; x < rect.End.X; x++)
            {
                for (int y = rect.Position.Y; y < rect.End.Y; y++)
                {
                    yield return new Vector2I(x, y);
                }
            }
        }
    }
}