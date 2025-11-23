// /Core/TerrainLayerManager.cs
using Godot;
using System.Collections.Generic;
using Terrain3DWrapper;
using Terrain3DTools.Core.Debug;
using Terrain3DTools.Layers;
using Terrain3DTools.Utils;
using System.Linq;

namespace Terrain3DTools.Core
{
    [GlobalClass, Tool]
    public partial class TerrainLayerManager : Node3D
    {
        private enum UpdatePhase
        {
            Idle,
            Processing, // GPU work in progress
            ReadyToPush  // GPU work done, ready to sync
        }
        #region Fields
        [Export(PropertyHint.None, "Read-only in the editor")]
        private int _terrainLayerCountForInspector;

        private Terrain3DConnector _terrain3DConnector;
        private ScenePreviewManager _scenePreviewManager;
        private LayerCollectionManager _layerCollectionManager;
        private RegionDependencyManager _regionDependencyManager;
        private RegionMapManager _regionMapManager;
        public RegionMapManager GetRegionMapManager() => _regionMapManager;
        private TerrainUpdateProcessor _updateProcessor;
        private UpdateScheduler _updateScheduler;
        private Terrain3DIntegration _terrain3DIntegration;
        private UpdatePhase _currentPhase = UpdatePhase.Idle;
        private HashSet<Vector2I> _regionsProcessedThisUpdate = new();
        private List<Vector2I> _regionsToRemoveAfterPush = new();
        private float _worldHeight = 128;

        private bool _isInitialized = false;
        private bool _SyncedWithTerrain3D = false;
        private AsyncGpuTaskManager _taskManagerInstance;

        [Export]
        public bool AutoPushToTerrain { get; set; } = false;

        [Export]
        public bool DebugTerrainPush { get; set; } = false;
        [Export]
        public bool DebugTerrain3DIntegration { get; set; } = false;

        [Export]
        public bool EnableRegionPreviews
        {
            get => _enableRegionPreviews;
            set
            {
                _enableRegionPreviews = value;
                if (_regionMapManager != null)
                {
                    _regionMapManager.SetPreviewsEnabled(value);
                }
            }
        }
        private bool _enableRegionPreviews = true;

        #endregion

        #region Properties
        /// <summary>
        /// Gets the region size from the connected Terrain3D node.
        /// </summary>
        public int Terrain3DRegionSize => _terrain3DConnector?.RegionSize ?? 0;

        /// <summary>
        /// Gets the number of terrain layers currently managed.
        /// </summary>
        public int TerrainLayerCount => _layerCollectionManager?.Layers.Count ?? 0;

        /// <summary>
        /// Gets the mesh vertex spacing from the connected Terrain3D node.
        /// </summary>
        public float TerrainVertexSpacing => _terrain3DConnector?.MeshVertexSpacing ?? 0f;

        /// <summary>
        /// Gets or sets the Terrain3D node to connect to.
        /// </summary>
        [Export]
        public Node3D Terrain3DNode
        {
            get => _terrain3DConnector?.Terrain3DNode;
            set
            {
                if (_terrain3DConnector == null)
                {
                    _terrain3DConnector = new Terrain3DConnector(Owner);
                }
                _terrain3DConnector.Terrain3DNode = value;
                OnTerrain3DConnectionChanged();
            }
        }

        /// <summary>
        /// Gets or sets the world height scale for terrain processing.
        /// </summary>
        [Export]
        public float WorldHeightScale { get => _worldHeight; set => _worldHeight = value; }
        #endregion

        #region Debug System Fields
        const string DEBUG_CLASS = "TerrainLayerManager";
        private DebugManager _debugManager;

        [ExportGroup("🐛 Debug Settings")]
        [Export]
        public bool AlwaysReportErrors
        {
            get => _debugManager?.AlwaysReportErrors ?? true;
            set { if (_debugManager != null) _debugManager.AlwaysReportErrors = value; }
        }

