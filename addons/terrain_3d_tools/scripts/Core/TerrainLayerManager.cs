// /Core/TerrainLayerManager.cs
using Godot;
using System.Collections.Generic;
using Terrain3DTools.Core.Debug;
using Terrain3DTools.Layers;
using Terrain3DTools.Layers.Instancer;
using Terrain3DTools.Brushes;
using Terrain3DTools.Utils;
using Terrain3DTools.Settings;
using System.Linq;

namespace Terrain3DTools.Core
{
    /// <summary>
    /// Central orchestrator for the terrain layer system. Manages the update pipeline
    /// that processes layer changes, generates GPU work, and synchronizes results
    /// with Terrain3D.
    /// 
    /// Settings are managed via GlobalToolSettingsManager. Export properties here
    /// act as proxies that sync bidirectionally with the global settings.
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
            ReadyToPush,
            WaitingForTerrainPush,
            ReadyToPushInstances,
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
        private bool _isInitialized = false;
        private AsyncGpuTaskManager _taskManagerInstance;
        private TerrainLayerBase _selectedLayer;
        private bool _settingsSubscribed = false;
        private bool _hasInstancersThisUpdate = false;
        private HashSet<Vector2I> _regionsWithPendingInstances = new();
        private Dictionary<ulong, Dictionary<Vector2I, InstanceBuffer>> _pendingInstanceBuffers = new();
        private Dictionary<ulong, HashSet<Vector2I>> _previousInstanceRegions = new();
        private Dictionary<ulong, HashSet<int>> _previousMeshIdsByLayer = new();
        private bool _fullyInitialized = false;
        private TerrainBrushManager _brushManager;

        // Debug
        private const string DEBUG_CLASS = "TerrainLayerManager";
        private DebugManager _debugManager;
        #endregion

        #region Cached Collections for Hot Path
        private readonly HashSet<TerrainLayerBase> _maskDirtyLayersCache = new();
        private readonly HashSet<TerrainLayerBase> _positionDirtyLayersCache = new();
        private readonly HashSet<Vector2I> _affectedRegionsCache = new();
        private readonly HashSet<Vector2I> _featureAffectedRegionsCache = new();
        private readonly HashSet<Vector2I> _instancerAffectedRegionsCache = new();
        private readonly HashSet<Vector2I> _regionsToProcessCache = new();
        private readonly List<TerrainLayerBase> _dirtyHeightLayersCache = new();
        private readonly List<TerrainLayerBase> _dirtyTextureLayersCache = new();
        private readonly List<TerrainLayerBase> _dirtyFeatureLayersCache = new();
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

        public void SetSelectedLayer(TerrainLayerBase layer)
        {
            _selectedLayer = layer;
            _updateScheduler?.SignalChanges();
            DebugManager.Instance?.Log(DEBUG_CLASS, DebugCategory.Initialization, $"Selected layer: {_selectedLayer}");
        }

        public TerrainLayerBase GetSelectedLayer() => _selectedLayer;
        #endregion

        #region Godot Lifecycle
        public override void _Ready()
        {
            InitializeDebugManager();
            InitializeManager();

            // Subscribe to settings changes
            SubscribeToSettings();

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

            var settings = GlobalToolSettingsManager.Current;

            switch (_currentPhase)
            {
                case UpdatePhase.Idle:
                    ProcessIdlePhase(settings);
                    break;

                case UpdatePhase.Processing:
                    ProcessProcessingPhase();
                    break;

                case UpdatePhase.ReadyToPush:
                    ProcessReadyToPushPhase(settings);
                    break;

                case UpdatePhase.WaitingForTerrainPush:
                    ProcessWaitingForTerrainPushPhase();
                    break;

                case UpdatePhase.ReadyToPushInstances:
                    ProcessReadyToPushInstancesPhase();
                    break;
            }
        }

        public override void _ExitTree()
        {
            Cleanup();
            UnsubscribeFromSettings();

            if (DebugManager.Instance == _debugManager)
                DebugManager.Instance = null;
        }

        #endregion

        #region Settings Subscription
        private void SubscribeToSettings()
        {
            if (_settingsSubscribed) return;
            GlobalToolSettingsManager.SettingsChanged += OnGlobalSettingsChanged;
            _settingsSubscribed = true;
        }

        private void UnsubscribeFromSettings()
        {
            if (!_settingsSubscribed) return;
            GlobalToolSettingsManager.SettingsChanged -= OnGlobalSettingsChanged;
            _settingsSubscribed = false;
        }

        /// <summary>
        /// Responds to settings changes from GlobalToolSettingsManager.
        /// Syncs relevant settings to internal components that need them cached.
        /// </summary>
        private void OnGlobalSettingsChanged()
        {
            var settings = GlobalToolSettingsManager.Current;
            if (settings == null) return;

            // Update scheduler timing
            if (_updateScheduler != null)
            {
                _updateScheduler.UpdateInterval = settings.UpdateInterval;
                _updateScheduler.InteractionThreshold = settings.InteractionThreshold;
            }

            // Update GPU task manager
            if (_taskManagerInstance != null)
            {
                _taskManagerInstance.CleanupFrameThreshold = settings.GpuCleanupFrameThreshold;
            }

            // Update region previews
            _regionMapManager?.SetPreviewsEnabled(settings.EnableRegionPreviews);

            // Update brush manager height scale
            _brushManager?.SetHeightScale(settings.WorldHeightScale);

            // Propagate world height scale to feature layers
            PropagateWorldHeightScaleToFeatureLayers();

            DebugManager.Instance?.Log(DEBUG_CLASS, DebugCategory.Initialization,
                "Settings synchronized from GlobalToolSettings");
        }
        #endregion

