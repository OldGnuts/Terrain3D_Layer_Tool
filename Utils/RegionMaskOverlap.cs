// /Utils/RegionMaskOverlap.cs
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

        /// <summary>Width of overlap in region pixels.</summary>
        public readonly int RegionWidth => RegionMax.X - RegionMin.X;
        
        /// <summary>Height of overlap in region pixels.</summary>
        public readonly int RegionHeight => RegionMax.Y - RegionMin.Y;
        
        /// <summary>Width of overlap in mask pixels.</summary>
        public readonly int MaskWidth => MaskMax.X - MaskMin.X;
        
        /// <summary>Height of overlap in mask pixels.</summary>
        public readonly int MaskHeight => MaskMax.Y - MaskMin.Y;

        public override readonly string ToString()
        {
            return $"Region({RegionMin}-{RegionMax}), Mask({MaskMin}-{MaskMax})";
        }
    }

    public static class RegionMaskOverlap
    {
        /// <summary>
        /// Calculate overlap between a region and a mask defined by center + size.
        /// This is the standard method used by most layers.
        /// </summary>
        public static OverlapResult? GetRegionMaskOverlap(
            Vector2I regionCoord,
            int regionSize,
            Vector2 maskCenterWorld,
            Vector2I maskSize)
        {
            // Convert center+size to min/max bounds
            var maskHalfSize = new Vector2(maskSize.X * 0.5f, maskSize.Y * 0.5f);
            var maskMinWorld = maskCenterWorld - maskHalfSize;
            var maskMaxWorld = maskCenterWorld + maskHalfSize;

            return GetRegionMaskOverlapFromBounds(regionCoord, regionSize, maskMinWorld, maskMaxWorld, maskSize);
        }

        /// <summary>
        /// Calculate overlap between a region and a mask defined by explicit world bounds.
        /// Use this when the mask's world position is defined by min/max rather than center.
        /// This is used by PathLayer where bounds come from curve extents.
        /// </summary>
        /// <param name="regionCoord">Region grid coordinates (e.g., (-1, 0))</param>
        /// <param name="regionSize">Size of region in world units/pixels (e.g., 1024)</param>
        /// <param name="maskWorldMin">Minimum world coordinate of mask coverage</param>
        /// <param name="maskWorldMax">Maximum world coordinate of mask coverage</param>
        /// <param name="maskPixelSize">Size of mask texture in pixels</param>
        /// <returns>Overlap result with pixel coordinates, or null if no overlap</returns>
        public static OverlapResult? GetRegionMaskOverlapFromBounds(
            Vector2I regionCoord,
            int regionSize,
            Vector2 maskWorldMin,
            Vector2 maskWorldMax,
            Vector2I maskPixelSize)
        {
            // Region bounds in world coordinates
            var regionMinWorld = new Vector2(regionCoord.X * regionSize, regionCoord.Y * regionSize);
            var regionMaxWorld = regionMinWorld + new Vector2(regionSize, regionSize);

            // Find intersection in world space
            var intersectMinWorld = new Vector2(
                Mathf.Max(regionMinWorld.X, maskWorldMin.X),
                Mathf.Max(regionMinWorld.Y, maskWorldMin.Y)
            );
            var intersectMaxWorld = new Vector2(
                Mathf.Min(regionMaxWorld.X, maskWorldMax.X),
                Mathf.Min(regionMaxWorld.Y, maskWorldMax.Y)
            );

            // Check if there's actually an overlap
            if (intersectMinWorld.X >= intersectMaxWorld.X || intersectMinWorld.Y >= intersectMaxWorld.Y)
                return null;

            // Convert intersection to region-local pixel coordinates (0 to regionSize)
            var regionOverlapMin = new Vector2I(
                Mathf.RoundToInt(intersectMinWorld.X - regionMinWorld.X),
                Mathf.RoundToInt(intersectMinWorld.Y - regionMinWorld.Y)
            );
            var regionOverlapMax = new Vector2I(
                Mathf.RoundToInt(intersectMaxWorld.X - regionMinWorld.X),
                Mathf.RoundToInt(intersectMaxWorld.Y - regionMinWorld.Y)
            );

            // Convert intersection to mask-local pixel coordinates
            // Account for potential scale difference between world size and pixel size
            Vector2 maskWorldSize = maskWorldMax - maskWorldMin;
            
            // Handle edge case where world size is zero or very small
            if (maskWorldSize.X < 0.001f || maskWorldSize.Y < 0.001f)
                return null;

            // Scale factors from world units to mask pixels
            float scaleX = maskPixelSize.X / maskWorldSize.X;
            float scaleY = maskPixelSize.Y / maskWorldSize.Y;

            var maskOverlapMin = new Vector2I(
                Mathf.RoundToInt((intersectMinWorld.X - maskWorldMin.X) * scaleX),
                Mathf.RoundToInt((intersectMinWorld.Y - maskWorldMin.Y) * scaleY)
            );
            var maskOverlapMax = new Vector2I(
                Mathf.RoundToInt((intersectMaxWorld.X - maskWorldMin.X) * scaleX),
                Mathf.RoundToInt((intersectMaxWorld.Y - maskWorldMin.Y) * scaleY)
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
                Mathf.Clamp(maskOverlapMin.X, 0, maskPixelSize.X),
                Mathf.Clamp(maskOverlapMin.Y, 0, maskPixelSize.Y)
            );
            maskOverlapMax = new Vector2I(
                Mathf.Clamp(maskOverlapMax.X, 0, maskPixelSize.X),
                Mathf.Clamp(maskOverlapMax.Y, 0, maskPixelSize.Y)
            );

            // Final validation - ensure we have actual overlap after clamping
            if (regionOverlapMin.X >= regionOverlapMax.X || regionOverlapMin.Y >= regionOverlapMax.Y ||
                maskOverlapMin.X >= maskOverlapMax.X || maskOverlapMin.Y >= maskOverlapMax.Y)
            {
                return null;
            }

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