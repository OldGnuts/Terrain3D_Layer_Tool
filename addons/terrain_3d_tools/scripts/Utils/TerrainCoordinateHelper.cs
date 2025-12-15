using Godot;
using System;
using Terrain3DTools.Layers;
using System.Collections.Generic;

namespace Terrain3DTools.Utils
{
    public static class TerrainCoordinateHelper
    {
        public static Vector2I WorldToRegionCoords(Vector3 worldPos, int regionSize)
        {
            int rx = Mathf.FloorToInt(worldPos.X / (float)regionSize);
            int rz = Mathf.FloorToInt(worldPos.Z / (float)regionSize);
            return new Vector2I(rx, rz);
        }

        public static Vector2 RegionMinWorld(Vector2I regionCoords, int regionSize)
        {
            return new Vector2(
                regionCoords.X * regionSize,
                regionCoords.Y * regionSize
            );
        }

        public static Rect2I GetRegionBoundsForLayer(TerrainLayerBase layer, int regionSize)
        {
            int halfX = (int)(layer.Size.X / 2);
            int halfZ = (int)(layer.Size.Y / 2);

            Vector2 topLeft = new Vector2(layer.GlobalPosition.X - halfX, layer.GlobalPosition.Z - halfZ);
            Vector2 bottomRight = new Vector2(layer.GlobalPosition.X + halfX, layer.GlobalPosition.Z + halfZ);

            Vector2I start = WorldToRegionCoords(new Vector3(topLeft.X, 0, topLeft.Y), regionSize);
            Vector2I end = WorldToRegionCoords(new Vector3(bottomRight.X, 0, bottomRight.Y), regionSize);

            int minX = Math.Min(start.X, end.X);
            int minY = Math.Min(start.Y, end.Y);
            int maxX = Math.Max(start.X, end.X);
            int maxY = Math.Max(start.Y, end.Y);

            return new Rect2I(new Vector2I(minX, minY), new Vector2I(maxX - minX + 1, maxY - minY + 1));
        }

        public static bool LayersOverlap(TerrainLayerBase a, TerrainLayerBase b)
        {
            Vector2 aMin = new Vector2(a.GlobalPosition.X - a.Size.X / 2, a.GlobalPosition.Z - a.Size.Y / 2);
            Vector2 aMax = new Vector2(a.GlobalPosition.X + a.Size.X / 2, a.GlobalPosition.Z + a.Size.Y / 2);

            Vector2 bMin = new Vector2(b.GlobalPosition.X - b.Size.X / 2, b.GlobalPosition.Z - b.Size.Y / 2);
            Vector2 bMax = new Vector2(b.GlobalPosition.X + b.Size.X / 2, b.GlobalPosition.Z + b.Size.Y / 2);

            return (aMin.X <= bMax.X && aMax.X >= bMin.X) &&
                   (aMin.Y <= bMax.Y && aMax.Y >= bMin.Y);
        }

    }
}