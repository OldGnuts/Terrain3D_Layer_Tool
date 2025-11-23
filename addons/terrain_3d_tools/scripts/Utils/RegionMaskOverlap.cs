using Godot;
using System;

namespace Terrain3DTools.Utils
{
    public struct OverlapResult
    {
        public Vector2I RegionMin; // min pixel coords inside the region
        public Vector2I RegionMax; // max pixel coords (exclusive) inside the region
        public Vector2I MaskMin;   // min pixel coords inside the mask
        public Vector2I MaskMax;   // max pixel coords inside the mask
    }
    public static class RegionMaskOverlap
    {
        public static OverlapResult? GetRegionMaskOverlap(
            Vector2I regionCoord,
            int regionSize,
            Vector2 maskCenterWorld,  // Changed to Vector2 for precision
            Vector2I maskSize)
        {
            // Region bounds in world coordinates
            var regionMinWorld = new Vector2(regionCoord.X * regionSize, regionCoord.Y * regionSize);
            var regionMaxWorld = regionMinWorld + new Vector2(regionSize, regionSize);

            // Mask bounds in world coordinates  
            var maskHalfSize = new Vector2(maskSize.X * 0.5f, maskSize.Y * 0.5f);
            var maskMinWorld = maskCenterWorld - maskHalfSize;
            var maskMaxWorld = maskCenterWorld + maskHalfSize;

            // Find intersection in world space
            var intersectMinWorld = new Vector2(
                Mathf.Max(regionMinWorld.X, maskMinWorld.X),
                Mathf.Max(regionMinWorld.Y, maskMinWorld.Y)
            );
            var intersectMaxWorld = new Vector2(
                Mathf.Min(regionMaxWorld.X, maskMaxWorld.X),
                Mathf.Min(regionMaxWorld.Y, maskMaxWorld.Y)
            );

            // Check if there's actually an overlap
            if (intersectMinWorld.X >= intersectMaxWorld.X || intersectMinWorld.Y >= intersectMaxWorld.Y)
                return null;

            // Convert intersection to region-local coordinates (0 to regionSize)
            var regionOverlapMin = new Vector2I(
                Mathf.RoundToInt(intersectMinWorld.X - regionMinWorld.X),
                Mathf.RoundToInt(intersectMinWorld.Y - regionMinWorld.Y)
            );
            var regionOverlapMax = new Vector2I(
                Mathf.RoundToInt(intersectMaxWorld.X - regionMinWorld.X),
                Mathf.RoundToInt(intersectMaxWorld.Y - regionMinWorld.Y)
            );

            var maskOverlapMin = new Vector2I(
                Mathf.RoundToInt(intersectMinWorld.X - maskMinWorld.X),
                Mathf.RoundToInt(intersectMinWorld.Y - maskMinWorld.Y)
            );
            var maskOverlapMax = new Vector2I(
                Mathf.RoundToInt(intersectMaxWorld.X - maskMinWorld.X),
                Mathf.RoundToInt(intersectMaxWorld.Y - maskMinWorld.Y)
            );

            // Clamp to valid ranges to prevent out-of-bounds access
            regionOverlapMin = new Vector2I(
                Mathf.Clamp(regionOverlapMin.X, 0, regionSize),
                Mathf.Clamp(regionOverlapMin.Y, 0, regionSize)
            );
            regionOverlapMax = new Vector2I(
                Mathf.Clamp(regionOverlapMax.X, 0, regionSize),
                Mathf.Clamp(regionOverlapMax.Y, 0, regionSize)
            );

            maskOverlapMin = new Vector2I(
                Mathf.Clamp(maskOverlapMin.X, 0, maskSize.X),
                Mathf.Clamp(maskOverlapMin.Y, 0, maskSize.Y)
            );
            maskOverlapMax = new Vector2I(
                Mathf.Clamp(maskOverlapMax.X, 0, maskSize.X),
                Mathf.Clamp(maskOverlapMax.Y, 0, maskSize.Y)
            );

            return new OverlapResult
            {
                RegionMin = regionOverlapMin,
                RegionMax = regionOverlapMax,
                MaskMin = maskOverlapMin,
                MaskMax = maskOverlapMax
            };
        }
    }
}