        #region Initialization
        private void InitializeManager()
        {
            DebugManager.Instance?.RegisterClass("HeightLayer");
            DebugManager.Instance?.RegisterClass("TextureLayer");
            DebugManager.Instance?.RegisterClass("PathLayer");

            DebugManager.Instance?.RegisterClass(DEBUG_CLASS);
            DebugManager.Instance?.StartTimer(DEBUG_CLASS, DebugCategory.Initialization, "InitializeManager");

            Name = "TerrainLayerManager";
            AddToGroup("terrain_layer_manager");

            // Get settings for initialization
            var settings = GlobalToolSettingsManager.Current;

            if (AsyncGpuTaskManager.Instance == null)
            {
                _taskManagerInstance = new AsyncGpuTaskManager();
                _taskManagerInstance.Name = "AsyncGpuTaskManager_Managed";

                // Configure from global settings
                _taskManagerInstance.CleanupFrameThreshold = settings?.GpuCleanupFrameThreshold ?? 3;

                AddChild(_taskManagerInstance);

                DebugManager.Instance?.Log(DEBUG_CLASS, DebugCategory.Initialization,
                    $"Created AsyncGpuTaskManager instance (Cleanup Threshold: {_taskManagerInstance.CleanupFrameThreshold})");
            }

            _scenePreviewManager = new ScenePreviewManager(this);
            _scenePreviewManager.Initialize();
            _layerCollectionManager = new LayerCollectionManager(this);
            _updateScheduler = new UpdateScheduler();
            _terrain3DConnector = new Terrain3DConnector(Owner);

            // Apply settings to scheduler
            if (settings != null)
            {
                _updateScheduler.UpdateInterval = settings.UpdateInterval;
                _updateScheduler.InteractionThreshold = settings.InteractionThreshold;
            }

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

            var settings = GlobalToolSettingsManager.Current;

            _regionMapManager = new RegionMapManager(
                Terrain3DRegionSize,
                _scenePreviewManager.PreviewParent,
                Owner);

            _regionMapManager.SetPreviewsEnabled(settings?.EnableRegionPreviews ?? true);

            _regionDependencyManager = new RegionDependencyManager(Terrain3DRegionSize, new Vector2I(-16, 15));

            _updateProcessor = new TerrainUpdateProcessor(
                _regionMapManager,
                _regionDependencyManager,
                Terrain3DRegionSize,
                this);

            _terrain3DIntegration = new Terrain3DIntegration(
                _terrain3DConnector.Terrain3D,
                _regionMapManager,
                Terrain3DRegionSize,
                settings?.WorldHeightScale ?? 128f);

            if (_brushManager != null)
            {
                _brushManager.InitializeFastPath(
                    _regionMapManager,
                    _terrain3DIntegration,
                    Terrain3DRegionSize,
                    settings?.WorldHeightScale ?? 128f);
            }

            if (_terrain3DIntegration.ValidateTerrainSystem())
            {
                DebugManager.Instance?.Log(DEBUG_CLASS, DebugCategory.Initialization,
                    "All sub-managers initialized successfully");
                DebugManager.Instance?.Log(DEBUG_CLASS, DebugCategory.Validation,
                    _terrain3DConnector.GetConnectionStatus());

                PropagateWorldHeightScaleToFeatureLayers();

                // Mark as fully initialized after a short delay to let Terrain3D settle
                CallDeferred(nameof(SetFullyInitialized));
            }
            else
            {
                DebugManager.Instance?.LogError(DEBUG_CLASS,
                    "Terrain3D integration validation failed");
            }

            DebugManager.Instance?.EndTimer(DEBUG_CLASS, DebugCategory.Initialization, "InitializeSubManagers");
        }

        /// <summary>
        /// Sets the brush manager reference for fast path initialization.
        /// </summary>
        public void SetBrushManager(TerrainBrushManager brushManager)
        {
            _brushManager = brushManager;

            // Initialize fast path if sub-managers are already ready
            if (_terrain3DIntegration != null && _regionMapManager != null)
            {
                var settings = GlobalToolSettingsManager.Current;
                _brushManager.InitializeFastPath(
                    _regionMapManager,
                    _terrain3DIntegration,
                    Terrain3DRegionSize,
                    settings?.WorldHeightScale ?? 128f);
            }
        }

        private void SetFullyInitialized()
        {
            _fullyInitialized = true;
            DebugManager.Instance?.Log(DEBUG_CLASS, DebugCategory.Initialization,
                "TerrainLayerManager fully initialized");
        }

