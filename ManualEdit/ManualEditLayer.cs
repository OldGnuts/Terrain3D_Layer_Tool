// /Layers/ManualEdit/ManualEditLayer.cs

using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using Terrain3DTools.Core;
using Terrain3DTools.Core.Debug;
using Terrain3DTools.Layers.ManualEdit;
using Terrain3DTools.Utils;

namespace Terrain3DTools.Layers
{
    /// <summary>
    /// A terrain layer that stores manual edits made by the user.
    /// Fixed at world origin (0,0,0), with bounds computed from edited regions.
    /// Multiple ManualEditLayers can exist for organizational purposes
    /// (e.g., "River touchups", "Road edges").
    /// </summary>
    [GlobalClass, Tool]
    public partial class ManualEditLayer : FeatureLayer
    {
        #region Constants
        private const string DEBUG_CLASS_NAME = "ManualEditLayer";
        #endregion

        #region Private Fields

        private HashSet<Vector2I> _editedRegions = new();
        private Dictionary<Vector2I, ManualEditBuffer> _editBuffers = new();
        private int _regionSize = 0;
        private bool _boundsNeedUpdate = true;
        private Vector2 _cachedMinBounds;
        private Vector2 _cachedMaxBounds;

        #endregion

        #region Exported Properties

        [ExportGroup("Edit Layer Settings")]

        [Export]
        public string Description { get; set; } = "";

        [Export]
        public bool HeightEditingEnabled { get; set; } = true;

        [Export]
        public bool TextureEditingEnabled { get; set; } = true;

        [Export]
        public bool InstanceEditingEnabled { get; set; } = true;

        [ExportGroup("Debug")]

        [Export]
        public int EditedRegionCount
        {
            get => _editedRegions.Count;
            set { } // Read-only in practice, setter required for export
        }

        #endregion

        #region Properties

        /// <summary>
        /// Gets all regions that have edits.
        /// </summary>
        public IReadOnlyCollection<Vector2I> EditedRegions => _editedRegions;

        /// <summary>
        /// Gets the cached region size from TerrainLayerManager.
        /// </summary>
        public int RegionSize => _regionSize;

        #endregion

        #region Godot Lifecycle

        static ManualEditLayer()
        {
            DebugManager.Instance?.RegisterClass(DEBUG_CLASS_NAME);
        }

        public override void _Ready()
        {
            base._Ready();

            // Initialize layer name if not set
            if (string.IsNullOrEmpty(LayerName) ||
                LayerName.StartsWith("New Layer") ||
                LayerName.StartsWith("Feature Layer"))
            {
                LayerName = $"Manual Edits {IdGenerator.GenerateShortUid()}";
            }

            // Declare what we modify based on enabled flags
            UpdateModifiesFlags();

            if (Engine.IsEditorHint())
            {
                ResetTransformToOrigin();
            }

            DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.Initialization,
                $"ManualEditLayer '{LayerName}' ready");
        }

        public override void _Notification(int what)
        {
            if (what == NotificationTransformChanged && Engine.IsEditorHint())
            {
                // Force back to origin like PathLayer
                if (GlobalPosition != Vector3.Zero || GlobalRotation != Vector3.Zero || Scale != Vector3.One)
                {
                    CallDeferred(nameof(ResetTransformToOrigin));
                }
                return;
            }

            base._Notification(what);

            if (what == (int)NotificationPredelete)
            {
                FreeAllBuffers();
            }
        }

        public override void _ValidateProperty(Godot.Collections.Dictionary property)
        {
            base._ValidateProperty(property);
            string name = property["name"].AsString();

            // Hide properties that don't apply to manual edit layers
            if (name == "Masks" || name == "FalloffStrength" ||
                name == "FalloffCurve" || name == "FalloffMode" ||
                name == "Size")
            {
                property["usage"] = (int)PropertyUsageFlags.NoEditor;
            }
        }

        private void ResetTransformToOrigin()
        {
            GlobalPosition = Vector3.Zero;
            GlobalRotation = Vector3.Zero;
            Scale = Vector3.One;
        }

        #endregion

        #region TerrainLayerBase Overrides

        public override LayerType GetLayerType() => LayerType.Feature;

        public override string LayerTypeName() => "Manual Edit Layer";

        public override (Vector2 Min, Vector2 Max) GetWorldBounds()
        {
            if (_boundsNeedUpdate)
            {
                ComputeBoundsFromEditedRegions();
            }
            return (_cachedMinBounds, _cachedMaxBounds);
        }

        public override bool SizeHasChanged()
        {
            // Manual edit layers don't have a single size - they span edited regions
            return false;
        }

