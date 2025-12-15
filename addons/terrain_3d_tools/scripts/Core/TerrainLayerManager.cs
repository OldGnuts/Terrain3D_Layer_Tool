// /Core/TerrainLayerManager.cs
using Godot;
using System.Collections.Generic;
using Terrain3DTools.Core.Debug;
using Terrain3DTools.Layers;
using Terrain3DTools.Utils;
using System.Linq;

namespace Terrain3DTools.Core
{
    /// <summary>
    /// Central orchestrator for the terrain layer system. Manages the update pipeline
    /// that processes layer changes, generates GPU work, and synchronizes results
    /// with Terrain3D.
    /// </summary>
    [GlobalClass, Tool]
    public partial class TerrainLayerManager : Node3D
    {
        /// <summary>
        /// State machine phases for the update pipeline.
        /// Idle -> Processing -> ReadyToPush -> Idle
        /// </summary>
        private enum UpdatePhase
        {
            Idle,
            Processing,
            ReadyToPush
        }

        #region Fields
        [Export(PropertyHint.None, "Read-only in the editor")]
        private int _terrainLayerCountForInspector;

        private Terrain3DConnector _terrain3DConnector;
        private ScenePreviewManager _scenePreviewManager;
        private LayerCollectionManager _layerCollectionManager;
        private RegionDependencyManager _regionDependencyManager;
        private RegionMapManager _regionMapManager;
        private TerrainUpdateProcessor _updateProcessor;
        private UpdateScheduler _updateScheduler;
        private Terrain3DIntegration _terrain3DIntegration;
        private UpdatePhase _currentPhase = UpdatePhase.Idle;
        private HashSet<Vector2I> _regionsProcessedThisUpdate = new();
        private List<Vector2I> _regionsToRemoveAfterPush = new();
        private float _worldHeight = 128;
        private bool _isInitialized = false;
        private AsyncGpuTaskManager _taskManagerInstance;
        private TerrainLayerBase _selectedLayer;

        [Export]
        public bool AutoPushToTerrain { get; set; } = false;

        [Export]
        public bool EnableRegionPreviews
        {
            get => _enableRegionPreviews;
            set
            {
                _enableRegionPreviews = value;
                _regionMapManager?.SetPreviewsEnabled(value);
            }
        }
        private bool _enableRegionPreviews = true;

        #endregion

        #region Properties
        public int Terrain3DRegionSize => _terrain3DConnector?.RegionSize ?? 0;

        public int TerrainLayerCount => _layerCollectionManager?.Layers.Count ?? 0;

        public double TerrainVertexSpacing => _terrain3DConnector?.MeshVertexSpacing ?? 0f;

