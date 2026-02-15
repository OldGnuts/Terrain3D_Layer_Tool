// /Brushes/TerrainBrushManager.cs
using Godot;
using System.Collections.Generic;
using Terrain3DTools.Core;
using Terrain3DTools.Core.Debug;
using Terrain3DTools.Layers;
using Terrain3DTools.Layers.ManualEdit;

namespace Terrain3DTools.Brushes
{
    /// <summary>
    /// Manages terrain brush tools and coordinates brush operations.
    /// Owns the brush tools and settings, handles undo/redo integration.
    /// Implements fast path updates for real-time brush feedback.
    /// Extends RefCounted to allow use with EditorUndoRedoManager.
    /// </summary>
    public partial class TerrainBrushManager : RefCounted
    {
        private const string DEBUG_CLASS_NAME = "TerrainBrushManager";

        #region Fields

        private readonly Dictionary<BrushToolType, IBrushTool> _tools = new();
        private IBrushTool _activeTool;
        private BrushToolType _activeToolType = BrushToolType.None;
        private ManualEditLayer _activeLayer;
        private BrushSettings _settings;
        private EditorUndoRedoManager _undoRedo;

        // Undo storage - session scoped with rolling window
        private readonly BrushUndoStore _undoStore = new(maxEntries: 30);
        private ulong _nextUndoId = 0;

        // Fast path state
        private BrushFastPathContext _fastPathContext;
        private RegionMapManager _regionMapManager;
        private Terrain3DIntegration _terrain3DIntegration;
        private int _regionSize;
        private float _heightScale;

        // Fast path runtime state
        private HashSet<Vector2I> _dirtyRegions = new();
        private Dictionary<Vector2I, Rid> _heightMapBackups = new();
        private Dictionary<Vector2I, Rid> _controlMapBackups = new();
        private Dictionary<Vector2I, Rid> _exclusionMapBackups = new();
        private double _timeSinceLastPush = 0;
        private double _lastDabTime = 0;

        #endregion

        #region Configuration

        /// <summary>
        /// Minimum time between terrain pushes during stroke (seconds).
        /// Lower = more responsive, higher = better performance.
        /// Default 66ms (~15Hz).
        /// </summary>
        public double PushInterval { get; set; } = 0.066;

        #endregion

        #region Properties

        public BrushSettings Settings => _settings;
        public BrushToolType ActiveToolType => _activeToolType;
        public ManualEditLayer ActiveLayer => _activeLayer;
        public bool IsStrokeActive => _activeTool?.IsStrokeActive ?? false;

        /// <summary>
        /// Returns true if a brush stroke is active (for pipeline to check).
        /// </summary>
        public static bool IsAnyStrokeActive => _instance?.IsStrokeActive ?? false;

        private static TerrainBrushManager _instance;

        #endregion

        #region Initialization

        public TerrainBrushManager()
        {
            _instance = this;
            DebugManager.Instance?.RegisterClass(DEBUG_CLASS_NAME);

            _settings = new BrushSettings();

            // Initialize tools
            _tools[BrushToolType.HeightRaise] = new HeightBrushTool(isRaise: true);
            _tools[BrushToolType.HeightLower] = new HeightBrushTool(isRaise: false);
            _tools[BrushToolType.HeightSmooth] = new HeightSmoothBrushTool();
            _tools[BrushToolType.TexturePaint] = new TextureBrushTool();
            _tools[BrushToolType.InstancePlace] = new InstancePlaceBrushTool();
            _tools[BrushToolType.InstanceErase] = new InstanceEraseBrushTool();
            _tools[BrushToolType.InstanceExclude] = new InstanceExclusionBrushTool();

            // Initialize fast path context
            _fastPathContext = new BrushFastPathContext
            {
                GetRegionData = GetRegionDataForFastPath,
                MarkRegionDirty = MarkRegionDirtyForFastPath,
                Enabled = true
            };

            DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.Initialization,
                $"TerrainBrushManager initialized with {_tools.Count} tools");
        }