        [Export]
        public bool AlwaysReportWarnings
        {
            get => _debugManager?.AlwaysReportWarnings ?? true;
            set { if (_debugManager != null) _debugManager.AlwaysReportWarnings = value; }
        }

        [Export]
        public bool EnableMessageAggregation
        {
            get => _debugManager?.EnableMessageAggregation ?? true;
            set { if (_debugManager != null) _debugManager.EnableMessageAggregation = value; }
        }

        [Export(PropertyHint.Range, "0.1,5.0,0.1")]
        public float AggregationWindowSeconds
        {
            get => _debugManager?.AggregationWindowSeconds ?? 1.0f;
            set { if (_debugManager != null) _debugManager.AggregationWindowSeconds = value; }
        }

        [Export(PropertyHint.ResourceType, "ClassDebugConfig")]
        public Godot.Collections.Array<ClassDebugConfig> ActiveDebugClasses
        {
            get => _activeDebugClasses;
            set
            {
                _activeDebugClasses = value;
                _debugManager?.UpdateFromConfigArray(_activeDebugClasses);
            }
        }
        private Godot.Collections.Array<ClassDebugConfig> _activeDebugClasses = new();

        #endregion

        #region Godot Lifecycle
        public override void _Ready()
        {

            InitializeDebugManager();

            InitializeManager();

            if (_terrain3DConnector.IsConnected)
            {
                InitializeSubManagers();
            }

            TerrainHeightQuery.SetTerrainLayerManager(this);
        }

        public override void _Process(double delta)
        {
            if (!_isInitialized || !Engine.IsEditorHint())
            {
                DebugManager.Instance?.Log("TerrainLayerManager", DebugCategory.UpdateCycle,
                            "_Process returned : Initialized " + _isInitialized + " | IsEditorHint : " + Engine.IsEditorHint());
                return;
            }

            _debugManager?.Process(delta);
            _updateScheduler.Process(delta);

            // State machine for update pipeline
            switch (_currentPhase)
            {
                case UpdatePhase.Idle:
                    // Check if we should start a new update
                    if (_updateScheduler.ShouldProcessUpdate())
                    {
                        DebugManager.Instance?.Log("TerrainLayerManager", DebugCategory.UpdateCycle,
                            "Update Phase - Idle, ProcessUpdate");
                        ProcessUpdate();
                        _updateScheduler.CompleteUpdateCycle();
                    }
                    break;

                case UpdatePhase.Processing:
                    // Check if GPU work is complete
                    DebugManager.Instance?.Log("TerrainLayerManager", DebugCategory.UpdateCycle,
                            "Update Phase - Processing, HasPendingWork");
                    if (!AsyncGpuTaskManager.Instance.HasPendingWork)
                    {
                        _currentPhase = UpdatePhase.ReadyToPush;
                        DebugManager.Instance?.Log("TerrainLayerManager", DebugCategory.UpdateCycle,
                            "GPU work complete - ready to push");
                    }
                    break;

                case UpdatePhase.ReadyToPush:
                    // Push to Terrain3D and clean up
                    DebugManager.Instance?.Log(DEBUG_CLASS, DebugCategory.TerrainPush, "Pushing to Terrain3D.");
                    if (AutoPushToTerrain)
                    {
                        PushUpdatesToTerrain();
                    }

                    // Clean up our region map
                    if (_regionsToRemoveAfterPush.Count > 0)
                    {
                        foreach (var region in _regionsToRemoveAfterPush)
                        {
                            _regionMapManager.RemoveRegion(region);
                        }
                        _regionsToRemoveAfterPush.Clear();
                    }

                    _regionsProcessedThisUpdate.Clear();
                    _currentPhase = UpdatePhase.Idle;

                    DebugManager.Instance?.Log("TerrainLayerManager", DebugCategory.UpdateCycle,
                        "Update cycle complete");

                    _debugManager?.FlushAggregatedMessages();
                    break;
            }
        }

