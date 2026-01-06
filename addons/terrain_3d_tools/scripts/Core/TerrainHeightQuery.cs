using Godot;
using System;
using Terrain3DTools.Settings;
using Terrain3DTools.Utils;

namespace Terrain3DTools.Core
{
    public static class TerrainHeightQuery
    {
        private static TerrainLayerManager _terrainLayerManager;
        
        public static void SetTerrainLayerManager(TerrainLayerManager manager)
        {
            _terrainLayerManager = manager;
        }
        
        public static TerrainLayerManager GetTerrainLayerManager()
        {
            return _terrainLayerManager;
        }
        
        /// <summary>
        /// Query height at world position (X, Z) with bilinear interpolation
        /// Returns the actual world height (normalized height * WorldHeightScale)
        /// </summary>
        public static float? QueryHeight(Vector2 worldPosition)
        {
            if (_terrainLayerManager == null)
                return null;
                
            return QueryHeight(worldPosition, _terrainLayerManager);
        }
        
        public static float? QueryHeight(Vector2 worldPosition, TerrainLayerManager terrainManager)
        {
            if (terrainManager == null)
                return null;
            
            var regionMapManager = terrainManager.GetRegionMapManager();
            if (regionMapManager == null)
                return null;
            
            int regionSize = terrainManager.Terrain3DRegionSize;
            
            // Read world height scale from global settings
            float worldHeightScale = GlobalToolSettingsManager.Current?.WorldHeightScale ?? 128f;
            
            if (regionSize <= 0)
                return null;
            
            // Calculate which region this position is in
            Vector2I regionCoords = new Vector2I(
                Mathf.FloorToInt(worldPosition.X / regionSize),
                Mathf.FloorToInt(worldPosition.Y / regionSize)
            );
            
            // Get region data (don't create if it doesn't exist)
            var regionData = regionMapManager.GetRegionData(regionCoords);
            if (regionData == null || !regionData.HeightMap.IsValid)
                return null;
            
            // Calculate local position within region (0 to regionSize)
            Vector2 regionMinWorld = new Vector2(regionCoords.X * regionSize, regionCoords.Y * regionSize);
            Vector2 localPos = worldPosition - regionMinWorld;
            
            // Download the heightmap data from GPU
            var heightImage = TextureUtil.TextureToImage(regionData.HeightMap, regionSize, regionSize, Image.Format.Rf);
            if (heightImage == null)
                return null;
            
            int width = heightImage.GetWidth();
            int height = heightImage.GetHeight();
            
            // Convert to floating point pixel coordinates for interpolation
            float fx = (localPos.X / regionSize) * width;
            float fy = (localPos.Y / regionSize) * height;
            
            // Clamp to valid range
            if (fx < 0 || fx >= width - 1 || fy < 0 || fy >= height - 1)
                return null;
            
            // Get the four surrounding pixels
            int x0 = (int)Mathf.Floor(fx);
            int y0 = (int)Mathf.Floor(fy);
            int x1 = Mathf.Min(x0 + 1, width - 1);
            int y1 = Mathf.Min(y0 + 1, height - 1);
            
            // Get fractional parts for interpolation
            float tx = fx - x0;
            float ty = fy - y0;
            
            // Sample four corners (normalized -1 to 1)
            float h00 = SampleHeightFromImage(heightImage, x0, y0);
            float h10 = SampleHeightFromImage(heightImage, x1, y0);
            float h01 = SampleHeightFromImage(heightImage, x0, y1);
            float h11 = SampleHeightFromImage(heightImage, x1, y1);
            
            // Bilinear interpolation
            float h0 = Mathf.Lerp(h00, h10, tx);
            float h1 = Mathf.Lerp(h01, h11, tx);
            float normalizedHeight = Mathf.Lerp(h0, h1, ty);
            
            // Convert normalized height (-1 to 1) to world height
            return normalizedHeight * worldHeightScale;
        }
        
        private static float SampleHeightFromImage(Image image, int x, int y)
        {
            // Height is stored in R channel as a float in range -1 to 1
            Color pixel = image.GetPixel(x, y);
            return pixel.R;
        }
        
        /// <summary>
        /// Raycast from camera to terrain (for editor tools)
        /// </summary>
        public static Vector3? RaycastToTerrain(Camera3D camera, Vector2 screenPosition)
        {
            if (camera == null || _terrainLayerManager == null)
                return null;
            
            var from = camera.ProjectRayOrigin(screenPosition);
            var direction = camera.ProjectRayNormal(screenPosition);
            
            // Sample along the ray to find intersection with terrain
            float maxDistance = 10000f;
            int samples = 200;
            
            for (int i = 0; i < samples; i++)
            {
                float t = (i / (float)samples) * maxDistance;
                var point = from + direction * t;
                var point2D = new Vector2(point.X, point.Z);
                
                var terrainHeight = QueryHeight(point2D);
                if (terrainHeight.HasValue)
                {
                    // Check if ray has crossed the terrain
                    if (point.Y <= terrainHeight.Value)
                    {
                        // Refine the intersection point
                        return new Vector3(point.X, terrainHeight.Value, point.Z);
                    }
                }
            }
            
            return null;
        }
    }
}