        /// <summary>
        /// Initializes the fast path system with required references.
        /// Call from TerrainLayerManager after sub-managers are ready.
        /// </summary>
        public void InitializeFastPath(
            RegionMapManager regionMapManager,
            Terrain3DIntegration terrain3DIntegration,
            int regionSize,
            float heightScale)
        {
            _regionMapManager = regionMapManager;
            _terrain3DIntegration = terrain3DIntegration;
            _regionSize = regionSize;
            _heightScale = heightScale;
            _fastPathContext.RegionSize = regionSize;

            DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.Initialization,
                $"Fast path initialized: regionSize={regionSize}, heightScale={heightScale}");
        }

        /// <summary>
        /// Updates the height scale (call when settings change).
        /// </summary>
        public void SetHeightScale(float heightScale)
        {
            _heightScale = heightScale;
        }

        /// <summary>
        /// Sets the EditorUndoRedoManager for undo/redo integration.
        /// </summary>
        public void SetUndoRedo(EditorUndoRedoManager undoRedo)
        {
            _undoRedo = undoRedo;
        }

        #endregion

        #region Layer Management

        /// <summary>
        /// Sets the active layer for editing.
        /// </summary>
        public void SetActiveLayer(ManualEditLayer layer)
        {
            if (_activeTool?.IsStrokeActive == true)
            {
                _activeTool.CancelStroke();
                CleanupFastPathState();
            }

            _activeLayer = layer;

            DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.UpdateCycle,
                $"Active layer set to: {layer?.LayerName ?? "null"}");
        }

        #endregion

        #region Tool Selection

        /// <summary>
        /// Sets the active brush tool.
        /// </summary>
        public void SetActiveTool(BrushToolType toolType)
        {
            if (_activeTool?.IsStrokeActive == true)
            {
                _activeTool.CancelStroke();
                CleanupFastPathState();
            }

            _activeToolType = toolType;
            _activeTool = toolType != BrushToolType.None && _tools.TryGetValue(toolType, out var tool)
                ? tool
                : null;

            DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.UpdateCycle,
                $"Active tool set to: {toolType}");
        }

        /// <summary>
        /// Gets a tool by type.
        /// </summary>
        public IBrushTool GetTool(BrushToolType toolType)
        {
            _tools.TryGetValue(toolType, out var tool);
            return tool;
        }

        #endregion

        #region Brush Operations

        /// <summary>
        /// Begins a brush stroke at the given world position.
        /// </summary>
        public bool BeginStroke(Vector3 worldPos)
        {
            if (_activeTool == null || _activeLayer == null)
            {
                DebugManager.Instance?.LogWarning(DEBUG_CLASS_NAME,
                    "Cannot begin stroke: no active tool or layer");
                return false;
            }

            if (!ValidateLayerForTool())
            {
                return false;
            }

            // Ensure any pending GPU work is complete
            AsyncGpuTaskManager.Instance?.SyncIfNeeded();

            // Reset fast path state
            _dirtyRegions.Clear();
            _timeSinceLastPush = 0;
            _lastDabTime = Time.GetTicksMsec() / 1000.0;
            FreeAllBackups();

            _activeTool.BeginStroke(_activeLayer, worldPos, _settings, _fastPathContext);

            DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.UpdateCycle,
                $"Stroke began at {worldPos} with {_activeToolType}");

            return true;
        }

        /// <summary>
        /// Continues the current brush stroke.
        /// </summary>
        public void ContinueStroke(Vector3 worldPos)
        {
            if (_activeTool == null || _activeLayer == null || !_activeTool.IsStrokeActive)
                return;

            // Calculate delta time for throttling
            double currentTime = Time.GetTicksMsec() / 1000.0;
            double deltaTime = currentTime - _lastDabTime;
            _lastDabTime = currentTime;
            _timeSinceLastPush += deltaTime;

            _activeTool.ContinueStroke(_activeLayer, worldPos, _settings, _fastPathContext);

            // Throttled push to Terrain3D
            if (_timeSinceLastPush >= PushInterval && _dirtyRegions.Count > 0)
            {
                PushDirtyRegions();
                _timeSinceLastPush = 0;
            }
        }

        /// <summary>
        /// Ends the current brush stroke and registers undo action.
        /// </summary>
        public void EndStroke()
        {
            if (_activeTool == null || _activeLayer == null || !_activeTool.IsStrokeActive)
                return;

            // Final push before ending
            if (_dirtyRegions.Count > 0)
            {
                PushDirtyRegions();
            }

            var undoData = _activeTool.EndStroke(_activeLayer);

            if (undoData != null && _undoRedo != null)
            {
                RegisterUndoAction(undoData);
            }

            // Determine if we need to trigger instancer update
            bool needsInstancerUpdate = _activeToolType == BrushToolType.InstanceExclude;

            // Clean up backups (successful stroke)
            FreeAllBackups();
            _dirtyRegions.Clear();

            // Mark layer dirty only if instancers need updating
            if (needsInstancerUpdate)
            {
                _activeLayer.ForceDirty();
                DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.UpdateCycle,
                    "Triggering instancer update after exclusion stroke");
            }

            DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.UpdateCycle,
                $"Stroke ended, {undoData?.AffectedRegions.Count ?? 0} regions affected");
        }

        /// <summary>
        /// Cancels the current stroke without registering undo.
        /// </summary>
        public void CancelStroke()
        {
            if (_activeTool == null || !_activeTool.IsStrokeActive)
                return;

            // Ensure GPU work is complete before restoring
            AsyncGpuTaskManager.Instance?.SyncIfNeeded();

            // Restore from backups
            RestoreAllBackups();

            // Push restored state
            if (_dirtyRegions.Count > 0)
            {
                PushDirtyRegions();
            }

            _activeTool.CancelStroke();

            CleanupFastPathState();

            DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.UpdateCycle,
                "Stroke cancelled");
        }

        #endregion

        #region Fast Path - Region Data Access

        private RegionData GetRegionDataForFastPath(Vector2I regionCoords)
        {
            if (_regionMapManager == null)
            {
                GD.PrintErr("[TerrainBrushManager] _regionMapManager is NULL!");
                return null;
            }

            GD.Print($"[TerrainBrushManager] RegionMapManager has {_regionMapManager.GetManagedRegionCoords().Count} regions");
            GD.Print($"[TerrainBrushManager] Looking for region {regionCoords}, HasRegion={_regionMapManager.HasRegion(regionCoords)}");

            return _regionMapManager.GetRegionData(regionCoords);
        }

        private void MarkRegionDirtyForFastPath(Vector2I regionCoords)
        {
            if (_dirtyRegions.Add(regionCoords))
            {
                // First time touching this region - create backups
                EnsureBackups(regionCoords);
            }
        }

        #endregion

        #region Fast Path - Backup Management

        private void EnsureBackups(Vector2I regionCoords)
        {
            var regionData = _regionMapManager?.GetRegionData(regionCoords);
            if (regionData == null) return;

            // Height backup
            if (regionData.HeightMap.IsValid && !_heightMapBackups.ContainsKey(regionCoords))
            {
                var backup = CreateBackupTexture(regionData.HeightMap);
                if (backup.IsValid)
                {
                    _heightMapBackups[regionCoords] = backup;
                }
            }

            // Control map backup (for texture painting)
            if (regionData.ControlMap.IsValid && !_controlMapBackups.ContainsKey(regionCoords))
            {
                var backup = CreateBackupTexture(regionData.ControlMap);
                if (backup.IsValid)
                {
                    _controlMapBackups[regionCoords] = backup;
                }
            }

            // Exclusion map backup (if exists)
            if (regionData.HasExclusionMap && !_exclusionMapBackups.ContainsKey(regionCoords))
            {
                var exclusionMap = regionData.GetOrCreateExclusionMap(_regionSize);
                if (exclusionMap.IsValid)
                {
                    var backup = CreateBackupTexture(exclusionMap);
                    if (backup.IsValid)
                    {
                        _exclusionMapBackups[regionCoords] = backup;
                    }
                }
            }

            DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.GpuResources,
                $"Created backups for region {regionCoords}");
        }

        private Rid CreateBackupTexture(Rid source)
        {
            if (!source.IsValid) return new Rid();

            var backup = Gpu.CreateTexture2D(
                (uint)_regionSize, (uint)_regionSize,
                RenderingDevice.DataFormat.R32Sfloat,
                RenderingDevice.TextureUsageBits.StorageBit |
                RenderingDevice.TextureUsageBits.CanCopyFromBit |
                RenderingDevice.TextureUsageBits.CanCopyToBit);

            if (!backup.IsValid) return new Rid();

            Gpu.Rd.TextureCopy(
                source, backup,
                Vector3.Zero, Vector3.Zero,
                new Vector3(_regionSize, _regionSize, 1),
                0, 0, 0, 0);

            return backup;
        }

        private void RestoreAllBackups()
        {
            foreach (var (regionCoords, backup) in _heightMapBackups)
            {
                var regionData = _regionMapManager?.GetRegionData(regionCoords);
                if (regionData != null && regionData.HeightMap.IsValid && backup.IsValid)
                {
                    Gpu.Rd.TextureCopy(
                        backup, regionData.HeightMap,
                        Vector3.Zero, Vector3.Zero,
                        new Vector3(_regionSize, _regionSize, 1),
                        0, 0, 0, 0);
                }
            }

            foreach (var (regionCoords, backup) in _controlMapBackups)
            {
                var regionData = _regionMapManager?.GetRegionData(regionCoords);
                if (regionData != null && regionData.ControlMap.IsValid && backup.IsValid)
                {
                    Gpu.Rd.TextureCopy(
                        backup, regionData.ControlMap,
                        Vector3.Zero, Vector3.Zero,
                        new Vector3(_regionSize, _regionSize, 1),
                        0, 0, 0, 0);
                }
            }

            foreach (var (regionCoords, backup) in _exclusionMapBackups)
            {
                var regionData = _regionMapManager?.GetRegionData(regionCoords);
                if (regionData != null && backup.IsValid)
                {
                    var exclusionMap = regionData.GetOrCreateExclusionMap(_regionSize);
                    if (exclusionMap.IsValid)
                    {
                        Gpu.Rd.TextureCopy(
                            backup, exclusionMap,
                            Vector3.Zero, Vector3.Zero,
                            new Vector3(_regionSize, _regionSize, 1),
                            0, 0, 0, 0);
                    }
                }
            }

            DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.UpdateCycle,
                $"Restored backups: {_heightMapBackups.Count} height, {_controlMapBackups.Count} control, {_exclusionMapBackups.Count} exclusion");
        }

        private void FreeAllBackups()
        {
            foreach (var backup in _heightMapBackups.Values)
            {
                if (backup.IsValid) Gpu.FreeRid(backup);
            }
            _heightMapBackups.Clear();

            foreach (var backup in _controlMapBackups.Values)
            {
                if (backup.IsValid) Gpu.FreeRid(backup);
            }
            _controlMapBackups.Clear();

            foreach (var backup in _exclusionMapBackups.Values)
            {
                if (backup.IsValid) Gpu.FreeRid(backup);
            }
            _exclusionMapBackups.Clear();
        }

        private void CleanupFastPathState()
        {
            FreeAllBackups();
            _dirtyRegions.Clear();
            _timeSinceLastPush = 0;
        }

        #endregion

        #region Fast Path - Terrain Push

        private void PushDirtyRegions()
        {
            if (_dirtyRegions.Count == 0 || _terrain3DIntegration == null)
                return;

            DebugManager.Instance?.StartTimer(DEBUG_CLASS_NAME, DebugCategory.TerrainPush, "FastPush");

            // Sync GPU before readback - use managed sync to avoid "sync without submit" errors
            AsyncGpuTaskManager.Instance?.SyncIfNeeded();

            int successCount = 0;
            bool pushHeight = _activeToolType == BrushToolType.HeightRaise ||
                              _activeToolType == BrushToolType.HeightLower ||
                              _activeToolType == BrushToolType.HeightSmooth;
            bool pushControl = _activeToolType == BrushToolType.TexturePaint;

            foreach (var regionCoords in _dirtyRegions)
            {
                bool success = false;

                if (pushHeight)
                {
                    success = _terrain3DIntegration.PushRegionHeightFast(regionCoords, _heightScale);
                }
                else if (pushControl)
                {
                    success = _terrain3DIntegration.PushRegionControlFast(regionCoords);
                }

                if (success) successCount++;
            }

            _dirtyRegions.Clear();

            DebugManager.Instance?.EndTimer(DEBUG_CLASS_NAME, DebugCategory.TerrainPush, "FastPush");
            DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.TerrainPush,
                $"Fast push: {successCount} regions");
        }

        #endregion

        #region Undo/Redo

        private void RegisterUndoAction(BrushUndoData undoData)
        {
            if (_undoRedo == null || undoData == null) return;

            // Store undo data with a unique ID
            ulong undoId = _nextUndoId++;
            _undoStore.Store(undoId, undoData);

            _undoRedo.CreateAction(undoData.Description);

            // Do action: restore after states (redo)
            _undoRedo.AddDoMethod(this, nameof(ExecuteRedo), (long)undoId);

            // Undo action: restore before states
            _undoRedo.AddUndoMethod(this, nameof(ExecuteUndo), (long)undoId);

            _undoRedo.CommitAction();

            DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.UpdateCycle,
                $"Registered undo action: {undoData.Description} " +
                $"(store: {_undoStore.Count} entries, ~{_undoStore.EstimatedMemoryUsage / 1024}KB)");
        }

        /// <summary>
        /// Called by undo system for redo operation.
        /// </summary>
        public void ExecuteRedo(long undoId)
        {
            var undoData = _undoStore.Get((ulong)undoId);
            if (undoData == null)
            {
                DebugManager.Instance?.LogWarning(DEBUG_CLASS_NAME,
                    $"Redo data not found (pruned): {undoId}");
                return;
            }

            // Ensure GPU work is complete before modifying textures
            AsyncGpuTaskManager.Instance?.SyncIfNeeded();

            // Disable fast path during undo/redo
            _fastPathContext.Enabled = false;

            try
            {
                if (undoData.UndoType == BrushUndoType.InstancePlacement)
                {
                    ExecuteInstanceRedo(undoData);
                }
                else if (undoData.RegionPatches != null)
                {
                    ExecutePatchRedo(undoData);
                }

                MarkRegionsDirtyAndPush(undoData.Layer, undoData.AffectedRegions, undoData.UndoType);
            }
            finally
            {
                _fastPathContext.Enabled = true;
            }
        }

        /// <summary>
        /// Called by undo system for undo operation.
        /// </summary>
        public void ExecuteUndo(long undoId)
        {
            var undoData = _undoStore.Get((ulong)undoId);
            if (undoData == null)
            {
                DebugManager.Instance?.LogWarning(DEBUG_CLASS_NAME,
                    $"Undo data not found (pruned): {undoId}");
                return;
            }

            // Ensure GPU work is complete before modifying textures
            AsyncGpuTaskManager.Instance?.SyncIfNeeded();

            // Disable fast path during undo/redo
            _fastPathContext.Enabled = false;

            try
            {
                if (undoData.UndoType == BrushUndoType.InstancePlacement)
                {
                    ExecuteInstanceUndo(undoData);
                }
                else if (undoData.RegionPatches != null)
                {
                    ExecutePatchUndo(undoData);
                }

                MarkRegionsDirtyAndPush(undoData.Layer, undoData.AffectedRegions, undoData.UndoType);
            }
            finally
            {
                _fastPathContext.Enabled = true;
            }
        }

        /// <summary>
        /// Executes patch-based undo (restores before state).
        /// </summary>
        private void ExecutePatchUndo(BrushUndoData undoData)
        {
            if (undoData.Layer == null || !GodotObject.IsInstanceValid(undoData.Layer))
            {
                DebugManager.Instance?.LogWarning(DEBUG_CLASS_NAME,
                    "Cannot execute undo: layer is invalid");
                return;
            }

            foreach (var (regionCoords, patch) in undoData.RegionPatches)
            {
                var buffer = undoData.Layer.GetEditBuffer(regionCoords);
                if (buffer == null)
                {
                    DebugManager.Instance?.LogWarning(DEBUG_CLASS_NAME,
                        $"No buffer for region {regionCoords} during undo");
                    continue;
                }

                switch (undoData.UndoType)
                {
                    case BrushUndoType.Height:
                        if (patch.BeforeData != null)
                        {
                            buffer.WriteWorkingHeightSubrect(patch.Bounds, patch.BeforeData);
                            buffer.CommitHeightDelta();
                        }
                        break;

                    case BrushUndoType.Texture:
                        if (patch.BeforeDataUint != null)
                        {
                            buffer.WriteWorkingTextureSubrect(patch.Bounds, patch.BeforeDataUint);
                            buffer.CommitTextureEdit();
                        }
                        break;

                    case BrushUndoType.InstanceExclusion:
                        if (patch.BeforeData != null)
                        {
                            buffer.WriteWorkingExclusionSubrect(patch.Bounds, patch.BeforeData);
                            buffer.CommitInstanceExclusion();
                        }
                        break;
                }

                DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.UpdateCycle,
                    $"Restored before state for region {regionCoords}");
            }
        }

        /// <summary>
        /// Executes patch-based redo (restores after state).
        /// </summary>
        private void ExecutePatchRedo(BrushUndoData undoData)
        {
            if (undoData.Layer == null || !GodotObject.IsInstanceValid(undoData.Layer))
            {
                DebugManager.Instance?.LogWarning(DEBUG_CLASS_NAME,
                    "Cannot execute redo: layer is invalid");
                return;
            }

            foreach (var (regionCoords, patch) in undoData.RegionPatches)
            {
                var buffer = undoData.Layer.GetEditBuffer(regionCoords);
                if (buffer == null)
                {
                    DebugManager.Instance?.LogWarning(DEBUG_CLASS_NAME,
                        $"No buffer for region {regionCoords} during redo");
                    continue;
                }

                switch (undoData.UndoType)
                {
                    case BrushUndoType.Height:
                        if (patch.AfterData != null)
                        {
                            buffer.WriteWorkingHeightSubrect(patch.Bounds, patch.AfterData);
                            buffer.CommitHeightDelta();
                        }
                        break;

                    case BrushUndoType.Texture:
                        if (patch.AfterDataUint != null)
                        {
                            buffer.WriteWorkingTextureSubrect(patch.Bounds, patch.AfterDataUint);
                            buffer.CommitTextureEdit();
                        }
                        break;

                    case BrushUndoType.InstanceExclusion:
                        if (patch.AfterData != null)
                        {
                            buffer.WriteWorkingExclusionSubrect(patch.Bounds, patch.AfterData);
                            buffer.CommitInstanceExclusion();
                        }
                        break;
                }

                DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.UpdateCycle,
                    $"Restored after state for region {regionCoords}");
            }
        }

        /// <summary>
        /// Executes instance placement undo (removes placed, re-adds removed).
        /// </summary>
        private void ExecuteInstanceUndo(BrushUndoData undoData)
        {
            if (undoData.Layer == null || !GodotObject.IsInstanceValid(undoData.Layer))
                return;

            // Remove instances that were placed
            if (undoData.PlacedInstances != null)
            {
                foreach (var record in undoData.PlacedInstances)
                {
                    var buffer = undoData.Layer.GetEditBuffer(record.RegionCoords);
                    buffer?.RemoveClosestInstance(record.MeshId, record.Transform.Origin, 0.1f);
                }
            }

            // Re-add instances that were removed
            if (undoData.RemovedInstances != null)
            {
                foreach (var record in undoData.RemovedInstances)
                {
                    var buffer = undoData.Layer.GetOrCreateEditBuffer(record.RegionCoords);
                    buffer?.AddInstance(record.MeshId, record.Transform);
                }
            }
        }

        /// <summary>
        /// Executes instance placement redo (re-adds placed, re-removes removed).
        /// </summary>
        private void ExecuteInstanceRedo(BrushUndoData undoData)
        {
            if (undoData.Layer == null || !GodotObject.IsInstanceValid(undoData.Layer))
                return;

            // Re-add instances that were placed
            if (undoData.PlacedInstances != null)
            {
                foreach (var record in undoData.PlacedInstances)
                {
                    var buffer = undoData.Layer.GetOrCreateEditBuffer(record.RegionCoords);
                    buffer?.AddInstance(record.MeshId, record.Transform);
                }
            }

            // Re-remove instances that were removed
            if (undoData.RemovedInstances != null)
            {
                foreach (var record in undoData.RemovedInstances)
                {
                    var buffer = undoData.Layer.GetEditBuffer(record.RegionCoords);
                    buffer?.RemoveClosestInstance(record.MeshId, record.Transform.Origin, 0.1f);
                }
            }
        }

        /// <summary>
        /// Marks regions dirty and triggers appropriate pipeline/push for undo/redo.
        /// </summary>
        private void MarkRegionsDirtyAndPush(ManualEditLayer layer, HashSet<Vector2I> regions, BrushUndoType undoType)
        {
            if (layer == null || !GodotObject.IsInstanceValid(layer) || regions == null)
                return;

            // For undo/redo, we need to trigger full pipeline to reapply edits
            foreach (var regionCoords in regions)
            {
                layer.MarkRegionEdited(regionCoords);
            }

            layer.ForceDirty();
        }

        /// <summary>
        /// Clears all undo history. Call on save or when switching layers.
        /// </summary>
        public void ClearUndoHistory()
        {
            _undoStore.Clear();

            DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.Cleanup,
                "Undo history cleared");
        }

        /// <summary>
        /// Gets the current number of undo entries stored.
        /// </summary>
        public int UndoEntryCount => _undoStore.Count;

        /// <summary>
        /// Gets the estimated memory usage of undo storage in bytes.
        /// </summary>
        public long UndoMemoryUsage => _undoStore.EstimatedMemoryUsage;

        #endregion

        #region Validation

        private bool ValidateLayerForTool()
        {
            if (_activeLayer == null) return false;

            bool valid = _activeToolType switch
            {
                BrushToolType.HeightRaise or
                BrushToolType.HeightLower or
                BrushToolType.HeightSmooth => _activeLayer.HeightEditingEnabled,

                BrushToolType.TexturePaint => _activeLayer.TextureEditingEnabled,

                BrushToolType.InstancePlace or
                BrushToolType.InstanceErase or
                BrushToolType.InstanceExclude => _activeLayer.InstanceEditingEnabled,

                _ => false
            };

            if (!valid)
            {
                DebugManager.Instance?.LogWarning(DEBUG_CLASS_NAME,
                    $"Tool {_activeToolType} not enabled on layer {_activeLayer.LayerName}");
            }

            return valid;
        }

        #endregion

        #region Cleanup

        public void Cleanup()
        {
            if (_activeTool?.IsStrokeActive == true)
            {
                CancelStroke();
            }

            CleanupFastPathState();
            _undoStore.Clear();
            _instance = null;
        }

        #endregion
    }

    public enum BrushToolType
    {
        None,
        HeightRaise,
        HeightLower,
        HeightSmooth,
        TexturePaint,
        InstancePlace,
        InstanceErase,
        InstanceExclude
    }
}