        public override void _ExitTree()
        {
            Cleanup();

            if (DebugManager.Instance == _debugManager)
                DebugManager.Instance = null;
        }

        #endregion

        #region Initialization
        private void InitializeManager()
        {

            DebugManager.Instance?.RegisterClass(DEBUG_CLASS);
            DebugManager.Instance?.StartTimer(DEBUG_CLASS, DebugCategory.Initialization, "InitializeManager");

            Name = "TerrainLayerManager";
            AddToGroup("terrain_layer_manager");

            if (AsyncGpuTaskManager.Instance == null)
            {
                _taskManagerInstance = new AsyncGpuTaskManager();
                _taskManagerInstance.Name = "AsyncGpuTaskManager_Managed";
                AddChild(_taskManagerInstance);

                DebugManager.Instance?.Log(DEBUG_CLASS, DebugCategory.Initialization,
                    "Created AsyncGpuTaskManager instance");
            }

            _scenePreviewManager = new ScenePreviewManager(this);
            _scenePreviewManager.Initialize();
            _layerCollectionManager = new LayerCollectionManager(this);
            _updateScheduler = new UpdateScheduler();
            _terrain3DConnector = new Terrain3DConnector(Owner);

            AutoAssignTerrain3D();
            _isInitialized = true;

            DebugManager.Instance?.EndTimer(DEBUG_CLASS, DebugCategory.Initialization, "InitializeManager");
            DebugManager.Instance?.Log(DEBUG_CLASS, DebugCategory.Initialization,
                "Manager initialization complete");
        }

        private void InitializeSubManagers()
        {
            const string DEBUG_CLASS = "TerrainLayerManager";
            DebugManager.Instance?.StartTimer(DEBUG_CLASS, DebugCategory.Initialization, "InitializeSubManagers");

            if (Terrain3DRegionSize <= 0 || _scenePreviewManager == null)
            {
                DebugManager.Instance?.LogError(DEBUG_CLASS,
                    "Cannot initialize sub-managers: invalid region size or missing preview manager");
                return;
            }

            _regionMapManager = new RegionMapManager(
                Terrain3DRegionSize,
                _scenePreviewManager.PreviewParent,
                Owner);

            // Region previews do not need to be enabled if we are updating the terrain system in real-time
            _regionMapManager.SetPreviewsEnabled(_enableRegionPreviews); 

            _regionDependencyManager = new RegionDependencyManager(Terrain3DRegionSize, new Vector2I(-16, 15));

            _updateProcessor = new TerrainUpdateProcessor(
                _regionMapManager,
                _regionDependencyManager,
                Terrain3DRegionSize);

            _terrain3DIntegration = new Terrain3DIntegration(
                _terrain3DConnector.Terrain3D,
                _regionMapManager,
                Terrain3DRegionSize,
                _worldHeight);

            if (_terrain3DIntegration.ValidateTerrainSystem())
            {
                DebugManager.Instance?.Log(DEBUG_CLASS, DebugCategory.Initialization,
                    "All sub-managers initialized successfully");
                DebugManager.Instance?.Log(DEBUG_CLASS, DebugCategory.Validation,
                    _terrain3DConnector.GetConnectionStatus());
            }
            else
            {
                DebugManager.Instance?.LogError(DEBUG_CLASS,
                    "Terrain3D integration validation failed");
            }

            DebugManager.Instance?.EndTimer(DEBUG_CLASS, DebugCategory.Initialization, "InitializeSubManagers");
        }

        private void InitializeDebugManager()
        {
            _debugManager = new DebugManager();
            DebugManager.Instance = _debugManager;

            // Update from inspector array
            _debugManager.UpdateFromConfigArray(_activeDebugClasses);

            _debugManager.Log("TerrainLayerManager", DebugCategory.Initialization,
                "Debug Manager initialized");
        }
        private void AutoAssignTerrain3D()
        {
            if (_terrain3DConnector.IsConnected)
                return;

            if (_terrain3DConnector.AutoConnect())
            {
                OnTerrain3DConnectionChanged();
            }
        }

