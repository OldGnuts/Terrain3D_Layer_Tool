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

        // Storage for undo data, keyed by unique ID
        private readonly Dictionary<ulong, BrushUndoData> _undoDataStore = new();
        private ulong _nextUndoId = 0;

        #endregion

        #region Properties

        public BrushSettings Settings => _settings;
        public BrushToolType ActiveToolType => _activeToolType;
        public ManualEditLayer ActiveLayer => _activeLayer;
        public bool IsStrokeActive => _activeTool?.IsStrokeActive ?? false;

        #endregion

        #region Initialization

        public TerrainBrushManager()
        {
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

            DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.Initialization,
                $"TerrainBrushManager initialized with {_tools.Count} tools");
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

            _activeTool.BeginStroke(_activeLayer, worldPos, _settings);

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

            _activeTool.ContinueStroke(_activeLayer, worldPos, _settings);
        }

        /// <summary>
        /// Ends the current brush stroke and registers undo action.
        /// </summary>
        public void EndStroke()
        {
            if (_activeTool == null || _activeLayer == null || !_activeTool.IsStrokeActive)
                return;

            var undoData = _activeTool.EndStroke(_activeLayer);

            if (undoData != null && _undoRedo != null)
            {
                RegisterUndoAction(undoData);
            }

            // Mark layer dirty to trigger pipeline update
            _activeLayer.ForceDirty();

            DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.UpdateCycle,
                $"Stroke ended, {undoData?.AffectedRegions.Count ?? 0} regions affected");
        }

        /// <summary>
        /// Cancels the current stroke without registering undo.
        /// </summary>
        public void CancelStroke()
        {
            _activeTool?.CancelStroke();

            DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.UpdateCycle,
                "Stroke cancelled");
        }

        #endregion

        #region Undo/Redo

        private void RegisterUndoAction(BrushUndoData undoData)
        {
            if (_undoRedo == null || undoData == null) return;

            // Store undo data with a unique ID
            ulong undoId = _nextUndoId++;
            _undoDataStore[undoId] = undoData;

            _undoRedo.CreateAction(undoData.Description);

            // Do action: restore after states
            _undoRedo.AddDoMethod(this, nameof(ExecuteRedo), (long)undoId);

            // Undo action: restore before states
            _undoRedo.AddUndoMethod(this, nameof(ExecuteUndo), (long)undoId);

            _undoRedo.CommitAction();

            DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.UpdateCycle,
                $"Registered undo action: {undoData.Description}");
        }

        /// <summary>
        /// Called by undo system for redo operation.
        /// </summary>
        public void ExecuteRedo(long undoId)
        {
            if (!_undoDataStore.TryGetValue((ulong)undoId, out var undoData)) return;

            if (undoData.UndoType == BrushUndoType.InstancePlacement)
            {
                // Redo = re-add placed instances
                if (undoData.PlacedInstances != null)
                {
                    foreach (var record in undoData.PlacedInstances)
                    {
                        var buffer = undoData.Layer?.GetOrCreateEditBuffer(record.RegionCoords);
                        buffer?.AddInstance(record.MeshId, record.Transform);
                    }
                }
                // Redo = re-remove removed instances (for erase tool)
                if (undoData.RemovedInstances != null)
                {
                    foreach (var record in undoData.RemovedInstances)
                    {
                        var buffer = undoData.Layer?.GetEditBuffer(record.RegionCoords);
                        buffer?.RemoveClosestInstance(record.MeshId, record.Transform.Origin, 0.1f);
                    }
                }
            }
            else
            {
                RestoreStates(undoData.Layer, undoData.AfterStates, undoData.UndoType);
            }

            MarkRegionsDirty(undoData.Layer, undoData.AffectedRegions);
        }

        /// <summary>
        /// Called by undo system for undo operation.
        /// </summary>
        public void ExecuteUndo(long undoId)
        {
            if (!_undoDataStore.TryGetValue((ulong)undoId, out var undoData)) return;

            if (undoData.UndoType == BrushUndoType.InstancePlacement)
            {
                // Undo = remove placed instances
                if (undoData.PlacedInstances != null)
                {
                    foreach (var record in undoData.PlacedInstances)
                    {
                        var buffer = undoData.Layer?.GetEditBuffer(record.RegionCoords);
                        buffer?.RemoveClosestInstance(record.MeshId, record.Transform.Origin, 0.1f);
                    }
                }
                // Undo = re-add removed instances (for erase tool)
                if (undoData.RemovedInstances != null)
                {
                    foreach (var record in undoData.RemovedInstances)
                    {
                        var buffer = undoData.Layer?.GetOrCreateEditBuffer(record.RegionCoords);
                        buffer?.AddInstance(record.MeshId, record.Transform);
                    }
                }
            }
            else
            {
                RestoreStates(undoData.Layer, undoData.BeforeStates, undoData.UndoType);
            }

            MarkRegionsDirty(undoData.Layer, undoData.AffectedRegions);
        }

        private void RestoreStates(
            ManualEditLayer layer,
            Dictionary<Vector2I, byte[]> states,
            BrushUndoType undoType)
        {
            if (layer == null || !GodotObject.IsInstanceValid(layer)) return;

            // Handle texture-based undo (height, texture, exclusion)
            if (states != null && states.Count > 0)
            {
                foreach (var (regionCoords, data) in states)
                {
                    var buffer = layer.GetEditBuffer(regionCoords);
                    if (buffer == null) continue;

                    Rid textureRid = undoType switch
                    {
                        BrushUndoType.Height => buffer.HeightDelta,
                        BrushUndoType.Texture => buffer.TextureEdit,
                        BrushUndoType.InstanceExclusion => buffer.InstanceExclusion,
                        _ => new Rid()
                    };

                    if (textureRid.IsValid && data != null)
                    {
                        Gpu.Rd.TextureUpdate(textureRid, 0, data);
                    }
                }
            }
        }

        private void MarkRegionsDirty(ManualEditLayer layer, HashSet<Vector2I> regions)
        {
            if (layer == null || !GodotObject.IsInstanceValid(layer) || regions == null) return;

            foreach (var regionCoords in regions)
            {
                layer.MarkRegionEdited(regionCoords);
            }

            layer.ForceDirty();
        }

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