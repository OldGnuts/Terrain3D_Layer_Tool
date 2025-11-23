using Godot;
using System;
using Godot.Collections;

namespace Terrain3DWrapper
{
    /// <summary>
    /// Wrapper for Terrain3DData - manages terrain regions and data operations
    /// </summary>
    public class Terrain3DData : IDisposable
    {
        private readonly GodotObject _data;
        private bool _disposed = false;

        #region Properties (from Terrain3DData API)

        /// <summary>
        /// Gets or sets the color maps array
        /// </summary>
        public Godot.Collections.Array ColorMaps
        {
            get => _data?.Get("color_maps").As<Godot.Collections.Array>();
            set => _data?.Set("color_maps", value);
        }

        /// <summary>
        /// Gets or sets the control maps array
        /// </summary>
        public Godot.Collections.Array ControlMaps
        {
            get => _data?.Get("control_maps").As<Godot.Collections.Array>();
            set => _data?.Set("control_maps", value);
        }

        /// <summary>
        /// Gets or sets whether to generate foliage
        /// </summary>
        public bool GenerateFoliage
        {
            get => _data?.Get("generate_foliage").SafeAsBool() ?? false;
            set => _data?.Set("generate_foliage", value);
        }

        /// <summary>
        /// Gets or sets the height maps array
        /// </summary>
        public Godot.Collections.Array HeightMaps
        {
            get => _data?.Get("height_maps").As<Godot.Collections.Array>();
            set => _data?.Set("height_maps", value);
        }

        /// <summary>
        /// Gets or sets the multimesh dictionary
        /// </summary>
        public Godot.Collections.Dictionary Multimeshes
        {
            get => _data?.Get("multimeshes").As<Godot.Collections.Dictionary>();
            set => _data?.Set("multimeshes", value);
        }

        /// <summary>
        /// Gets or sets the region locations array
        /// </summary>
        public Godot.Collections.Array RegionLocations
        {
            get => _data?.Get("region_locations").As<Godot.Collections.Array>();
            set => _data?.Set("region_locations", value);
        }

        /// <summary>
        /// Gets or sets the region map dictionary
        /// </summary>
        public Godot.Collections.Dictionary RegionMap
        {
            get => _data?.Get("region_map").As<Godot.Collections.Dictionary>();
            set => _data?.Set("region_map", value);
        }

        /// <summary>
        /// Gets or sets the save directory
        /// </summary>
        public string SaveDirectory
        {
            get => _data?.Get("save_directory").SafeAsString();
            set => _data?.Set("save_directory", value);
        }

        /// <summary>
        /// Gets or sets the version
        /// </summary>
        public float DataVersion
        {
            get => _data?.Get("version").SafeAsSingle() ?? 0.0f;
            set => _data?.Set("version", value);
        }

        #endregion

        #region Constructors

        /// <summary>
        /// Creates a wrapper around an existing Terrain3DData GodotObject
        /// </summary>
        /// <param name="data">The Terrain3DData GodotObject to wrap</param>
        public Terrain3DData(GodotObject data)
        {
            _data = data ?? throw new ArgumentNullException(nameof(data));
        }

        #endregion

        #region Methods (from Terrain3DData API)

        /// <summary>
        /// Gets the underlying GodotObject for advanced operations
        /// </summary>
        public GodotObject GetUnderlyingObject() => _data;

        /// <summary>
        /// Add region at location
        /// </summary>
        /// <param name="regionLoc">Region location</param>
        /// <param name="update">Whether to update terrain after adding</param>
        /// <returns>Region ID or -1 if failed</returns>
        public Terrain3DRegion AddRegionBlank(Vector2I regionLoc, bool update = true)
        {
            Terrain3DRegion region = new Terrain3DRegion(_data?.Call("add_region_blank", regionLoc, update).SafeAsGodotObject());
            return region;
        }

        /// <summary>
        /// Add region at global position
        /// </summary>
        /// <param name="globalPosition"></param>
        /// <param name="update"></param>
        /// <returns></returns>
        public Terrain3DRegion AddRegionBlankP(Vector3 globalPosition, bool update = true)
        {
            Terrain3DRegion region = new Terrain3DRegion(_data?.Call("add_region_blankp", globalPosition, update).SafeAsGodotObject());
            return region;
        }

