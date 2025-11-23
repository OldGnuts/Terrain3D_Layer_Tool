using Godot;
using System;

namespace Terrain3DWrapper
{
    /// <summary>
    /// Main Terrain3D wrapper class
    /// </summary>
    public class Terrain3D : IDisposable
    {
        private GodotObject _terrain;
        private bool _disposed = false;
        private bool _ownsTerrainInstance = false;

        public Terrain3DData Data { get; private set; }
        public Terrain3DCollision Collision { get; private set; }

        /// <summary>
        /// Verifies if Terrain3D is a valid class, and if the node is a Terrain3D node
        /// </summary>
        /// <param name="node"> A Terrain3D node assigned in the editor inspector </param>
        /// <returns></returns>
        

        #region Properties (from Terrain3D API)

            /// <summary>
            /// Gets or sets the terrain assets
            /// </summary>
        public GodotObject Assets
        {
            get => _terrain?.Get("assets").SafeAsGodotObject();
            set => _terrain?.Set("assets", value);
        }

        /// <summary>
        /// Gets or sets the cast shadow setting
        /// </summary>
        public GeometryInstance3D.ShadowCastingSetting CastShadow
        {
            get => (GeometryInstance3D.ShadowCastingSetting)_terrain?.Get("cast_shadow").SafeAsInt32();
            set => _terrain?.Set("cast_shadow", (int)value);
        }

        /// <summary>
        /// Gets or sets whether collision is enabled
        /// </summary>
        public bool CollisionEnabled
        {
            get => _terrain?.Get("collision_enabled").SafeAsBool() ?? false;
            set => _terrain?.Set("collision_enabled", value);
        }

        /// <summary>
        /// Gets or sets the collision layer
        /// </summary>
        public uint CollisionLayer
        {
            get => _terrain?.Get("collision_layer").SafeAsUInt32() ?? 0;
            set => _terrain?.Set("collision_layer", value);
        }

        /// <summary>
        /// Gets or sets the collision mask
        /// </summary>
        public uint CollisionMask
        {
            get => _terrain?.Get("collision_mask").SafeAsUInt32() ?? 0;
            set => _terrain?.Set("collision_mask", value);
        }

        /// <summary>
        /// Gets or sets the terrain data
        /// </summary>
        public GodotObject TerrainData
        {
            get => _terrain?.Get("data").SafeAsGodotObject();
            set
            {
                _terrain?.Set("data", value);
                // Recreate data wrapper
                Data?.Dispose();
                if (value != null)
                    Data = new Terrain3DData(value);
            }
        }

        /// <summary>
        /// Gets or sets the GI mode
        /// </summary>
        public GeometryInstance3D.GIModeEnum GIMode
        {
            get => (GeometryInstance3D.GIModeEnum)_terrain?.Get("gi_mode").SafeAsInt32();
            set => _terrain?.Set("gi_mode", (int)value);
        }

        /// <summary>
        /// Gets or sets whether to use 16-bit height maps
        /// </summary>
        public bool Height16Bit
        {
            get => _terrain?.Get("height_16bit").SafeAsBool() ?? false;
            set => _terrain?.Set("height_16bit", value);
        }

        /// <summary>
        /// Gets or sets the render layers
        /// </summary>
        public uint Layers
        {
            get => _terrain?.Get("layers").SafeAsUInt32() ?? 0;
            set => _terrain?.Set("layers", value);
        }

        /// <summary>
        /// Gets or sets the material
        /// </summary>
        public Material Material
        {
            get => _terrain?.Get("material").SafeAsGodotObject<Material>();
            set => _terrain?.Set("material", value);
        }

        /// <summary>
        /// Gets or sets the mesh LOD levels
        /// </summary>
        public int MeshLods
        {
            get => _terrain?.Get("mesh_lods").SafeAsInt32() ?? 0;
            set => _terrain?.Set("mesh_lods", value);
        }

        /// <summary>
        /// Gets or sets the mesh vertex spacing
        /// </summary>
        public float MeshVertexSpacing
        {
            get => _terrain?.Get("mesh_vertex_spacing").SafeAsSingle() ?? 0.0f;
            set => _terrain?.Set("mesh_vertex_spacing", value);
        }

        /// <summary>
        /// Gets or sets the region size
        /// </summary>
        public int RegionSize
        {
            get => _terrain?.Get("region_size").SafeAsInt32() ?? 1024;
            set => _terrain?.Set("region_size", value);
        }

        /// <summary>
        /// Gets or sets whether to show debug collision
        /// </summary>
        public bool ShowDebugCollision
        {
            get => _terrain?.Get("show_debug_collision").SafeAsBool() ?? false;
            set => _terrain?.Set("show_debug_collision", value);
        }

        /// <summary>
        /// Gets or sets the version string
        /// </summary>
        public string Version
        {
            get => _terrain?.Get("version").SafeAsString("Unknown");
            set => _terrain?.Set("version", value);
        }