        private void InitializeDebugManager()
        {
            _debugManager = new DebugManager();
            DebugManager.Instance = _debugManager;

            // Apply settings from GlobalToolSettings
            var settings = GlobalToolSettingsManager.Current;
            if (settings != null)
            {
                _debugManager.AlwaysReportErrors = settings.AlwaysReportErrors;
                _debugManager.AlwaysReportWarnings = settings.AlwaysReportWarnings;
                _debugManager.EnableMessageAggregation = settings.EnableMessageAggregation;
                _debugManager.AggregationWindowSeconds = settings.AggregationWindowSeconds;
                _debugManager.UpdateFromConfigArray(settings.ActiveDebugClasses);
            }

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
        /// Processes a single update cycle through 6 phases.
        /// Reads settings directly from GlobalToolSettingsManager.
        /// </summary>
        private void ProcessUpdate()
        {
            if (_currentPhase != UpdatePhase.Idle)
            {
                DebugManager.Instance?.LogWarning(DEBUG_CLASS,
                    "Skipping update - previous update still in progress");
                return;
            }

            if (!_terrain3DConnector.ValidateConnection())
            {
                DebugManager.Instance?.Log(DEBUG_CLASS, DebugCategory.Validation,
                    "Terrain3D connection not validated");
                AutoAssignTerrain3D();
                if (!_terrain3DConnector.IsConnected) return;
            }

            if (_updateScheduler.IsFullUpdateQueued)
            {
                _updateScheduler.ProcessReDirtyLayers();
            }

            _layerCollectionManager.Update();
            var allLayers = _layerCollectionManager.Layers;

            // Clear cached collections
            _maskDirtyLayersCache.Clear();
            _positionDirtyLayersCache.Clear();

            // Collect dirty layers without LINQ allocation
            foreach (var layer in allLayers)
            {
                if (!GodotObject.IsInstanceValid(layer)) continue;

                if (layer.IsDirty)
                    _maskDirtyLayersCache.Add(layer);

                if (layer.PositionDirty)
                    _positionDirtyLayersCache.Add(layer);
            }

            if (_positionDirtyLayersCache.Count > 0)
            {
                DebugManager.Instance?.Log(DEBUG_CLASS, DebugCategory.UpdateCycle,
                    $"Position-dirty layers: {string.Join(", ", _positionDirtyLayersCache.Select(l => l.LayerName))}");
            }

            DebugManager.Instance?.Log(DEBUG_CLASS, DebugCategory.UpdateCycle,
                $"Layers: Total={allLayers.Count}, MaskDirty={_maskDirtyLayersCache.Count}, PositionDirty={_positionDirtyLayersCache.Count}");

            foreach (var layer in _positionDirtyLayersCache)
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
                $"Managed: {_regionMapManager.GetManagedRegionCoords().Count}, BoundaryDirty: {boundaryDirtyRegions.Count}, ToRemove: {regionsToRemove.Count}");

            bool hasChanges = _maskDirtyLayersCache.Count > 0 ||
                             _positionDirtyLayersCache.Count > 0 ||
                             boundaryDirtyRegions.Count > 0 ||
                             regionsToRemove.Count > 0;

            if (!hasChanges) return;

            _updateScheduler.SignalChanges();

            LayerDependencyManager.PropagateDirtyStateFromMovement(
                _positionDirtyLayersCache,
                allLayers,
                _maskDirtyLayersCache);

            var propagatedDirtyLayers = LayerDependencyManager.PropagateDirtyState(allLayers);
            _maskDirtyLayersCache.UnionWith(propagatedDirtyLayers);

            LayerDependencyManager.PropagateInstancerDirtyState(
                allLayers,
                _maskDirtyLayersCache,
                _previousInstanceRegions,
                Terrain3DRegionSize);

            DebugManager.Instance?.Log(DEBUG_CLASS, DebugCategory.LayerDirtying,
                $"After propagation: {_maskDirtyLayersCache.Count} mask dirty layers");

            // Clear and reuse affected regions collections
            _affectedRegionsCache.Clear();
            _affectedRegionsCache.UnionWith(boundaryDirtyRegions);
            _featureAffectedRegionsCache.Clear();
            _instancerAffectedRegionsCache.Clear();

            foreach (var layer in _maskDirtyLayersCache)
            {
                var bounds = TerrainCoordinateHelper.GetRegionBoundsForLayer(layer, Terrain3DRegionSize);
                foreach (var coord in bounds.GetRegionCoords())
                {
                    _affectedRegionsCache.Add(coord);
                    if (layer.GetLayerType() == LayerType.Feature)
                    {
                        _featureAffectedRegionsCache.Add(coord);
                    }
                    if (layer is InstancerLayer)
                    {
                        _instancerAffectedRegionsCache.Add(coord);
                    }
                }
            }

            foreach (var layer in _positionDirtyLayersCache)
            {
                var bounds = TerrainCoordinateHelper.GetRegionBoundsForLayer(layer, Terrain3DRegionSize);
                foreach (var coord in bounds.GetRegionCoords())
                {
                    _affectedRegionsCache.Add(coord);
                    if (layer.GetLayerType() == LayerType.Feature)
                    {
                        _featureAffectedRegionsCache.Add(coord);
                    }
                    if (layer is InstancerLayer)
                    {
                        _instancerAffectedRegionsCache.Add(coord);
                    }
                }
            }

            DebugManager.Instance?.Log(DEBUG_CLASS, DebugCategory.RegionDependencies,
                $"Affected: {_affectedRegionsCache.Count}, FeatureAffected: {_featureAffectedRegionsCache.Count}");

            // Filter to processable regions without LINQ allocation
            _regionsToProcessCache.Clear();
            foreach (var coord in _affectedRegionsCache)
            {
                var tiered = _regionDependencyManager.GetTieredLayersForRegion(coord);
                if (tiered?.ShouldProcess() ?? false)
                {
                    _regionsToProcessCache.Add(coord);
                }
            }

            DebugManager.Instance?.Log(DEBUG_CLASS, DebugCategory.RegionDependencies,
                $"Affected: {_affectedRegionsCache.Count} → Processing: {_regionsToProcessCache.Count}");

            // Check for height layer size changes without LINQ allocation
            bool anyHeightLayerSizeChanged = false;
            foreach (var layer in _maskDirtyLayersCache)
            {
                if (layer.GetLayerType() == LayerType.Height && layer.SizeHasChanged())
                {
                    anyHeightLayerSizeChanged = true;
                    break;
                }
            }

            bool isCurrentlyInteractive = _updateScheduler.IsCurrentUpdateInteractive();
            bool isInteractiveResize = isCurrentlyInteractive && anyHeightLayerSizeChanged;

            DebugManager.Instance?.Log(DEBUG_CLASS, DebugCategory.UpdateCycle,
                $"Phase 4 - IsCurrentlyInteractive: {isCurrentlyInteractive}, " +
                      $"AnyHeightLayerSizeChanged: {anyHeightLayerSizeChanged}, " +
                      $"IsInteractiveResize: {isInteractiveResize}");

            if (isInteractiveResize)
            {
                foreach (var layer in _maskDirtyLayersCache)
                {
                    if (layer.GetLayerType() == LayerType.Height && layer.SizeHasChanged())
                    {
                        _updateScheduler.MarkLayerForReDirty(layer);
                        DebugManager.Instance?.Log(DEBUG_CLASS, DebugCategory.UpdateCycle,
                            $"Marked '{layer.LayerName}' for re-dirty after interaction ends");
                    }
                }
            }

            var settings = GlobalToolSettingsManager.Current;
            float worldHeightScale = settings?.WorldHeightScale ?? 128f;

            foreach (var layer in _maskDirtyLayersCache)
            {
                layer.PrepareMaskResources(isInteractiveResize);
                if (layer is FeatureLayer featureLayer)
                {
                    featureLayer.SetWorldHeightScale(worldHeightScale);
                }
            }

            // Build typed layer lists without LINQ allocation
            _dirtyHeightLayersCache.Clear();
            _dirtyTextureLayersCache.Clear();
            _dirtyFeatureLayersCache.Clear();

            foreach (var layer in _maskDirtyLayersCache)
            {
                switch (layer.GetLayerType())
                {
                    case LayerType.Height:
                        _dirtyHeightLayersCache.Add(layer);
                        break;
                    case LayerType.Texture:
                        _dirtyTextureLayersCache.Add(layer);
                        break;
                    case LayerType.Feature:
                        _dirtyFeatureLayersCache.Add(layer);
                        break;
                }
            }

            DebugManager.Instance?.Log(DEBUG_CLASS, DebugCategory.PhaseExecution,
                $"GPU work: Height={_dirtyHeightLayersCache.Count}, Texture={_dirtyTextureLayersCache.Count}, Feature={_dirtyFeatureLayersCache.Count}");

            _updateProcessor.ProcessUpdatesAsync(
                _regionsToProcessCache,
                _dirtyHeightLayersCache,
                _dirtyTextureLayersCache,
                _dirtyFeatureLayersCache,
                _regionDependencyManager.GetActiveRegionCoords(),
                isInteractiveResize,
                _selectedLayer
            );

            _regionsProcessedThisUpdate = new HashSet<Vector2I>(_regionsToProcessCache);
            _regionsToRemoveAfterPush = regionsToRemove;
            _currentPhase = UpdatePhase.Processing;

            // Check for instancer layers without LINQ allocation
            _hasInstancersThisUpdate = false;
            foreach (var layer in allLayers)
            {
                if (layer is InstancerLayer il && GodotObject.IsInstanceValid(il) && il.IsDirty)
                {
                    _hasInstancersThisUpdate = true;
                    break;
                }
            }

            if (_hasInstancersThisUpdate)
            {
                DebugManager.Instance?.Log(DEBUG_CLASS, DebugCategory.UpdateCycle,
                    "Update includes instancer layer(s)");
            }

            foreach (var layer in _positionDirtyLayersCache) layer.ClearPositionDirty();
            foreach (var layer in _maskDirtyLayersCache) layer.ClearDirty();

            DebugManager.Instance?.Log(DEBUG_CLASS, DebugCategory.UpdateCycle,
                "GPU work submitted");
        }
        #endregion