        /// <summary>
        /// Calculate axis-aligned bounding box for all regions
        /// </summary>
        /// <returns>AABB containing all regions</returns>
        public Aabb CalcAabb()
        {
            return _data?.Call("calc_aabb").SafeAsAabb() ?? new Aabb();
        }

        /// <summary>
        /// Export an image of specified map type
        /// </summary>
        /// <param name="mapType">Type of map to export</param>
        /// <param name="path">File path to save to</param>
        /// <returns>Error code</returns>
        public Error ExportImage(string fileName, int mapType)
        {
            return (Error)_data?.Call("export_image", fileName, mapType).SafeAsInt32();
        }

        /// <summary>
        /// Get color at world position
        /// </summary>
        /// <param name="globalPosition">World position</param>
        /// <returns>Color at position</returns>
        public Color GetColor(Vector3 globalPosition)
        {
            return _data?.Call("get_color", globalPosition).SafeAsColor() ?? new Color(.5f, 0, 0, 1);
        }

        /// <summary>
        /// Get height at world position
        /// </summary>
        /// <param name="globalPosition">World position</param>
        /// <returns>Height at position</returns>
        public float GetHeight(Vector3 globalPosition)
        {
            return _data?.Call("get_height", globalPosition).SafeAsSingle() ?? 0f;
        }

        /// <summary>
        /// Get height range across all regions
        /// </summary>
        /// <returns>Vector2 with min height (x) and max height (y)</returns>
        public Vector2 GetHeightRange()
        {
            return _data?.Call("get_height_range").SafeAsVector2() ?? new Vector2(0, 0);
        }

        /// <summary>
        /// Get map image at region location
        /// </summary>
        /// <param name="mapType">Type of map</param>
        /// <param name="regionLoc">Region location</param>
        /// <returns>Image or null if not found</returns>
        public Image GetMaps(int mapType, Vector2I regionLoc)
        {
            return _data?.Call("get_maps", mapType).SafeAsGodotObject<Image>();
        }

        /// <summary>
        /// Get map image by region ID
        /// </summary>
        /// <param name="mapType">Type of map</param>
        /// <param name="regionId">Region ID</param>
        /// <returns>Image or null if not found</returns>
        public Image GetMapById(int mapType, int regionId)
        {
            return _data?.Call("get_map_by_id", mapType, regionId).SafeAsGodotObject<Image>();
        }

        /// <summary>
        /// Get normal at world position
        /// </summary>
        /// <param name="globalPosition">World position</param>
        /// <returns>Normal vector at position</returns>
        public Vector3 GetNormal(Vector3 globalPosition)
        {
            return _data?.Call("get_normal", globalPosition).SafeAsVector3() ?? new Vector3(0, 0, 0);
        }

        /// <summary>
        /// Get pixel value from map at world position
        /// </summary>
        /// <param name="mapType">Type of map</param>
        /// <param name="globalPosition">World position</param>
        /// <returns>Pixel value</returns>
        public float GetPixel(int mapType, Vector3 globalPosition)
        {
            return _data?.Call("get_pixel", mapType, globalPosition).SafeAsSingle() ?? 0f;
        }

        /// <summary>
        /// Get the Terrain3DRegion at location (x,x)
        /// </summary>
        /// <param name="regionLoc"> (x,x)</param>
        /// <returns>Return the Terrain3DRegion at the specified location. This will return inactive regions marked for deletion. Check with Terrain3DRegion.deleted.</returns>
        public Terrain3DRegion GetRegion(Vector2I regionLoc)
        {
            Terrain3DRegion region = new Terrain3DRegion(_data?.Call("get_region", regionLoc).SafeAsGodotObject());
            return region;
        }

        /// <summary>
        /// Get region count
        /// </summary>
        /// <returns>Number of regions</returns>
        public int GetRegionCount()
        {
            return _data?.Call("get_region_count").SafeAsInt32() ?? 0;
        }

        /// <summary>
        /// Get region ID from location
        /// </summary>
        /// <param name="regionLoc">Region location</param>
        /// <returns>Region ID or -1 if not found</returns>
        public int GetRegionId(Vector2I regionLoc)
        {
            return _data?.Call("get_region_id", regionLoc).SafeAsInt32() ?? -1;
        }