        #endregion

        #region Constructors

        public Terrain3D(GodotObject terrain)
        {
            _terrain = terrain ?? throw new ArgumentNullException(nameof(terrain));
            _ownsTerrainInstance = false;
            Initialize();
        }

        public Terrain3D()
        {
            CreateNewTerrain();
            _ownsTerrainInstance = true;
            Initialize();
        }

        public Terrain3D(Node parent) : this()
        {
            if (parent != null && _terrain is Node terrainNode)
            {
                parent.AddChild(terrainNode);
            }
        }

        #endregion

        #region Initialization

        private void CreateNewTerrain()
        {
            _terrain = ClassDB.Instantiate("Terrain3D").AsGodotObject();

            if (_terrain == null)
                throw new Terrain3DException("Failed to instantiate Terrain3D. Make sure the Terrain3D plugin is installed and enabled.");

            GD.Print("Created new Terrain3D instance");
        }

        private void Initialize()
        {
            try
            {
                // Validate that it's actually a Terrain3D node
                if (!_terrain.HasMethod("get_data"))
                    throw new Terrain3DException("Provided object is not a valid Terrain3D instance.");

                // Initialize data wrapper
                var dataObject = _terrain.Get("data").SafeAsGodotObject();
                if (dataObject != null)
                {
                    Data = new Terrain3DData(dataObject);
                }

                // Initialize collision wrapper
                Collision = new Terrain3DCollision(_terrain);

                GD.Print($"Terrain3D wrapper initialized successfully. Version: {Version}");
            }
            catch (Exception ex)
            {
                throw new Terrain3DException("Failed to initialize Terrain3D wrapper", ex);
            }
        }

        #endregion

        #region Methods (from Terrain3D API)

        /// <summary>
        /// Gets the underlying GodotObject for advanced operations
        /// </summary>
        public GodotObject GetUnderlyingObject() => _terrain;

        /// <summary>
        /// Get the terrain data
        /// </summary>
        public GodotObject GetData()
        {
            return _terrain?.Call("get_data").SafeAsGodotObject();
        }

        /// <summary>
        /// Set the terrain data
        /// </summary>
        public void SetData(GodotObject data)
        {
            _terrain?.Call("set_data", data);
            Data?.Dispose();
            if (data != null)
            {
                Data = new Terrain3DData(data);
            }
        }

        /// <summary>
        /// Get the terrain assets
        /// </summary>
        public GodotObject GetAssets()
        {
            return _terrain?.Call("get_assets").SafeAsGodotObject();
        }

        /// <summary>
        /// Set the terrain assets
        /// </summary>
        public void SetAssets(GodotObject assets)
        {
            _terrain?.Call("set_assets", assets);
        }

        /// <summary>
        /// Get camera instance ID
        /// </summary>
        public ulong GetCameraId()
        {
            return _terrain?.Call("get_camera_id").SafeAsUInt32() ?? 0;
        }

        /// <summary>
        /// Set camera instance ID
        /// </summary>
        public void SetCameraId(ulong cameraId)
        {
            _terrain?.Call("set_camera_id", cameraId);
        }

        /// <summary>
        /// Get current editor
        /// </summary>
        public GodotObject GetEditor()
        {
            return _terrain?.Call("get_editor").SafeAsGodotObject();
        }

        /// <summary>
        /// Set current editor
        /// </summary>
        public void SetEditor(GodotObject editor)
        {
            _terrain?.Call("set_editor", editor);
        }

        /// <summary>
        /// Get the plugin class
        /// </summary>
        public GodotObject GetPlugin()
        {
            return _terrain?.Call("get_plugin").SafeAsGodotObject();
        }

        /// <summary>
        /// Set the plugin class
        /// </summary>
        public void SetPlugin(GodotObject plugin)
        {
            _terrain?.Call("set_plugin", plugin);
        }

        /// <summary>
        /// Set debug level
        /// </summary>
        public void SetDebugLevel(int level)
        {
            _terrain?.Call("set_debug_level", level);
        }

        /// <summary>
        /// Get debug level
        /// </summary>
        public int GetDebugLevel()
        {
            return _terrain?.Call("get_debug_level").SafeAsInt32() ?? 0;
        }

        /// <summary>
        /// Update terrain
        /// </summary>
        public void UpdateTerrain()
        {
            _terrain?.Call("update_terrain");
        }

        #endregion

        #region Disposal

        public void Dispose()
        {
            if (!_disposed)
            {
                Data?.Dispose();
                Collision?.Dispose();

                // Only free the terrain if we created it
                if (_terrain != null && _ownsTerrainInstance && _terrain is Node terrainNode)
                {
                    terrainNode.QueueFree();
                }
                _terrain = null;

                _disposed = true;
            }
        }

        #endregion
    }
}