        #region State Machine Phase Handlers

        private void ProcessIdlePhase(GlobalToolSettings settings)
        {
            // Skip normal processing if brush stroke is active
            if (TerrainBrushManager.IsAnyStrokeActive)
            {
                DebugManager.Instance?.Log(DEBUG_CLASS, DebugCategory.UpdateCycle,
                    "Skipping pipeline - brush stroke active");
                return;
            }

            if (_updateScheduler.ShouldProcessUpdate() &&
                (_terrain3DIntegration == null || !_terrain3DIntegration.HasPendingPushes))
            {
                ProcessUpdate();
                _updateScheduler.CompleteUpdateCycle();
            }
            else if (_updateScheduler.ShouldProcessUpdate() && _terrain3DIntegration?.HasPendingPushes == true)
            {
                DebugManager.Instance?.Log(DEBUG_CLASS, DebugCategory.UpdateCycle,
                    $"Deferring update - push still pending, on frame {Engine.GetProcessFrames()}");
            }
        }

        private void ProcessProcessingPhase()
        {
            if (!AsyncGpuTaskManager.Instance.HasPendingWork)
            {
                _currentPhase = UpdatePhase.ReadyToPush;
                DebugManager.Instance?.Log(DEBUG_CLASS, DebugCategory.UpdateCycle,
                    $"GPU work complete - ready to push, on frame {Engine.GetProcessFrames()}");
            }
        }