        /// <summary>
        /// Called when the Terrain3D connection changes (connected or disconnected).
        /// </summary>
        private void OnTerrain3DConnectionChanged()
        {
            if (_terrain3DConnector.IsConnected)
            {
                // Connection established or changed
                if (_isInitialized)
                {
                    InitializeSubManagers();
                }
            }
            else
            {
                // Connection lost or cleared
                CleanupSubManagers();
            }
        }

        private void CleanupSubManagers()
        {
            _regionMapManager = null;
            _regionDependencyManager = null;
            _updateProcessor = null;
            _terrain3DIntegration = null;
        }
        #endregion

        #region Main Update Loop
        private void ProcessUpdate()
        {
            const string DEBUG_CLASS = "TerrainLayerManager";

            // Can't start new update while processing
            if (_currentPhase != UpdatePhase.Idle)
            {
                DebugManager.Instance?.LogWarning(DEBUG_CLASS,
                    "Skipping update - previous update still in progress");
                return;
            }

            // === VALIDATION ===
            if (!_terrain3DConnector.ValidateConnection())
            {
                AutoAssignTerrain3D();
                if (!_terrain3DConnector.IsConnected) return;
            }

            // === PHASE 1: DETECT CHANGES ===
            _layerCollectionManager.Update();
            var allLayers = _layerCollectionManager.Layers;

            var maskDirtyLayers = allLayers
                .Where(l => GodotObject.IsInstanceValid(l) && l.IsDirty)
                .ToHashSet();

            var positionDirtyLayers = allLayers
                .Where(l => GodotObject.IsInstanceValid(l) && l.PositionDirty)
                .ToHashSet();

            DebugManager.Instance?.Log(DEBUG_CLASS, DebugCategory.UpdateCycle,
                $"Layers: Total={allLayers.Count}, MaskDirty={maskDirtyLayers.Count}, PositionDirty={positionDirtyLayers.Count}");
            DebugManager.Instance?.Log(DEBUG_CLASS, DebugCategory.LayerDirtying,
                $"PHASE 1: MaskDirty={maskDirtyLayers.Count}, PositionDirty={positionDirtyLayers.Count}");
            foreach (var layer in positionDirtyLayers)
            {
                DebugManager.Instance?.Log(DEBUG_CLASS, DebugCategory.LayerDetails,
                    $"Position dirty: '{layer.LayerName}' at {layer.GlobalPosition}");
            }

            // Update region dependencies (fills boundaryDirtyRegions)
            var boundaryDirtyRegions = new HashSet<Vector2I>();
            _regionDependencyManager.Update(allLayers, boundaryDirtyRegions);

            DebugManager.Instance?.Log(DEBUG_CLASS, DebugCategory.RegionDependencies,
                $"Boundary dirty regions: {boundaryDirtyRegions.Count}");
            foreach (var region in boundaryDirtyRegions)
            {
                DebugManager.Instance?.Log(DEBUG_CLASS, DebugCategory.RegionDetails,
                    $"  Boundary region: {region}");
            }
            // Determine what to remove
            var regionsToRemove = _regionDependencyManager.GetRegionsToRemove(
                _regionMapManager.GetManagedRegionCoords()
            );

            DebugManager.Instance?.Log(DEBUG_CLASS, DebugCategory.RegionLifecycle,
                $"Managed: {_regionMapManager.GetManagedRegionCoords().Count}, ToRemove: {regionsToRemove.Count}");

            bool hasChanges = maskDirtyLayers.Count > 0 ||
                             positionDirtyLayers.Count > 0 ||
                             boundaryDirtyRegions.Count > 0 ||
                             regionsToRemove.Count > 0;

            if (!hasChanges) return;

            _updateScheduler.SignalChanges();

            // === PHASE 2: PROPAGATE DEPENDENCIES ===
            LayerDependencyManager.PropagateDirtyStateFromMovement(
                positionDirtyLayers,
                allLayers,
                maskDirtyLayers);

            var propagatedDirtyLayers = LayerDependencyManager.PropagateDirtyState(allLayers);
            maskDirtyLayers.UnionWith(propagatedDirtyLayers);

            DebugManager.Instance?.Log(DEBUG_CLASS, DebugCategory.LayerDirtying,
                $"After propagation: {maskDirtyLayers.Count} mask dirty layers");

            // === PHASE 3: DETERMINE AFFECTED REGIONS ===
            var affectedRegions = new HashSet<Vector2I>(boundaryDirtyRegions);

            // Add regions overlapped by any dirty or position-changed layers
            foreach (var layer in maskDirtyLayers.Union(positionDirtyLayers))
            {
                var bounds = TerrainCoordinateHelper.GetRegionBoundsForLayer(layer, Terrain3DRegionSize);
                foreach (var coord in bounds.GetRegionCoords())
                {
                    affectedRegions.Add(coord);
                }
            }

            // Filter to processable regions
            var regionsToProcess = affectedRegions
                .Where(coord =>
                {
                    // Always process boundary regions (need clearing)
                    if (boundaryDirtyRegions.Contains(coord))
                        return true;

                    var tiered = _regionDependencyManager.GetTieredLayersForRegion(coord);
                    return tiered?.ShouldProcess() ?? false;
                })
                .ToHashSet();

            DebugManager.Instance?.Log(DEBUG_CLASS, DebugCategory.RegionDependencies,
                $"Affected: {affectedRegions.Count} → Processing: {regionsToProcess.Count}");

            DebugManager.Instance?.Log(DEBUG_CLASS, DebugCategory.UpdateCycle,
                $"Processing {regionsToProcess.Count} regions, " +
                $"{maskDirtyLayers.Count} dirty layers, " +
                $"{regionsToRemove.Count} to remove");

            // === PHASE 4: PREPARE RESOURCES ===
            bool isInteractiveResize = _updateScheduler.IsCurrentUpdateInteractive() &&
                                      maskDirtyLayers.Any(l => l.SizeHasChanged);

            foreach (var layer in maskDirtyLayers)
            {
                layer.PrepareMaskResources(isInteractiveResize);
            }

            // === PHASE 5: SUBMIT GPU WORK ===
            var dirtyHeightLayers = maskDirtyLayers
                .Where(l => l.GetLayerType() == LayerType.Height);
            var dirtyTextureLayers = maskDirtyLayers
                .Where(l => l.GetLayerType() == LayerType.Texture);
            var dirtyFeatureLayers = maskDirtyLayers
                .Where(l => l.GetLayerType() == LayerType.Feature);

            DebugManager.Instance?.Log(DEBUG_CLASS, DebugCategory.PhaseExecution,
                $"GPU work: Height={dirtyHeightLayers.Count()}, Texture={dirtyTextureLayers.Count()}, Feature={dirtyFeatureLayers.Count()}");

            _updateProcessor.ProcessUpdatesAsync(
                regionsToProcess,
                dirtyHeightLayers,
                dirtyTextureLayers,
                dirtyFeatureLayers,
                _regionDependencyManager.GetActiveRegionCoords(),
                isInteractiveResize,
                _worldHeight
            );

            // === PHASE 6: UPDATE STATE ===
            _regionsProcessedThisUpdate = regionsToProcess;
            _regionsToRemoveAfterPush = regionsToRemove;
            _currentPhase = UpdatePhase.Processing;

            // Clear dirty flags
            foreach (var layer in positionDirtyLayers) layer.ClearPositionDirty();
            foreach (var layer in maskDirtyLayers) layer.ClearDirty();

            DebugManager.Instance?.Log(DEBUG_CLASS, DebugCategory.UpdateCycle,
                "Phase = Processing | GPU work Submitted");
        }
        #endregion

