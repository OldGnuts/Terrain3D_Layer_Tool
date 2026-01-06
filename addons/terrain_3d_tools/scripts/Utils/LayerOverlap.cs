using Godot;
using Terrain3DTools.Layers;

namespace Terrain3DTools.Utils
{
    /// <summary>
    /// A static utility class to check for 2D spatial overlaps between terrain layers on the XZ plane.
    /// </summary>
    public static class LayerOverlap
    {
        /// <summary>
        /// Checks if the bounding boxes of two TerrainLayerBase objects intersect on the XZ plane.
        /// </summary>
        /// <param name="layerA">The first layer.</param>
        /// <param name="layerB">The second layer.</param>
        /// <returns>True if the layers' XZ bounds overlap, false otherwise.</returns>
        public static bool DoLayersOverlap(TerrainLayerBase layerA, TerrainLayerBase layerB)
        {
            if (layerA == null || layerB == null)
            {
                return false;
            }

            // Get the global bounding boxes for each layer as a Rect2 on the XZ plane.
            Rect2 boundsA = GetLayerBoundsXZ(layerA);
            Rect2 boundsB = GetLayerBoundsXZ(layerB);

            // Use Godot's built-in Rect2 intersection test.
            return boundsA.Intersects(boundsB);
        }

        /// <summary>
        /// Helper function to create a Rect2 representing the layer's footprint on the XZ plane.
        /// </summary>
        private static Rect2 GetLayerBoundsXZ(TerrainLayerBase layer)
        {
            Vector3 globalPos = layer.GlobalPosition;
            Vector2I size = layer.Size;

            // The layer's position is its center. We need the top-left corner for Rect2.
            Vector2 positionXZ = new Vector2(globalPos.X - size.X / 2.0f, globalPos.Z - size.Y / 2.0f);
            Vector2 sizeXZ = new Vector2(size.X, size.Y);

            return new Rect2(positionXZ, sizeXZ);
        }

        private static Rect2 GetLayerWorldBounds(TerrainLayerBase layer)
        {
            var worldPos = new Vector2(layer.GlobalPosition.X, layer.GlobalPosition.Z);
            var size = new Vector2(layer.Size.X, layer.Size.Y);
            var center = worldPos;
            var halfSize = size * 0.5f;
            
            return new Rect2(center - halfSize, size);
        }
    }
}