        private void ProcessReadyToPushPhase(GlobalToolSettings settings)
        {
            // Mark regions as updated
            foreach (var regionCoord in _regionsProcessedThisUpdate)
            {
                _regionDependencyManager.MarkRegionUpdated(regionCoord);
            }

            DebugManager.Instance?.Log(DEBUG_CLASS, DebugCategory.UpdateCycle,
                $"Marked {_regionsProcessedThisUpdate.Count} regions as updated, on frame {Engine.GetProcessFrames()}");

            // Push terrain data
            if (settings?.AutoPushToTerrain ?? false)
            {
                PushUpdatesToTerrain();
            }

            // Handle region removal (deferred until after push)
            if (_regionsToRemoveAfterPush.Count > 0)
            {
                foreach (var region in _regionsToRemoveAfterPush)
                {
                    _regionMapManager.RemoveRegion(region);
                }
                _regionsToRemoveAfterPush.Clear();
            }

            // Check if we have instancers that need processing
            if (_hasInstancersThisUpdate && _regionsWithPendingInstances.Count > 0)
            {
                _currentPhase = UpdatePhase.WaitingForTerrainPush;
                DebugManager.Instance?.Log(DEBUG_CLASS, DebugCategory.UpdateCycle,
                    $"Transitioning to WaitingForTerrainPush - {_regionsWithPendingInstances.Count} regions have pending instances");
            }
            else
            {
                // No instancers - complete the cycle
                CompleteUpdateCycle();
            }
        }

        private void ProcessWaitingForTerrainPushPhase()
        {
            // Wait for terrain push to complete before pushing instances
            if (_terrain3DIntegration == null || !_terrain3DIntegration.HasPendingPushes)
            {
                DebugManager.Instance?.Log(DEBUG_CLASS, DebugCategory.UpdateCycle,
                    $"Terrain push complete - performing instance readback, on frame {Engine.GetProcessFrames()}");

                // Perform readback of all pending instance buffers
                PerformInstanceReadback();

                _currentPhase = UpdatePhase.ReadyToPushInstances;
            }
        }

        private void ProcessReadyToPushInstancesPhase()
        {
            DebugManager.Instance?.Log(DEBUG_CLASS, DebugCategory.UpdateCycle,
                $"Pushing instances to Terrain3D, on frame {Engine.GetProcessFrames()}");

            // Guard against pushing during initialization
            if (!CanPushInstances())
            {
                DebugManager.Instance?.LogWarning(DEBUG_CLASS,
                    "Skipping instance push - Terrain3D not ready");
                CompleteUpdateCycle();
                return;
            }

            // Push instances to Terrain3D
            PushInstancesToTerrain();

            // Complete the cycle
            CompleteUpdateCycle();
        }

        /// <summary>
        /// Checks if the system is ready to push instances to Terrain3D.
        /// </summary>
        private bool CanPushInstances()
        {
            if (!_fullyInitialized)
                return false;

            if (_terrain3DIntegration == null)
                return false;

            if (!_terrain3DConnector.IsConnected)
                return false;

            if (!_terrain3DConnector.ValidateConnection())
                return false;

            return _terrain3DIntegration.IsReadyForInstancePush();
        }

        private void CompleteUpdateCycle()
        {
            _regionsProcessedThisUpdate.Clear();
            _hasInstancersThisUpdate = false;
            _regionsWithPendingInstances.Clear();
            _pendingInstanceBuffers.Clear();
            _currentPhase = UpdatePhase.Idle;

            // Cleanup tracking for removed layers
            CleanupRemovedInstancerLayers();

            DebugManager.Instance?.Log(DEBUG_CLASS, DebugCategory.UpdateCycle,
                $"Update cycle complete, on frame {Engine.GetProcessFrames()}");

            _debugManager?.FlushAggregatedMessages();
        }

        #endregion

        #region Instance Management

        /// <summary>
        /// Called by InstancerPlacementPhase to register pending instance buffers for a region.
        /// Also persists the buffer to RegionData for future aggregation by other layers.
        /// </summary>
        public void RegisterPendingInstanceBuffer(ulong layerInstanceId, Vector2I regionCoords, InstanceBuffer buffer)
        {
            // Track as pending (fresh) this cycle
            if (!_pendingInstanceBuffers.ContainsKey(layerInstanceId))
            {
                _pendingInstanceBuffers[layerInstanceId] = new Dictionary<Vector2I, InstanceBuffer>();
            }
            _pendingInstanceBuffers[layerInstanceId][regionCoords] = buffer;
            _regionsWithPendingInstances.Add(regionCoords);
            _hasInstancersThisUpdate = true;

            // Persist to RegionData for future cycles when this layer isn't dirty
            var regionData = _regionMapManager.GetRegionData(regionCoords);
            regionData?.SetInstanceBuffer(layerInstanceId, buffer);

            DebugManager.Instance?.Log(DEBUG_CLASS, DebugCategory.UpdateCycle,
                $"Registered pending instance buffer for layer {layerInstanceId} in region {regionCoords}");
        }

        /// <summary>
        /// Performs GPU readback of all pending instance buffers.
        /// Called after terrain push completes.
        /// </summary>
        private void PerformInstanceReadback()
        {
            DebugManager.Instance?.StartTimer(DEBUG_CLASS, DebugCategory.UpdateCycle, "InstanceReadback");

            int totalBuffers = 0;
            int totalInstances = 0;

            foreach (var layerBuffers in _pendingInstanceBuffers)
            {
                ulong layerId = layerBuffers.Key;

                foreach (var regionBufferPair in layerBuffers.Value)
                {
                    Vector2I regionCoords = regionBufferPair.Key;
                    InstanceBuffer buffer = regionBufferPair.Value;

                    buffer.Readback();
                    totalBuffers++;
                    totalInstances += buffer.InstanceCount;

                    // DIAGNOSTIC: Show which mesh IDs are present after readback
                    var presentMeshIds = buffer.GetPresentMeshIndices();
                    DebugManager.Instance?.Log(DEBUG_CLASS, DebugCategory.TerrainPush,
                        $"Buffer readback for layer {layerId}, region {regionCoords}: {buffer.InstanceCount} total instances");

                    foreach (int meshId in presentMeshIds)
                    {
                        int count = buffer.GetTransformsForMesh(meshId).Length;
                        DebugManager.Instance?.Log(DEBUG_CLASS, DebugCategory.TerrainPush,
                            $"  MeshId {meshId}: {count} instances");
                    }
                }
            }

            DebugManager.Instance?.EndTimer(DEBUG_CLASS, DebugCategory.UpdateCycle, "InstanceReadback");
            DebugManager.Instance?.Log(DEBUG_CLASS, DebugCategory.PerformanceMetrics,
                $"Instance readback complete: {totalBuffers} buffers, {totalInstances} total instances");
        }

