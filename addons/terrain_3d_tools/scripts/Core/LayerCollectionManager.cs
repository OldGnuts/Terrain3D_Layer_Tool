using Godot;
using System.Linq;
using Terrain3DTools.Layers;
using Terrain3DTools.Core.Debug;

namespace Terrain3DTools.Core
{
    /// <summary>
    /// Manages the collection of terrain layers, tracking additions and removals.
    /// </summary>
    public class LayerCollectionManager
    {
        private const string DEBUG_CLASS_NAME = "LayerCollectionManager";
        
        private readonly Node _owner;
        private Godot.Collections.Array<TerrainLayerBase> _layers = new();
        private int _previousLayerCount = 0;

        public LayerCollectionManager(Node owner)
        {
            _owner = owner;
            
            DebugManager.Instance?.RegisterClass(DEBUG_CLASS_NAME);
            DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.Initialization, 
                "LayerCollectionManager initialized");
        }

        /// <summary>
        /// Gets the current collection of terrain layers.
        /// </summary>
        public Godot.Collections.Array<TerrainLayerBase> Layers => _layers;

        /// <summary>
        /// Updates the layer collection by scanning the scene tree.
        /// Detects additions and removals.
        /// </summary>
        public void Update()
        {
            DebugManager.Instance?.StartTimer(DEBUG_CLASS_NAME, DebugCategory.LayerLifecycle, "Update");

            // Get all layer nodes from the scene tree
            var layerNodes = _owner.GetTree()
                .GetNodesInGroup("terrain_layer")
                .Cast<TerrainLayerBase>()
                .Where(l => GodotObject.IsInstanceValid(l))
                .ToArray();

            var newLayers = new Godot.Collections.Array<TerrainLayerBase>();
            
            // Build new collection
            foreach (var layer in layerNodes)
            {
                newLayers.Add(layer);
            }

            // Detect changes
            int addedCount = 0;
            int removedCount = 0;

            // Check for new layers
            foreach (var layer in newLayers)
            {
                if (!_layers.Contains(layer))
                {
                    addedCount++;
                    DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.LayerDetails, 
                        $"New layer detected: '{layer.LayerName}' ({layer.GetLayerType()})");
                }
            }

            // Check for removed layers
            foreach (var layer in _layers)
            {
                if (!newLayers.Contains(layer))
                {
                    removedCount++;
                    DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.LayerDetails, 
                        $"Layer removed: '{layer.LayerName}' ({layer.GetLayerType()})");
                }
            }

            _layers = newLayers;

            DebugManager.Instance?.EndTimer(DEBUG_CLASS_NAME, DebugCategory.LayerLifecycle, "Update");

            // Log summary if there were changes
            if (addedCount > 0 || removedCount > 0)
            {
                DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.LayerLifecycle, 
                    $"Layer collection updated - Added: {addedCount}, Removed: {removedCount}, Total: {_layers.Count}");
            }

            // Log periodic status if layer count changed
            if (_layers.Count != _previousLayerCount)
            {
                LogLayerBreakdown();
                _previousLayerCount = _layers.Count;
            }
        }

        /// <summary>
        /// Logs a breakdown of layers by type.
        /// </summary>
        private void LogLayerBreakdown()
        {
            var heightCount = _layers.Count(l => l.GetLayerType() == LayerType.Height);
            var textureCount = _layers.Count(l => l.GetLayerType() == LayerType.Texture);
            var featureCount = _layers.Count(l => l.GetLayerType() == LayerType.Feature);

            DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.PerformanceMetrics, 
                $"Layer breakdown - Height: {heightCount}, Texture: {textureCount}, Feature: {featureCount}, Total: {_layers.Count}");
        }

        /// <summary>
        /// Gets statistics about the current layer collection.
        /// </summary>
        public string GetStats()
        {
            var heightCount = _layers.Count(l => l.GetLayerType() == LayerType.Height);
            var textureCount = _layers.Count(l => l.GetLayerType() == LayerType.Texture);
            var featureCount = _layers.Count(l => l.GetLayerType() == LayerType.Feature);
            var dirtyCount = _layers.Count(l => l.IsDirty);

            return $"Layer Collection Stats:\n" +
                   $"  Total: {_layers.Count}\n" +
                   $"  Height: {heightCount}, Texture: {textureCount}, Feature: {featureCount}\n" +
                   $"  Dirty: {dirtyCount}";
        }
    }
}