        #region Cleanup
        private void Cleanup()
        {
            DebugManager.Instance?.Log(DEBUG_CLASS, DebugCategory.Cleanup, "Cleaning up.");
            if (TerrainHeightQuery.GetTerrainLayerManager() == this)
            {
                TerrainHeightQuery.SetTerrainLayerManager(null);
            }

            // Cancel any pending async terrain pushes
            if (_terrain3DIntegration != null && _terrain3DIntegration.HasPendingPushes)
            {
                if (DebugTerrainPush)
                {
                    DebugManager.Instance?.Log(DEBUG_CLASS, DebugCategory.Cleanup, "Cancelling pending pushes during cleanup");
                }
                _terrain3DIntegration.CancelPendingPushes();
            }

            _regionMapManager?.FreeAll();
            _scenePreviewManager?.Cleanup();
            _updateScheduler?.Reset();
            _terrain3DConnector?.Disconnect();
        }
        #endregion

        #region Terrain Push API
        /// <summary>
        /// Pushes all regions that were updated since the last push to the Terrain3D system.
        /// This is called automatically if AutoPushToTerrain is enabled, or can be called manually.
        /// </summary>
        public void PushUpdatesToTerrain()
        {
            if (_terrain3DIntegration == null) return;

            // Get regions that were updated (marked by region compositing phases)
            var updatedRegions = _regionDependencyManager.GetAndClearUpdatedRegions();
            var allRegions = _regionDependencyManager.GetActiveRegionCoords().ToList();

            if (updatedRegions.Count == 0)
            {
                DebugManager.Instance?.Log("TerrainLayerManager", DebugCategory.TerrainPush,
                    "Nothing to push");
                return;
            }

            DebugManager.Instance?.Log("TerrainLayerManager", DebugCategory.TerrainPush,
                $"Pushing {updatedRegions.Count} updated region(s), ");

            _terrain3DIntegration.PushRegionsToTerrain(
                updatedRegions,
                allRegions
            );
        }

