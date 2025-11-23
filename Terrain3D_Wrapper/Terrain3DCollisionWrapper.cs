using Godot;
using System;

namespace Terrain3DWrapper
{
    /// <summary>
    /// Wrapper for Terrain3DCollision - handles terrain collision functionality
    /// </summary>
    public class Terrain3DCollision : IDisposable
    {
        private readonly GodotObject _collision;
        private bool _disposed = false;

        #region Properties (from Terrain3DCollision API)

        /// <summary>
        /// Gets or sets the collision layer
        /// </summary>
        public uint CollisionLayer
        {
            get => _collision?.Get("collision_layer").SafeAsUInt32() ?? 0;
            set => _collision?.Set("collision_layer", value);
        }

        /// <summary>
        /// Gets or sets the collision mask
        /// </summary>
        public uint CollisionMask
        {
            get => _collision?.Get("collision_mask").SafeAsUInt32() ?? 0;
            set => _collision?.Set("collision_mask", value);
        }

        /// <summary>
        /// Gets or sets whether collision is enabled
        /// </summary>
        public bool Enabled
        {
            get => _collision?.Get("enabled").SafeAsBool() ?? false;
            set => _collision?.Set("enabled", value);
        }

        /// <summary>
        /// Gets or sets the priority for collision processing
        /// </summary>
        public float Priority
        {
            get => _collision?.Get("priority").SafeAsSingle() ?? 0f;
            set => _collision?.Set("priority", value);
        }

        /// <summary>
        /// Gets or sets whether debug collision shapes are shown
        /// </summary>
        public bool ShowDebugCollision
        {
            get => _collision?.Get("show_debug_collision").SafeAsBool() ?? false;
            set => _collision?.Set("show_debug_collision", value);
        }

        #endregion

        #region Constructors

        /// <summary>
        /// Creates a wrapper around an existing Terrain3DCollision GodotObject
        /// </summary>
        /// <param name="collision">The Terrain3DCollision GodotObject to wrap</param>
        public Terrain3DCollision(GodotObject collision)
        {
            _collision = collision ?? throw new ArgumentNullException(nameof(collision));
        }

        #endregion

        #region Methods (from Terrain3DCollision API)

        /// <summary>
        /// Gets the underlying GodotObject for advanced operations
        /// </summary>
        public GodotObject GetUnderlyingObject() => _collision;

        /// <summary>
        /// Create collision shape for a region
        /// </summary>
        /// <param name="regionId">ID of the region</param>
        /// <param name="typeId">Type ID for collision shape</param>
        public void CreateCollision(int regionId, int typeId = 0)
        {
            _collision?.Call("create_collision", regionId, typeId);
        }

        /// <summary>
        /// Destroy collision for all regions
        /// </summary>
        public void DestroyCollision()
        {
            _collision?.Call("destroy_collision");
        }

        /// <summary>
        /// Get collision layer bit
        /// </summary>
        /// <param name="layerNumber">Layer number (1-32)</param>
        /// <returns>True if layer bit is set</returns>
        public bool GetCollisionLayerValue(int layerNumber)
        {
            return _collision?.Call("get_collision_layer_value", layerNumber).SafeAsBool() ?? false;
        }

        /// <summary>
        /// Get collision mask bit
        /// </summary>
        /// <param name="layerNumber">Layer number (1-32)</param>
        /// <returns>True if mask bit is set</returns>
        public bool GetCollisionMaskValue(int layerNumber)
        {
            return _collision?.Call("get_collision_mask_value", layerNumber).SafeAsBool() ?? false;
        }

        /// <summary>
        /// Get collision body for a region
        /// </summary>
        /// <param name="regionId">ID of the region</param>
        /// <returns>StaticBody3D or null if not found</returns>
        public StaticBody3D GetRegionBody(int regionId)
        {
            return _collision?.Call("get_region_body", regionId).SafeAsGodotObject<StaticBody3D>();
        }