        /// <summary>
        /// Pushes all pending instances to Terrain3D's instancer system.
        /// Aggregates from both fresh (dirty) and persisted (non-dirty) layer buffers.
        /// </summary>
        private void PushInstancesToTerrain()
        {
            if (_terrain3DIntegration == null) return;

            if (!_fullyInitialized)
            {
                DebugManager.Instance?.Log(DEBUG_CLASS, DebugCategory.TerrainPush,
                    "Skipping instance push - not fully initialized");
                return;
            }

            DebugManager.Instance?.Log(DEBUG_CLASS, DebugCategory.TerrainPush,
        $"=== PUSH DEBUG START ===");
            foreach (var (layerId, regions) in _pendingInstanceBuffers)
            {
                DebugManager.Instance?.Log(DEBUG_CLASS, DebugCategory.TerrainPush,
                    $"Layer {layerId} pending regions: {string.Join(", ", regions.Keys)}");
                foreach (var (regionCoords, buffer) in regions)
                {
                    DebugManager.Instance?.Log(DEBUG_CLASS, DebugCategory.TerrainPush,
                        $"  {regionCoords}: HasReadback={buffer.HasReadbackData}, Count={buffer.InstanceCount}");
                }
            }
            DebugManager.Instance?.Log(DEBUG_CLASS, DebugCategory.TerrainPush,
                $"=== PUSH DEBUG END ===");

            // Collect aggregated instance data, handling orphaned regions
            var (instanceData, regionsToClear, orphanedBuffers) = CollectAggregatedInstanceData();

            // Push to Terrain3D
            _terrain3DIntegration.PushInstancesToTerrain(instanceData, regionsToClear);

            // Clean up orphaned buffers from RegionData
            CleanupOrphanedInstanceBuffers(orphanedBuffers);
        }