        #endregion

        #region Debug Helper Methods (for Inspector/Editor)

        /// <summary>
        /// Creates a new ClassDebugConfig with default settings.
        /// Call this from editor tools or inspector buttons.
        /// </summary>
        public ClassDebugConfig CreateDebugConfigForClass(string className)
        {
            var config = new ClassDebugConfig(className)
            {
                Enabled = true,
                EnabledCategories = DebugCategory.None // User can enable categories as needed
            };

            return config;
        }

        /// <summary>
        /// Adds a class to the active debug classes array if it doesn't already exist.
        /// Returns true if added, false if already exists.
        /// </summary>
        public bool AddDebugClass(string className)
        {
            // Check if already exists
            if (_activeDebugClasses.Any(c => c?.ClassName == className))
            {
                GD.PrintErr($"[TerrainLayerManager] Class '{className}' is already in the debug list");
                return false;
            }

            var config = CreateDebugConfigForClass(className);
            _activeDebugClasses.Add(config);
            _debugManager?.UpdateFromConfigArray(_activeDebugClasses);

            GD.Print($"[TerrainLayerManager] Added '{className}' to debug classes");
            return true;
        }

        /// <summary>
        /// Removes a class from the active debug classes array.
        /// </summary>
        public bool RemoveDebugClass(string className)
        {
            var config = _activeDebugClasses.FirstOrDefault(c => c?.ClassName == className);
            if (config != null)
            {
                _activeDebugClasses.Remove(config);
                _debugManager?.UpdateFromConfigArray(_activeDebugClasses);

                GD.Print($"[TerrainLayerManager] Removed '{className}' from debug classes");
                return true;
            }

            return false;
        }

        #endregion
    }

}