        public override void PrepareMaskResources(bool isInteractive)
        {
            // Manual edit layers don't use the standard mask texture
            // Edit data is stored per-region in ManualEditBuffers
        }

        /// <summary>
        /// Manual edit layers don't use the standard CreateApplyRegionCommands.
        /// They have dedicated height and texture application methods.
        /// </summary>
        public override (Action<long> commands, List<Rid> tempRids, List<string>) CreateApplyRegionCommands(
            Vector2I regionCoords,
            RegionData regionData,
            int regionSize,
            Vector2 regionMinWorld,
            Vector2 regionSizeWorld)
        {
            // This is handled by the ManualEditApplicationPhase
            return (null, new List<Rid>(), new List<string>());
        }

        #endregion

        #region Region Size Management

        /// <summary>
        /// Sets the region size. Called by TerrainLayerManager during initialization.
        /// </summary>
        public void SetRegionSize(int regionSize)
        {
            if (_regionSize != regionSize && regionSize > 0)
            {
                _regionSize = regionSize;
                DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.Initialization,
                    $"Region size set to {regionSize}");
            }
        }

        #endregion

        #region Edit Buffer Management

        /// <summary>
        /// Gets or creates an edit buffer for the specified region.
        /// </summary>
        public ManualEditBuffer GetOrCreateEditBuffer(Vector2I regionCoords)
        {
            if (_regionSize <= 0)
            {
                DebugManager.Instance?.LogError(DEBUG_CLASS_NAME,
                    "Cannot create edit buffer: region size not set");
                return null;
            }

            if (!_editBuffers.TryGetValue(regionCoords, out var buffer))
            {
                buffer = new ManualEditBuffer(_regionSize);
                _editBuffers[regionCoords] = buffer;
                _editedRegions.Add(regionCoords);
                _boundsNeedUpdate = true;

                DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.RegionLifecycle,
                    $"Created edit buffer for region {regionCoords}");
            }

            return buffer;
        }

        /// <summary>
        /// Gets an existing edit buffer, or null if none exists for this region.
        /// </summary>
        public ManualEditBuffer GetEditBuffer(Vector2I regionCoords)
        {
            _editBuffers.TryGetValue(regionCoords, out var buffer);
            return buffer;
        }

        /// <summary>
        /// Checks if a region has any edits.
        /// </summary>
        public bool HasEditsInRegion(Vector2I regionCoords)
        {
            return _editedRegions.Contains(regionCoords);
        }

        /// <summary>
        /// Removes all edits from a region and frees its buffer.
        /// </summary>
        public void ClearRegionEdits(Vector2I regionCoords)
        {
            if (_editBuffers.TryGetValue(regionCoords, out var buffer))
            {
                buffer.Free();
                _editBuffers.Remove(regionCoords);
                _editedRegions.Remove(regionCoords);
                _boundsNeedUpdate = true;

                ForceDirty();

                DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.RegionLifecycle,
                    $"Cleared edits for region {regionCoords}");
            }
        }

        /// <summary>
        /// Clears all edits from all regions.
        /// </summary>
        public void ClearAllEdits()
        {
            FreeAllBuffers();
            ForceDirty();

            DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.RegionLifecycle,
                "Cleared all edits");
        }

        private void FreeAllBuffers()
        {
            foreach (var buffer in _editBuffers.Values)
            {
                buffer.Free();
            }
            _editBuffers.Clear();
            _editedRegions.Clear();
            _boundsNeedUpdate = true;
        }

        #endregion

        #region Bounds Computation

        private void ComputeBoundsFromEditedRegions()
        {
            if (_editedRegions.Count == 0 || _regionSize <= 0)
            {
                // Default small bounds when no edits
                _cachedMinBounds = new Vector2(-32, -32);
                _cachedMaxBounds = new Vector2(32, 32);
                _boundsNeedUpdate = false;
                return;
            }

            _cachedMinBounds = new Vector2(float.MaxValue, float.MaxValue);
            _cachedMaxBounds = new Vector2(float.MinValue, float.MinValue);

            foreach (var regionCoord in _editedRegions)
            {
                Vector2 regionMin = new Vector2(
                    regionCoord.X * _regionSize,
                    regionCoord.Y * _regionSize
                );
                Vector2 regionMax = regionMin + new Vector2(_regionSize, _regionSize);

                _cachedMinBounds.X = Mathf.Min(_cachedMinBounds.X, regionMin.X);
                _cachedMinBounds.Y = Mathf.Min(_cachedMinBounds.Y, regionMin.Y);
                _cachedMaxBounds.X = Mathf.Max(_cachedMaxBounds.X, regionMax.X);
                _cachedMaxBounds.Y = Mathf.Max(_cachedMaxBounds.Y, regionMax.Y);
            }

            _boundsNeedUpdate = false;

            DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.UpdateCycle,
                $"Computed bounds from {_editedRegions.Count} regions: " +
                $"({_cachedMinBounds}) to ({_cachedMaxBounds})");
        }

        /// <summary>
        /// Marks that a region has been edited, triggering bounds update and dirty flag.
        /// </summary>
        public void MarkRegionEdited(Vector2I regionCoords)
        {
            if (_editedRegions.Add(regionCoords))
            {
                _boundsNeedUpdate = true;
            }
            ForceDirty();
        }

        #endregion

        #region GPU Command Generation

        /// <summary>
        /// Creates GPU commands to apply height edits to a region.
        /// Called by ManualEditApplicationPhase.
        /// </summary>
        public (Action<long> commands, List<Rid> tempRids, List<string> shaders) CreateApplyHeightEditCommands(
            Vector2I regionCoords,
            RegionData regionData,
            int regionSize)
        {
            var buffer = GetEditBuffer(regionCoords);
            if (buffer == null || !buffer.HeightDelta.IsValid)
            {
                return (null, new List<Rid>(), new List<string>());
            }

            // TODO: Implement actual GPU commands once shader is written
            // For now, return empty - we'll implement the shader next

            DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.GpuDispatches,
                $"Creating height edit commands for region {regionCoords}");

            return (null, new List<Rid>(), new List<string>());
        }

        /// <summary>
        /// Creates GPU commands to apply texture edits to a region.
        /// Called by ManualEditApplicationPhase.
        /// </summary>
        public (Action<long> commands, List<Rid> tempRids, List<string> shaders) CreateApplyTextureEditCommands(
            Vector2I regionCoords,
            RegionData regionData,
            int regionSize)
        {
            var buffer = GetEditBuffer(regionCoords);
            if (buffer == null || !buffer.TextureEdit.IsValid)
            {
                return (null, new List<Rid>(), new List<string>());
            }

            // TODO: Implement actual GPU commands once shader is written
            // For now, return empty - we'll implement the shader next

            DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.GpuDispatches,
                $"Creating texture edit commands for region {regionCoords}");

            return (null, new List<Rid>(), new List<string>());
        }

        #endregion

        #region Helpers

        private void UpdateModifiesFlags()
        {
            ModifiesHeight = HeightEditingEnabled;
            ModifiesTexture = TextureEditingEnabled;
        }

        /// <summary>
        /// Converts a world position to region coordinates.
        /// </summary>
        public Vector2I WorldToRegionCoords(Vector3 worldPos)
        {
            if (_regionSize <= 0) return Vector2I.Zero;

            return new Vector2I(
                Mathf.FloorToInt(worldPos.X / _regionSize),
                Mathf.FloorToInt(worldPos.Z / _regionSize)
            );
        }

        /// <summary>
        /// Converts a world position to pixel coordinates within a region.
        /// </summary>
        public Vector2I WorldToRegionPixel(Vector3 worldPos, Vector2I regionCoords)
        {
            if (_regionSize <= 0) return Vector2I.Zero;

            float regionMinX = regionCoords.X * _regionSize;
            float regionMinZ = regionCoords.Y * _regionSize;

            return new Vector2I(
                Mathf.Clamp(Mathf.FloorToInt(worldPos.X - regionMinX), 0, _regionSize - 1),
                Mathf.Clamp(Mathf.FloorToInt(worldPos.Z - regionMinZ), 0, _regionSize - 1)
            );
        }

        #endregion
        #region Resource Declaration for Parallel Dispatch

        /// <summary>
        /// Returns all RIDs that manual edit buffers read from for a given region.
        /// Used for parallel dispatch grouping.
        /// </summary>
        public IEnumerable<Rid> GetEditBufferReadSources(Vector2I regionCoords)
        {
            var buffer = GetEditBuffer(regionCoords);
            if (buffer == null || !buffer.HasAllocatedResources)
                yield break;

            if (HeightEditingEnabled && buffer.HeightDelta.IsValid)
                yield return buffer.HeightDelta;

            if (TextureEditingEnabled && buffer.TextureEdit.IsValid)
                yield return buffer.TextureEdit;

            if (InstanceEditingEnabled && buffer.InstanceExclusion.IsValid)
                yield return buffer.InstanceExclusion;
        }

        #endregion
    }

}