        public RegionMapManager GetRegionMapManager() => _regionMapManager;

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
                if (IsInstanceValid(value))
                {
                    _terrain3DConnector.Terrain3DNode = value;
                    OnTerrain3DConnectionChanged();
                }
            }
        }

        [Export]
        public float WorldHeightScale
        {
            get => _worldHeight;
            set
            {
                if (_worldHeight != value)
                {
                    _worldHeight = value;
                    PropagateWorldHeightScaleToFeatureLayers();
                }
            }
        }

        public void SetSelectedLayer(TerrainLayerBase layer)
        {
            _selectedLayer = layer;
            _updateScheduler?.SignalChanges();
            DebugManager.Instance?.Log(DEBUG_CLASS, DebugCategory.Initialization, $"Selected layer: {_selectedLayer}");
        }

        public TerrainLayerBase GetSelectedLayer() => _selectedLayer;

        #endregion

        #region Update Timing Settings
        [ExportGroup("⏱️ Update Timing")]

        [Export(PropertyHint.Range, "0.016,1.0,0.016")]
        public float UpdateInterval
        {
            get => (float)(_updateScheduler?.UpdateInterval ?? 0.1);
            set
            {
                if (_updateScheduler != null)
                    _updateScheduler.UpdateInterval = value;
                _cachedUpdateInterval = value;
            }
        }
        private float _cachedUpdateInterval = 0.1f;

        [Export(PropertyHint.Range, "0.1,2.0,0.1")]
        public float InteractionThreshold
        {
            get => (float)(_updateScheduler?.InteractionThreshold ?? 0.5);
            set
            {
                if (_updateScheduler != null)
                    _updateScheduler.InteractionThreshold = value;
                _cachedInteractionThreshold = value;
            }
        }
        private float _cachedInteractionThreshold = 0.5f;
        #endregion

        #region Debug System
        private const string DEBUG_CLASS = "TerrainLayerManager";
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

        /// <summary>
        /// Main update loop implementing a state machine for the terrain processing pipeline.
        /// </summary>
        public override void _Process(double delta)
        {
            if (!_isInitialized || !Engine.IsEditorHint())
            {
                return;
            }

            _debugManager?.Process(delta);
            _updateScheduler.Process(delta);

            switch (_currentPhase)
            {
                case UpdatePhase.Idle:
                    if (_updateScheduler.ShouldProcessUpdate())
                    {
                        DebugManager.Instance?.Log(DEBUG_CLASS, DebugCategory.UpdateCycle,
                            "Starting ProcessUpdate");
                        ProcessUpdate();
                        _updateScheduler.CompleteUpdateCycle();
                    }
                    break;

                case UpdatePhase.Processing:
                    if (!AsyncGpuTaskManager.Instance.HasPendingWork)
                    {
                        _currentPhase = UpdatePhase.ReadyToPush;
                        DebugManager.Instance?.Log(DEBUG_CLASS, DebugCategory.UpdateCycle,
                            "GPU work complete - ready to push");
                    }
                    break;

                case UpdatePhase.ReadyToPush:
                    if (AutoPushToTerrain)
                    {
                        PushUpdatesToTerrain();
                    }

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

                    DebugManager.Instance?.Log(DEBUG_CLASS, DebugCategory.UpdateCycle,
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

            // Apply cached timing settings
            _updateScheduler.UpdateInterval = _cachedUpdateInterval;
            _updateScheduler.InteractionThreshold = _cachedInteractionThreshold;

            _isInitialized = true;

            DebugManager.Instance?.EndTimer(DEBUG_CLASS, DebugCategory.Initialization, "InitializeManager");
            DebugManager.Instance?.Log(DEBUG_CLASS, DebugCategory.Initialization,
                "Manager initialization complete");
        }

        private void InitializeSubManagers()
        {
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

                // NEW: Propagate world height scale to any existing feature layers
                PropagateWorldHeightScaleToFeatureLayers();
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
            _debugManager.UpdateFromConfigArray(_activeDebugClasses);
            _debugManager.Log(DEBUG_CLASS, DebugCategory.Initialization, "Debug Manager initialized");
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

        private void OnTerrain3DConnectionChanged()
        {
            if (_terrain3DConnector.IsConnected)
            {
                if (_isInitialized)
                {
                    InitializeSubManagers();
                }
            }
            else
            {
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
        /// <summary>
        /// Processes a single update cycle through 6 phases:
        /// 1. Detect changes (dirty layers, position changes)
        /// 2. Propagate dependencies between layers
        /// 3. Determine affected regions
        /// 4. Prepare GPU resources
        /// 5. Submit GPU work
        /// 6. Update state and clear dirty flags
        /// </summary>
        private void ProcessUpdate()
        {
            if (_currentPhase != UpdatePhase.Idle)
            {
                DebugManager.Instance?.LogWarning(DEBUG_CLASS,
                    "Skipping update - previous update still in progress");
                return;
            }

            // === VALIDATION ===
            if (!_terrain3DConnector.ValidateConnection())
            {
                DebugManager.Instance?.Log(DEBUG_CLASS, DebugCategory.Validation,
                    "Terrain3D connection not validated");
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

            foreach (var layer in positionDirtyLayers)
            {
                DebugManager.Instance?.Log(DEBUG_CLASS, DebugCategory.LayerDetails,
                    $"Position dirty: '{layer.LayerName}' at {layer.GlobalPosition}");
            }

            var boundaryDirtyRegions = new HashSet<Vector2I>();
            _regionDependencyManager.Update(allLayers, boundaryDirtyRegions);

            DebugManager.Instance?.Log(DEBUG_CLASS, DebugCategory.RegionDependencies,
                $"Boundary dirty regions: {boundaryDirtyRegions.Count}");

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

            foreach (var layer in maskDirtyLayers.Union(positionDirtyLayers))
            {
                var bounds = TerrainCoordinateHelper.GetRegionBoundsForLayer(layer, Terrain3DRegionSize);
                foreach (var coord in bounds.GetRegionCoords())
                {
                    affectedRegions.Add(coord);
                }
            }

            var regionsToProcess = affectedRegions
                .Where(coord =>
                {
                    if (boundaryDirtyRegions.Contains(coord))
                        return true;

                    var tiered = _regionDependencyManager.GetTieredLayersForRegion(coord);
                    return tiered?.ShouldProcess() ?? false;
                })
                .ToHashSet();

            DebugManager.Instance?.Log(DEBUG_CLASS, DebugCategory.RegionDependencies,
                $"Affected: {affectedRegions.Count} → Processing: {regionsToProcess.Count}");

            // === PHASE 4: PREPARE RESOURCES ===
            bool isInteractiveResize = _updateScheduler.IsCurrentUpdateInteractive() &&
                                      maskDirtyLayers.Any(l => l.SizeHasChanged);

            foreach (var layer in maskDirtyLayers)
            {
                layer.PrepareMaskResources(isInteractiveResize);
                if (layer is FeatureLayer featureLayer)
                {
                    featureLayer.SetWorldHeightScale(_worldHeight);
                }
            }

            // === PHASE 5: SUBMIT GPU WORK ===
            var dirtyHeightLayers = maskDirtyLayers.Where(l => l.GetLayerType() == LayerType.Height);
            var dirtyTextureLayers = maskDirtyLayers.Where(l => l.GetLayerType() == LayerType.Texture);
            var dirtyFeatureLayers = maskDirtyLayers.Where(l => l.GetLayerType() == LayerType.Feature);

            DebugManager.Instance?.Log(DEBUG_CLASS, DebugCategory.PhaseExecution,
                $"GPU work: Height={dirtyHeightLayers.Count()}, Texture={dirtyTextureLayers.Count()}, Feature={dirtyFeatureLayers.Count()}");

            _updateProcessor.ProcessUpdatesAsync(
                regionsToProcess,
                dirtyHeightLayers,
                dirtyTextureLayers,
                dirtyFeatureLayers,
                _regionDependencyManager.GetActiveRegionCoords(),
                isInteractiveResize,
                _worldHeight,
                _selectedLayer
            );

            // === PHASE 6: UPDATE STATE ===
            _regionsProcessedThisUpdate = regionsToProcess;
            _regionsToRemoveAfterPush = regionsToRemove;
            _currentPhase = UpdatePhase.Processing;

            foreach (var layer in positionDirtyLayers) layer.ClearPositionDirty();
            foreach (var layer in maskDirtyLayers) layer.ClearDirty();

            DebugManager.Instance?.Log(DEBUG_CLASS, DebugCategory.UpdateCycle,
                "GPU work submitted");
        }
        #endregion

        #region Cleanup
        private void Cleanup()
        {
            DebugManager.Instance?.Log(DEBUG_CLASS, DebugCategory.Cleanup, "Cleaning up");

            if (TerrainHeightQuery.GetTerrainLayerManager() == this)
            {
                TerrainHeightQuery.SetTerrainLayerManager(null);
            }

            _terrain3DIntegration?.CancelPendingPushes();
            _regionMapManager?.FreeAll();
            _scenePreviewManager?.Cleanup();
            _updateScheduler?.Reset();
            _terrain3DConnector?.Disconnect();
        }
        #endregion

        #region Terrain Push API
        /// <summary>
        /// Pushes updated regions to Terrain3D. Called automatically if AutoPushToTerrain
        /// is enabled, or can be called manually for deferred updates.
        /// </summary>
        public void PushUpdatesToTerrain()
        {
            if (_terrain3DIntegration == null) return;

            var updatedRegions = _regionDependencyManager.GetAndClearUpdatedRegions();
            var allRegions = _regionDependencyManager.GetActiveRegionCoords().ToList();

            if (updatedRegions.Count == 0)
            {
                DebugManager.Instance?.Log(DEBUG_CLASS, DebugCategory.TerrainPush, "Nothing to push");
                return;
            }

            DebugManager.Instance?.Log(DEBUG_CLASS, DebugCategory.TerrainPush,
                $"Pushing {updatedRegions.Count} updated region(s)");

            _terrain3DIntegration.PushRegionsToTerrain(updatedRegions, allRegions);
        }
        #endregion

        #region Feature Layer Support
        /// <summary>
        /// Propagates the current world height scale to all feature layers.
        /// Called when WorldHeightScale changes or when new feature layers are discovered.
        /// </summary>
        private void PropagateWorldHeightScaleToFeatureLayers()
        {
            if (_layerCollectionManager == null) return;

            foreach (var layer in _layerCollectionManager.Layers)
            {
                if (layer is FeatureLayer featureLayer)
                {
                    featureLayer.SetWorldHeightScale(_worldHeight);

                    DebugManager.Instance?.Log(DEBUG_CLASS, DebugCategory.Initialization,
                        $"Set world height scale {_worldHeight} on feature layer '{featureLayer.LayerName}'");
                }
            }
        }
        #endregion

        #region Debug Configuration API
        /// <summary>
        /// Programmatically adds a class to the debug configuration.
        /// Useful for editor tools that need to enable debugging at runtime.
        /// </summary>
        public bool AddDebugClass(string className)
        {
            if (_activeDebugClasses.Any(c => c?.ClassName == className))
            {
                GD.PrintErr($"[TerrainLayerManager] Class '{className}' is already in the debug list");
                return false;
            }

            var config = new ClassDebugConfig(className)
            {
                Enabled = true,
                EnabledCategories = DebugCategory.None
            };

            _activeDebugClasses.Add(config);
            _debugManager?.UpdateFromConfigArray(_activeDebugClasses);

            GD.Print($"[TerrainLayerManager] Added '{className}' to debug classes");
            return true;
        }

        /// <summary>
        /// Programmatically removes a class from the debug configuration.
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