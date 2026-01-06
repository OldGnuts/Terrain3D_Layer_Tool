using Godot;
using System;
using Terrain3DTools.Layers;
using System.Collections.Generic;

namespace Terrain3DTools.Utils
{
    public static class TerrainCoordinateHelper
    {
        public static Vector3 RegionCoordToWorldPosition(Vector2I regionCoords, int regionSize)
        {
            // Region coords to world position (center of region)
            float worldX = regionCoords.X * regionSize + regionSize * 0.5f;
            float worldZ = regionCoords.Y * regionSize + regionSize * 0.5f;
            return new Vector3(worldX, 0, worldZ);
        }
        public static Vector2I WorldToRegionCoords(Vector3 worldPos, int regionSize)
        {
            int rx = Mathf.FloorToInt(worldPos.X / (float)regionSize);
            int rz = Mathf.FloorToInt(worldPos.Z / (float)regionSize);
            return new Vector2I(rx, rz);
        }

        public static Vector2I WorldToRegionCoords(Vector2 worldPos, int regionSize)
        {
            int rx = Mathf.FloorToInt(worldPos.X / (float)regionSize);
            int ry = Mathf.FloorToInt(worldPos.Y / (float)regionSize);
            return new Vector2I(rx, ry);
        }

        public static Vector2 RegionMinWorld(Vector2I regionCoords, int regionSize)
        {
            return new Vector2(
                regionCoords.X * regionSize,
                regionCoords.Y * regionSize
            );
        }

        /// <summary>
        /// Gets the region bounds that a layer covers.
        /// Uses the layer's GetWorldBounds() method which handles special cases like PathLayer.
        /// </summary>
        public static Rect2I GetRegionBoundsForLayer(TerrainLayerBase layer, int regionSize)
        {
            // Use the layer's own bounds calculation (handles PathLayer correctly)
            var (minWorld, maxWorld) = layer.GetWorldBounds();

            Vector2I start = WorldToRegionCoords(minWorld, regionSize);
            Vector2I end = WorldToRegionCoords(maxWorld, regionSize);

            int minX = Math.Min(start.X, end.X);
            int minY = Math.Min(start.Y, end.Y);
            int maxX = Math.Max(start.X, end.X);
            int maxY = Math.Max(start.Y, end.Y);

            return new Rect2I(new Vector2I(minX, minY), new Vector2I(maxX - minX + 1, maxY - minY + 1));
        }

        /// <summary>
        /// Checks if two layers overlap in world space.
        /// Uses each layer's GetWorldBounds() for accurate overlap detection.
        /// </summary>
        public static bool LayersOverlap(TerrainLayerBase a, TerrainLayerBase b)
        {
            var (aMin, aMax) = a.GetWorldBounds();
            var (bMin, bMax) = b.GetWorldBounds();

            return (aMin.X <= bMax.X && aMax.X >= bMin.X) &&
                   (aMin.Y <= bMax.Y && aMax.Y >= bMin.Y);
        }
    }
}