        /// <summary>
        /// Get region ID from global position
        /// </summary>
        /// <param name="globalPosition">World position</param>
        /// <returns>Region ID or -1 if not found</returns>
        public int GetRegionIdP(Vector3 globalPosition)
        {
            return _data?.Call("get_region_idp", globalPosition).SafeAsInt32() ?? -1;
        }

        /// <summary>
        /// Get region location from global position
        /// </summary>
        /// <param name="globalPosition">Region ID</param>
        /// <returns>Region location</returns>
        public Vector2I GetRegionLocation(Vector3 globalPosition)
        {
            return _data?.Call("get_region_location", globalPosition).SafeAsVector2I() ?? new Vector2I(0, 0);
        }

        /// <summary>
        /// Get the Terrain3DRegion at location (x,x)
        /// </summary>
        /// <param name="regionLoc"> (x,x)</param>
        /// <returns>Return the Terrain3DRegion at the specified location. This will return inactive regions marked for deletion. Check with Terrain3DRegion.deleted.</returns>
        Terrain3DRegion GetRegionP(Vector3 globalPosition)
        {
            Terrain3DRegion region = new Terrain3DRegion(_data?.Call("get_regionp", globalPosition).SafeAsGodotObject());
            return region;
        }

        /// <summary>
        /// Get roughness at world position
        /// </summary>
        /// <param name="globalPosition">World position</param>
        /// <returns>Roughness value</returns>
        public float GetRoughness(Vector3 globalPosition)
        {
            return _data?.Call("get_roughness", globalPosition).SafeAsSingle() ?? 0f;
        }

        /// <summary>
        /// Gets the texture id's and blend values at a global position
        /// </summary>
        /// <param name="globalPosition"> Global position </param>
        /// <returns>Vector3(base texture id, overlay id, blend value)</returns>
        public Vector3 GetTextureId(Vector3 globalPosition)
        {
            return _data?.Call("get_texture_id", globalPosition).SafeAsVector3() ?? new Vector3(0, 0, 0);
        }

        /// <summary>
        /// Check if region exists at location
        /// </summary>
        /// <param name="regionLoc">Region location</param>
        /// <returns>True if region exists</returns>
        public bool HasRegion(Vector2I regionLoc)
        {
            return _data?.Call("has_region", regionLoc).SafeAsBool() ?? false;
        }

        /// <summary>
        /// Check if region exists at location
        /// </summary>
        /// <param name="globalPosition">Global position</param>
        /// <returns>True if region exists</returns>
        public bool HasRegionP(Vector3 globalPosition)
        {
            return _data?.Call("has_regionp", globalPosition).SafeAsBool() ?? false;
        }


        /// <summary>
        /// Import images as terrain data
        /// </summary>
        /// <param name="images">Array of images to import</param>
        /// <param name="globalPosition">World position to import at</param>
        /// <param name="offset">Height offset</param>
        /// <param name="scale">Height scale</param>
        /// <returns>Error code</returns>
        public Error ImportImages(Godot.Collections.Array images, Vector3 globalPosition, float offset = 0f, float scale = 1f)
        {
            return (Error)_data?.Call("import_images", images, globalPosition, offset, scale).SafeAsInt32();
        }

        /// <summary>
        /// Returns true if the slope of the terrain at the given position is within the slope range. If invert is true, it returns true if the position is outside the given range.
        /// </summary>
        /// <param name="globalPosition"></param>
        /// <param name="slopeRange"></param>
        /// <param name="invert"></param>
        /// <returns></returns>

        public bool IsInSlope(Vector3 globalPosition, Vector2 slopeRange, bool invert = false)
        {
            return _data?.Call("is_in_slope", globalPosition, slopeRange, invert).SafeAsBool() ?? false;
        }

        /// <summary>
        /// Returns true if the region at the location exists and is marked as deleted. Syntactic sugar for Terrain3DRegion.deleted.
        /// </summary>
        /// <param name="regionLoc"></param>
        /// <returns></returns>
        public bool IsRegionDeleted(Vector2I regionLoc)
        {
            return _data?.Call("is_region_deleted", regionLoc).SafeAsBool() ?? false;
        }