        /// <summary>
        /// Collects instance transform data by aggregating from all layers.
        /// For dirty layers: uses fresh buffer data from this cycle.
        /// For non-dirty layers: uses persisted buffer data from RegionData.
        /// 
        /// Returns:
        /// - instanceData: aggregated transforms per (region, meshId)
        /// - regionsToClear: which mesh IDs to clear per region
        /// - orphanedBuffers: buffers to remove (layer left the region)
        /// </summary>
        private (
    List<(Vector2I regionCoords, int meshAssetId, Transform3D[] transforms)> instanceData,
    Dictionary<Vector2I, HashSet<int>> regionsToClear,
    List<(Vector2I regionCoords, ulong layerId)> orphanedBuffers
) CollectAggregatedInstanceData()
        {
            var result = new List<(Vector2I, int, Transform3D[])>();
            var regionsToClear = new Dictionary<Vector2I, HashSet<int>>();
            var orphanedBuffers = new List<(Vector2I, ulong)>();

            var allInstancerLayers = _layerCollectionManager.Layers
                .OfType<InstancerLayer>()
                .Where(l => GodotObject.IsInstanceValid(l))
                .ToList();

            var layerLookup = allInstancerLayers.ToDictionary(l => l.GetInstanceId(), l => l);
            var dirtyLayerIds = _pendingInstanceBuffers.Keys.ToHashSet();

            // Track current mesh IDs for this cycle
            var currentMeshIdsByLayer = new Dictionary<ulong, HashSet<int>>();

            // First pass: determine which regions actually have instance data
            var regionsWithData = new Dictionary<ulong, HashSet<Vector2I>>();

            foreach (var layerId in dirtyLayerIds)
            {
                if (!_pendingInstanceBuffers.TryGetValue(layerId, out var pendingRegions)) continue;
                if (!layerLookup.TryGetValue(layerId, out var layer)) continue;

                regionsWithData[layerId] = new HashSet<Vector2I>();
                currentMeshIdsByLayer[layerId] = new HashSet<int>(layer.GetMeshAssetIds());

                foreach (var regionBufferPair in pendingRegions)
                {
                    Vector2I regionCoords = regionBufferPair.Key;
                    InstanceBuffer buffer = regionBufferPair.Value;

                    if (buffer.HasReadbackData && buffer.InstanceCount > 0)
                    {
                        regionsWithData[layerId].Add(regionCoords);
                        DebugManager.Instance?.Log(DEBUG_CLASS, DebugCategory.TerrainPush,
                            $"Region {regionCoords} has {buffer.InstanceCount} instances");
                    }
                    else
                    {
                        DebugManager.Instance?.Log(DEBUG_CLASS, DebugCategory.TerrainPush,
                            $"Skipping region {regionCoords} - 0 instances (mask doesn't overlap)");
                    }
                }
            }

            // Determine orphaned regions: was in previous, NOT in current regions with data
            var orphanedRegionsByLayer = new Dictionary<ulong, HashSet<Vector2I>>();
            foreach (var (layerId, previousRegions) in _previousInstanceRegions)
            {
                if (!dirtyLayerIds.Contains(layerId)) continue;

                var currentWithData = regionsWithData.GetValueOrDefault(layerId, new HashSet<Vector2I>());
                var orphaned = previousRegions.Except(currentWithData).ToHashSet();

                if (orphaned.Count > 0)
                {
                    orphanedRegionsByLayer[layerId] = orphaned;
                    DebugManager.Instance?.Log(DEBUG_CLASS, DebugCategory.UpdateCycle,
                        $"Layer {layerId} has {orphaned.Count} orphaned regions: {string.Join(", ", orphaned)}");
                }
            }

            // Build regions to clear - include BOTH current AND previous mesh IDs
            foreach (var layerId in dirtyLayerIds)
            {
                if (!layerLookup.TryGetValue(layerId, out var layer)) continue;

                // Get current mesh IDs
                var currentMeshIds = currentMeshIdsByLayer.GetValueOrDefault(layerId, new HashSet<int>());

                // Get previous mesh IDs (for removed meshes)
                var previousMeshIds = _previousMeshIdsByLayer.GetValueOrDefault(layerId, new HashSet<int>());

                // Union of current and previous - ensures removed meshes get cleared
                var allMeshIdsToConsider = new HashSet<int>(currentMeshIds);
                allMeshIdsToConsider.UnionWith(previousMeshIds);

                DebugManager.Instance?.Log(DEBUG_CLASS, DebugCategory.TerrainPush,
                    $"Layer {layerId}: current meshes [{string.Join(", ", currentMeshIds)}], " +
                    $"previous meshes [{string.Join(", ", previousMeshIds)}], " +
                    $"clearing [{string.Join(", ", allMeshIdsToConsider)}]");

                // Clear regions that have fresh data
                if (regionsWithData.TryGetValue(layerId, out var dataRegions))
                {
                    DebugManager.Instance?.Log(DEBUG_CLASS, DebugCategory.TerrainPush,
                        $"Layer {layerId}: clearing {dataRegions.Count} regions with data");

                    foreach (var regionCoords in dataRegions)
                    {
                        if (!regionsToClear.ContainsKey(regionCoords))
                            regionsToClear[regionCoords] = new HashSet<int>();

                        foreach (var meshId in allMeshIdsToConsider)
                            regionsToClear[regionCoords].Add(meshId);
                    }
                }

                // Clear orphaned regions (layer actually moved away from these)
                if (orphanedRegionsByLayer.TryGetValue(layerId, out var orphaned))
                {
                    DebugManager.Instance?.Log(DEBUG_CLASS, DebugCategory.TerrainPush,
                        $"Layer {layerId}: clearing {orphaned.Count} orphaned regions");

                    foreach (var regionCoords in orphaned)
                    {
                        if (!regionsToClear.ContainsKey(regionCoords))
                            regionsToClear[regionCoords] = new HashSet<int>();

                        // Use ALL mesh IDs (current + previous) for orphaned regions
                        foreach (var meshId in allMeshIdsToConsider)
                            regionsToClear[regionCoords].Add(meshId);

                        orphanedBuffers.Add((regionCoords, layerId));
                    }
                }
            }

            DebugManager.Instance?.Log(DEBUG_CLASS, DebugCategory.TerrainPush,
                $"Total regions to clear: {regionsToClear.Count}");

            // Aggregate transforms for regions being cleared
            var aggregatedTransforms = new Dictionary<(Vector2I, int), List<Transform3D>>();

            foreach (var (regionCoords, meshIds) in regionsToClear)
            {
                foreach (var meshId in meshIds)
                {
                    var key = (regionCoords, meshId);
                    if (!aggregatedTransforms.ContainsKey(key))
                        aggregatedTransforms[key] = new List<Transform3D>();
                }

                foreach (var layer in allInstancerLayers)
                {
                    var layerId = layer.GetInstanceId();
                    var layerMeshIds = layer.GetMeshAssetIds();

                    if (!meshIds.Intersect(layerMeshIds).Any()) continue;

                    // Skip if this is an orphaned region for this layer
                    if (orphanedRegionsByLayer.TryGetValue(layerId, out var orphanedSet) &&
                        orphanedSet.Contains(regionCoords))
                    {
                        continue;
                    }

                    InstanceBuffer buffer = null;
                    bool isFreshBuffer = false;

                    // Try fresh buffer first
                    if (_pendingInstanceBuffers.TryGetValue(layerId, out var layerPendingBuffers) &&
                        layerPendingBuffers.TryGetValue(regionCoords, out var pendingBuffer))
                    {
                        buffer = pendingBuffer;
                        isFreshBuffer = true;
                    }
                    // Then try persisted buffer from RegionData
                    else
                    {
                        var regionData = _regionMapManager.GetRegionData(regionCoords);
                        buffer = regionData?.GetInstanceBuffer(layerId);
                    }

                    if (buffer == null || !buffer.HasReadbackData || buffer.InstanceCount == 0)
                        continue;

                    foreach (var meshId in layerMeshIds)
                    {
                        if (!meshIds.Contains(meshId)) continue;

                        var transforms = buffer.GetTransformsForMesh(meshId);
                        if (transforms.Length > 0)
                        {
                            var key = (regionCoords, meshId);
                            aggregatedTransforms[key].AddRange(transforms);

                            DebugManager.Instance?.Log(DEBUG_CLASS, DebugCategory.TerrainPush,
                                $"Aggregated {transforms.Length} transforms from layer {layerId} " +
                                $"(fresh={isFreshBuffer}) for mesh {meshId} in region {regionCoords}");
                        }
                    }
                }
            }

            // Convert to result
            foreach (var ((regionCoords, meshId), transforms) in aggregatedTransforms)
            {
                if (transforms.Count > 0)
                {
                    result.Add((regionCoords, meshId, transforms.ToArray()));
                }
            }

            // Update tracking for BOTH regions AND mesh IDs
            foreach (var layerId in dirtyLayerIds)
            {
                if (regionsWithData.TryGetValue(layerId, out var dataRegions))
                {
                    _previousInstanceRegions[layerId] = new HashSet<Vector2I>(dataRegions);
                }

                if (currentMeshIdsByLayer.TryGetValue(layerId, out var meshIds))
                {
                    _previousMeshIdsByLayer[layerId] = new HashSet<int>(meshIds);
                }
            }

            DebugManager.Instance?.Log(DEBUG_CLASS, DebugCategory.TerrainPush,
                $"Aggregated instance data: {result.Count} entries, " +
                $"{regionsToClear.Count} regions to clear, " +
                $"{orphanedBuffers.Count} orphaned buffers");

            return (result, regionsToClear, orphanedBuffers);
        }

