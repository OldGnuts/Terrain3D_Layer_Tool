using Godot;
/// <summary>
/// Shared mapping info for applying a layer into a region.
/// Packaged so it can be translated into shader push constants.
/// </summary>
/// 
namespace Terrain3DTools.Layers
{
    public struct LayerRegionMapping
    {
        public Vector2I RegionCoords;   // region index
        public Vector2 RegionWorldMin;  // world origin of region
        public Vector2 RegionWorldSize; // world size of region (regionSize, regionSize)

        public Vector2 LayerWorldMin;   // world origin of layer footprint
        public Vector2 LayerWorldSize;  // world size of layer (Size)

        public Vector2I LayerTexSize;   // dimensions of layer mask texture
    }
}