using Godot;
using System;

namespace Terrain3DWrapper
{
    /// <summary>
    /// Wrapper for Terrain3DRegion - represents a single terrain region
    /// </summary>
    public class Terrain3DRegion : IDisposable
    {
        private readonly GodotObject _region;
        private bool _disposed = false;

        #region Properties (from Terrain3DRegion API)

        /// <summary>
        /// Gets or sets the color map for this region
        /// </summary>
        public Image ColorMap
        {
            get => _region?.Get("color_map").SafeAsGodotObject<Image>();
            set => _region?.Set("color_map", value);
        }

        /// <summary>
        /// Gets or sets the control map for this region
        /// </summary>
        public Image ControlMap
        {
            get => _region?.Get("control_map").SafeAsGodotObject<Image>();
            set => _region?.Set("control_map", value);
        }

        /// <summary>
        /// Gets or sets the control map for this region
        /// </summary>
        public bool Edited
        {
            get => (bool)(_region?.Get("edited").SafeAsBool());
            set => _region?.Set("control_map", value);
        }

        /// <summary>
        /// Gets or sets the height map for this region
        /// </summary>
        public Image HeightMap
        {
            get => _region?.Get("height_map").SafeAsGodotObject<Image>();
            set => _region?.Set("height_map", value);
        }

        public Vector2I location
        {
            get => _region?.Call("get_location").SafeAsVector2I() ?? new Vector2I(999, 999);
            set => _region?.Call("set_location", value);
        }


        /// <summary>
        /// Gets or sets whether this region has been modified
        /// </summary>
        public bool Modified
        {
            get => _region?.Get("modified").SafeAsBool() ?? false;
            set => _region?.Set("modified", value);
        }

        /// <summary>
        /// Gets or sets the version of this region
        /// </summary>
        public float RegionVersion
        {
            get => _region?.Get("version").SafeAsSingle() ?? 0.0f;
            set => _region?.Set("version", value);
        }

        #endregion

        #region Constructors

        /// <summary>
        /// Creates a wrapper around an existing Terrain3DRegion GodotObject
        /// </summary>
        /// <param name="region">The Terrain3DRegion GodotObject to wrap</param>
        public Terrain3DRegion(GodotObject region)
        {
            _region = region ?? throw new ArgumentNullException(nameof(region));
        }

        #endregion

        #region Methods (from Terrain3DRegion API)

        /// <summary>
        /// Gets the underlying GodotObject for advanced operations
        /// </summary>
        public GodotObject GetUnderlyingObject() => _region;

        /// <summary>
        /// Validates and adjusts the map size and format if possible, or creates a usable blank image in the right size and format.
        /// </summary>
        /// <param name="mapType"></param>
        /// <param name="map"></param>
        /// <returns></returns>

        public Image SanitizeMap(int mapType, Image map)
        {
            return _region?.Call("sanitize_map", mapType, map).SafeAsGodotObject<Image>();
        }

        /// <summary>
        /// Sanitizes all map types. See sanitize_map().
        /// </summary>
        public void SanitizeMaps()
        {
            _region?.Call("sanitize_maps");
        }

        /// <summary>
        /// Save region data to file
        /// </summary>
        /// <param name="path"> specifies a directory and file name to use from now on. </param>
        /// <returns>Error code</returns>
        public Error Save(string path = "", bool save16Bit = false)
        {
            return (Error)_region?.Call("save", path, save16Bit).SafeAsInt32();
        }

        /// <summary>
        /// Set map image by type
        /// </summary>
        /// <param name="mapType">Type of map (0=height, 1=control, 2=color)</param>
        /// <param name="image">Image to set</param>
        public void SetMap(int mapType, Image image)
        {
            _region?.Call("set_map", mapType, image);
        }

        #endregion

        #region Convenience Methods

        /// <summary>
        /// Set map by map type enum
        /// </summary>
        /// <param name="mapType">Map type enum</param>
        /// <param name="image">Image to set</param>
        public void SetMapByType(MapType mapType, Image image)
        {
            SetMap((int)mapType, image);
        }

        #endregion

        #region Disposal

        public void Dispose()
        {
            if (!_disposed)
            {
                // Note: We don't free the underlying GodotObject as it's managed by Terrain3DData
                _disposed = true;
            }
        }

        #endregion
    }
}