        /// <summary>
        /// Get collision shape for a region
        /// </summary>
        /// <param name="regionId">ID of the region</param>
        /// <returns>CollisionShape3D or null if not found</returns>
        public CollisionShape3D GetRegionShape(int regionId)
        {
            return _collision?.Call("get_region_shape", regionId).SafeAsGodotObject<CollisionShape3D>();
        }

        /// <summary>
        /// Remove collision for a specific region
        /// </summary>
        /// <param name="regionId">ID of the region</param>
        public void RemoveCollision(int regionId)
        {
            _collision?.Call("remove_collision", regionId);
        }

        /// <summary>
        /// Set collision layer bit
        /// </summary>
        /// <param name="layerNumber">Layer number (1-32)</param>
        /// <param name="value">True to set bit, false to clear</param>
        public void SetCollisionLayerValue(int layerNumber, bool value)
        {
            _collision?.Call("set_collision_layer_value", layerNumber, value);
        }

        /// <summary>
        /// Set collision mask bit
        /// </summary>
        /// <param name="layerNumber">Layer number (1-32)</param>
        /// <param name="value">True to set bit, false to clear</param>
        public void SetCollisionMaskValue(int layerNumber, bool value)
        {
            _collision?.Call("set_collision_mask_value", layerNumber, value);
        }

        /// <summary>
        /// Update collision for all regions
        /// </summary>
        public void UpdateCollision()
        {
            _collision?.Call("update_collision");
        }

        /// <summary>
        /// Update collision for a specific region
        /// </summary>
        /// <param name="regionId">ID of the region</param>
        public void UpdateRegionCollision(int regionId)
        {
            _collision?.Call("update_region_collision", regionId);
        }

        #endregion

        #region Convenience Methods

        /// <summary>
        /// Enable collision on a specific layer
        /// </summary>
        /// <param name="layerNumber">Layer number (1-32)</param>
        public void EnableCollisionLayer(int layerNumber)
        {
            SetCollisionLayerValue(layerNumber, true);
        }

        /// <summary>
        /// Disable collision on a specific layer
        /// </summary>
        /// <param name="layerNumber">Layer number (1-32)</param>
        public void DisableCollisionLayer(int layerNumber)
        {
            SetCollisionLayerValue(layerNumber, false);
        }

        /// <summary>
        /// Enable collision mask on a specific layer
        /// </summary>
        /// <param name="layerNumber">Layer number (1-32)</param>
        public void EnableCollisionMask(int layerNumber)
        {
            SetCollisionMaskValue(layerNumber, true);
        }

        /// <summary>
        /// Disable collision mask on a specific layer
        /// </summary>
        /// <param name="layerNumber">Layer number (1-32)</param>
        public void DisableCollisionMask(int layerNumber)
        {
            SetCollisionMaskValue(layerNumber, false);
        }

        /// <summary>
        /// Check if collision exists for a region
        /// </summary>
        /// <param name="regionId">ID of the region</param>
        /// <returns>True if collision exists for the region</returns>
        public bool HasRegionCollision(int regionId)
        {
            return GetRegionBody(regionId) != null;
        }

        /// <summary>
        /// Set collision layer to a specific layer only
        /// </summary>
        /// <param name="layerNumber">Layer number (1-32)</param>
        public void SetSingleCollisionLayer(int layerNumber)
        {
            CollisionLayer = (uint)(1 << (layerNumber - 1));
        }

        /// <summary>
        /// Set collision mask to a specific layer only
        /// </summary>
        /// <param name="layerNumber">Layer number (1-32)</param>
        public void SetSingleCollisionMask(int layerNumber)
        {
            CollisionMask = (uint)(1 << (layerNumber - 1));
        }

        /// <summary>
        /// Add a layer to the collision layer mask
        /// </summary>
        /// <param name="layerNumber">Layer number (1-32)</param>
        public void AddCollisionLayer(int layerNumber)
        {
            CollisionLayer |= (uint)(1 << (layerNumber - 1));
        }