        /// <summary>
        /// Removes instance buffers from RegionData for layers that no longer overlap those regions.
        /// </summary>
        private void CleanupOrphanedInstanceBuffers(List<(Vector2I regionCoords, ulong layerId)> orphanedBuffers)
        {
            foreach (var (regionCoords, layerId) in orphanedBuffers)
            {
                var regionData = _regionMapManager.GetRegionData(regionCoords);
                if (regionData != null)
                {
                    regionData.RemoveInstanceBuffer(layerId);

                    DebugManager.Instance?.Log(DEBUG_CLASS, DebugCategory.UpdateCycle,
                        $"Removed orphaned instance buffer for layer {layerId} from region {regionCoords}");
                }
            }
        }

        /// <summary>
        /// Cleans up tracking data for instancer layers that have been removed from the scene.
        /// </summary>
        private void CleanupRemovedInstancerLayers()
        {
            var currentLayerIds = _layerCollectionManager.Layers
                .OfType<InstancerLayer>()
                .Where(l => GodotObject.IsInstanceValid(l))
                .Select(l => l.GetInstanceId())
                .ToHashSet();

            // Find layer IDs that no longer exist
            var removedLayerIds = _previousInstanceRegions.Keys
                .Where(id => !currentLayerIds.Contains(id))
                .ToList();

            foreach (var layerId in removedLayerIds)
            {
                // Get the mesh IDs this layer was using
                var meshIds = _previousMeshIdsByLayer.GetValueOrDefault(layerId, new HashSet<int>());

                // Clean up instance buffers from all regions this layer touched
                if (_previousInstanceRegions.TryGetValue(layerId, out var regions))
                {
                    foreach (var regionCoords in regions)
                    {
                        // Clear instances for all mesh IDs this layer used
                        foreach (var meshId in meshIds)
                        {
                            try
                            {
                                _terrain3DIntegration?.ClearInstancesForMesh(regionCoords, meshId);
                            }
                            catch (System.Exception ex)
                            {
                                DebugManager.Instance?.LogError(DEBUG_CLASS,
                                    $"Failed to clear mesh {meshId} in region {regionCoords}: {ex.Message}");
                            }
                        }

                        var regionData = _regionMapManager.GetRegionData(regionCoords);
                        regionData?.RemoveInstanceBuffer(layerId);
                    }
                }

                _previousInstanceRegions.Remove(layerId);
                _previousMeshIdsByLayer.Remove(layerId);

                DebugManager.Instance?.Log(DEBUG_CLASS, DebugCategory.UpdateCycle,
                    $"Cleaned up tracking and buffers for removed instancer layer {layerId}");
            }
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

            _regionMapManager?.FreeAll();
            _scenePreviewManager?.Cleanup();
            _brushManager?.Cleanup();
            _updateScheduler?.Reset();
            _terrain3DConnector?.Disconnect();
        }
        #endregion

        #region Terrain Push API
        public void PushUpdatesToTerrain()
        {
            if (_terrain3DIntegration == null) return;

            var updatedRegions = _regionDependencyManager.GetAndClearUpdatedRegions();

            DebugManager.Instance?.Log(DEBUG_CLASS, DebugCategory.TerrainPush,
                $"PushUpdatesToTerrain called - updatedRegions: {updatedRegions.Count}");

            if (updatedRegions.Count == 0)
            {
                DebugManager.Instance?.Log(DEBUG_CLASS, DebugCategory.TerrainPush, "Nothing to push");
                return;
            }

            var allRegions = _regionDependencyManager.GetActiveRegionCoords().ToList();

            DebugManager.Instance?.Log(DEBUG_CLASS, DebugCategory.TerrainPush,
                $"Pushing regions: {string.Join(", ", updatedRegions)}");

            _terrain3DIntegration.PushRegionsToTerrain(updatedRegions, allRegions);
        }
        #endregion

        #region Feature Layer Support
        private void PropagateWorldHeightScaleToFeatureLayers()
        {
            if (_layerCollectionManager == null) return;

            var heightScale = GlobalToolSettingsManager.Current?.WorldHeightScale ?? 128f;

            foreach (var layer in _layerCollectionManager.Layers)
            {
                if (layer is FeatureLayer featureLayer)
                {
                    featureLayer.SetWorldHeightScale(heightScale);

                    DebugManager.Instance?.Log(DEBUG_CLASS, DebugCategory.Initialization,
                        $"Set world height scale {heightScale} on feature layer '{featureLayer.LayerName}'");
                }
            }
        }
        #endregion
    }
}