        /// <summary>
        /// Returns true if the region at the location exists and is marked as modified. Syntactic sugar for Terrain3DRegion.modified.
        /// </summary>
        /// <param name="regionLoc"></param>
        /// <returns></returns>
        public bool IsRegionModified(Vector2I regionLoc)
        {
            return _data?.Call("is_region_modified", regionLoc).SafeAsBool() ?? false;
        }


        /// <summary>
        /// Remove region at location
        /// </summary>
        /// <param name="regionLoc">Region location</param>
        /// <param name="updateTerrain">Whether to update terrain after removal</param>
        public void RemoveRegionL(Vector2I regionLoc, bool updateTerrain = true)
        {
            _data?.Call("remove_regionl", regionLoc, updateTerrain);
        }

        /// <summary>
        /// Remove region at location
        /// </summary>
        /// <param name="regionLoc">Region location</param>
        /// <param name="updateTerrain">Whether to update terrain after removal</param>
        public void RemoveRegion(GodotObject region, bool updateTerrain = true)
        {
            _data?.Call("remove_region", region, updateTerrain);
        }

        /// <summary>
        /// Removes the region at the specified global position.
        /// </summary>
        public void RemoveRegionP(Vector3 globalPosition, bool update = true)
        {
            _data?.Call("remove_regionp", globalPosition, update);
        }

        /// <summary>
        /// Saves the specified active region to the directory. See Terrain3DRegion.save().
        /// </summary>
        /// <param name="regionLoc">the region to save</param>
        /// <param name="directory"></param>
        /// <param name="save16Bit">16_bit - converts the edited 32-bit heightmap to 16-bit. This is a lossy operation.</param>
        public void SaveRegion(Vector2I regionLoc, string directory = "", bool save16Bit = false)
        {
            _data?.Call("save_region", regionLoc, directory, save16Bit).SafeAsInt32();
        }

        /// <summary>
        /// Set color at world position
        /// </summary>
        /// <param name="globalPosition">World position</param>
        /// <param name="color">Color to set</param>
        public void SetColor(Vector3 globalPosition, Color color)
        {
            _data?.Call("set_color", globalPosition, color);
        }

        /// <summary>
        /// Set height at world position
        /// </summary>
        /// <param name="globalPosition">World position</param>
        /// <param name="height">Height to set</param>
        public void SetHeight(Vector3 globalPosition, float height)
        {
            _data?.Call("set_height", globalPosition, height);
        }

        public void SetPixel(int mapType, Vector3 globalPosition, float value)
        {
            _data?.Call("set_pixel", mapType, globalPosition, value);
        }

        /// <summary>
        /// Set roughness at world position
        /// </summary>
        /// <param name="globalPosition">World position</param>
        /// <param name="roughness">Roughness to set</param>
        public void SetRoughness(Vector3 globalPosition, float roughness)
        {
            _data?.Call("set_roughness", globalPosition, roughness);
        }

        /// <summary>
        /// Regenerates the region map and the TextureArrays that combine the requested map types. This function needs to be called after editing any of the maps.
        /// </summary>
        /// <param name="mapType"> Regenerate only maps of this type </param>
        /// <param name="allRegions">Regenerate all regions if true, otherwise only those marked with Terrain3DRegion.edited </param>
        public void UpdateMaps(int mapType = 3, bool allRegions = true)
        {
            _data?.Call("update_maps", mapType, allRegions);
        }

        #endregion

        #region Helpers
        /// <summary>
        /// Gets all region locations that currently exist in Terrain3D storage.
        /// </summary>
        public System.Collections.Generic.List<Vector2I> GetRegionLocations()
        {
            var result = new System.Collections.Generic.List<Vector2I>();

            // Terrain3D API: get_region_locations() returns Array of Vector2i
            var locations = RegionLocations;

            if (locations is Godot.Collections.Array array)
            {
                foreach (var item in array)
                {
                    if (item.SafeAsVector2I() is Vector2I location)
                    {
                        result.Add(location);
                    }
                }
            }

            return result;
        }
        #endregion

        #region Disposal

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
            }
        }

        #endregion
    }
}