        /// <summary>
        /// Remove a layer from the collision layer mask
        /// </summary>
        /// <param name="layerNumber">Layer number (1-32)</param>
        public void RemoveCollisionLayer(int layerNumber)
        {
            CollisionLayer &= ~(uint)(1 << (layerNumber - 1));
        }

        /// <summary>
        /// Add a layer to the collision mask
        /// </summary>
        /// <param name="layerNumber">Layer number (1-32)</param>
        public void AddCollisionMask(int layerNumber)
        {
            CollisionMask |= (uint)(1 << (layerNumber - 1));
        }

        /// <summary>
        /// Remove a layer from the collision mask
        /// </summary>
        /// <param name="layerNumber">Layer number (1-32)</param>
        public void RemoveCollisionMask(int layerNumber)
        {
            CollisionMask &= ~(uint)(1 << (layerNumber - 1));
        }

        /// <summary>
        /// Clear all collision layers
        /// </summary>
        public void ClearCollisionLayers()
        {
            CollisionLayer = 0;
        }

        /// <summary>
        /// Clear all collision masks
        /// </summary>
        public void ClearCollisionMasks()
        {
            CollisionMask = 0;
        }

        /// <summary>
        /// Enable collision and update all regions
        /// </summary>
        public void EnableAndUpdate()
        {
            Enabled = true;
            UpdateCollision();
        }

        /// <summary>
        /// Disable collision and destroy all collision shapes
        /// </summary>
        public void DisableAndDestroy()
        {
            Enabled = false;
            DestroyCollision();
        }

        /// <summary>
        /// Check if a specific layer number is valid (1-32)
        /// </summary>
        /// <param name="layerNumber">Layer number to validate</param>
        /// <returns>True if layer number is valid</returns>
        public static bool IsValidLayerNumber(int layerNumber)
        {
            return layerNumber >= 1 && layerNumber <= 32;
        }

        /// <summary>
        /// Get all active collision layer numbers
        /// </summary>
        /// <returns>Array of active layer numbers</returns>
        public int[] GetActiveCollisionLayers()
        {
            var activeLayers = new System.Collections.Generic.List<int>();
            for (int i = 1; i <= 32; i++)
            {
                if (GetCollisionLayerValue(i))
                {
                    activeLayers.Add(i);
                }
            }
            return activeLayers.ToArray();
        }

        /// <summary>
        /// Get all active collision mask numbers
        /// </summary>
        /// <returns>Array of active mask numbers</returns>
        public int[] GetActiveCollisionMasks()
        {
            var activeMasks = new System.Collections.Generic.List<int>();
            for (int i = 1; i <= 32; i++)
            {
                if (GetCollisionMaskValue(i))
                {
                    activeMasks.Add(i);
                }
            }
            return activeMasks.ToArray();
        }

        /// <summary>
        /// Set multiple collision layers at once
        /// </summary>
        /// <param name="layerNumbers">Array of layer numbers to enable</param>
        /// <param name="clearOthers">Whether to clear other layers first</param>
        public void SetCollisionLayers(int[] layerNumbers, bool clearOthers = true)
        {
            if (clearOthers)
                ClearCollisionLayers();

            foreach (int layerNumber in layerNumbers)
            {
                if (IsValidLayerNumber(layerNumber))
                    EnableCollisionLayer(layerNumber);
            }
        }

        /// <summary>
        /// Set multiple collision masks at once
        /// </summary>
        /// <param name="layerNumbers">Array of layer numbers to enable</param>
        /// <param name="clearOthers">Whether to clear other masks first</param>
        public void SetCollisionMasks(int[] layerNumbers, bool clearOthers = true)
        {
            if (clearOthers)
                ClearCollisionMasks();

            foreach (int layerNumber in layerNumbers)
            {
                if (IsValidLayerNumber(layerNumber))
                    EnableCollisionMask(layerNumber);
            }
        }

        #endregion

        #region Disposal

        public void Dispose()
        {
            if (!_disposed)
            {
                // Note: We don't free the underlying GodotObject as it's managed by Terrain3D
                _disposed = true;
            }
        }

        #endregion
    }
}