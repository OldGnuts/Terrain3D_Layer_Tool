// /Layers/PathLayer.cs
using Godot;
using System;
using System.Linq;
using System.Collections.Generic;
using Terrain3DTools.Core;
using Terrain3DTools.Layers.Path;
using Terrain3DTools.Utils;
using Terrain3DTools.Core.Debug;

namespace Terrain3DTools.Layers
{
    /// <summary>
    /// A terrain feature layer that creates paths, roads, rivers, and similar linear features.
    /// This utilizes an SDF representation of the path to generate its layer mask.
    /// Uses an external Path3D node as the single source of truth for curve data.
    /// Path curve points can be created and edited in either the PathLayer or Path3D node.
    /// Functionality for creating and editing curve points is built into terrain_3d_tools.cs
    /// This class uses an external editor inspector for UI.
    /// This class intentionally hides the mask stack from TerrainLayerBase, 
    /// the pipeline is still in place for possible future enhancements.
    /// Unlike other layers, the path layer fixes itself to world position (0,0,0),
    /// and overrides the GetWorldBounds method to calculate bounds and position based on the path.
    /// </summary>
    [GlobalClass, Tool]
    public partial class PathLayer : FeatureLayer
    {
        #region Constants
        private const string DEBUG_CLASS_NAME = "PathLayer";
        internal const float MIN_SEGMENT_LENGTH = 0.5f;
        private const int CURVE_BAKE_INTERVAL = 1;
        #endregion

        #region Private Fields
        private PathType _pathType = PathType.Trail;
        private PathProfile _profile;
        private Path3D _externalCurveEditor;
        private bool _listeningToCurveEditor = false;

        // Sampling
        private int _resolution = 64;
        private bool _adaptiveResolution = true;
        private float _adaptiveMinAngle = 5.0f;

        // Corner handling
        private float _cornerSmoothing = 1.0f;
        private bool _smoothCorners = true;

        // Curve Smoothing
        private bool _autoSmoothCurve = false;
        private float _autoSmoothTension = 0.4f;
        private bool _isAutoSmoothing = false; // Prevent recursion

        // Caching & Bounds
        private Vector2 _maskWorldMin;
        private Vector2 _maskWorldMax;
        private bool _maskBoundsInitialized = false;

        // Cache Invalidation
        internal Vector3[] _cachedSamplePoints;
        internal float[] _cachedDistances;
        internal float[] _cachedSegmentData;
        internal bool _curveDataDirty = true;
        internal bool _segmentDataDirty = true;

        // GPU Resources
        internal Rid _sdfTextureRid;
        internal Rid _zoneDataTextureRid;

        // Subscriptions
        private bool _profileSubscribed = false;

        // Debug
        private bool _showProfile = false;

        // Thread Safety - Single source of truth for current update cycle
        private PathBakeState _activeBakeState;

        // Grade/Elevation Constraints
        private bool _enableGradeConstraint = false;
        private float _maxGradePercent = 8.0f;
        private bool _enableDownhillConstraint = false;
        private bool _allowSwitchbackGeneration = false;
        private int _maxSwitchbackPoints = 10;

        #endregion

        #region Computed Properties
        public Curve3D Curve => _externalCurveEditor?.Curve;
        public float PathLength => Curve?.GetBakedLength() ?? 0f;
        public int PointCount => Curve?.PointCount ?? 0;
        public float ProfileHalfWidth => _profile?.HalfWidth ?? 2.0f;
        public float ProfileTotalWidth => _profile?.TotalWidth ?? 4.0f;

        public Vector2 MaskWorldMin
        {
            get
            {
                if (!_maskBoundsInitialized) ComputeMaskBounds();
                return _maskWorldMin;
            }
        }

        public Vector2 MaskWorldMax
        {
            get
            {
                if (!_maskBoundsInitialized) ComputeMaskBounds();
                return _maskWorldMax;
            }
        }

        /// <summary>
        /// Returns true if any elevation constraint is enabled and has violations.
        /// </summary>
        public bool HasConstraintViolations
        {
            get
            {
                if (_enableGradeConstraint && GetGradeViolations().Count > 0)
                    return true;
                if (_enableDownhillConstraint && GetDownhillViolations().Count > 0)
                    return true;
                return false;
            }
        }

        /// <summary>
        /// Returns true if this path type uses grade constraints (roads, trails, railways).
        /// </summary>
        public bool UsesGradeConstraint => _pathType == PathType.Road ||
                                            _pathType == PathType.Trail ||
                                            _pathType == PathType.Railway;

        /// <summary>
        /// Returns true if this path type uses downhill constraints (rivers, streams, canals).
        /// </summary>
        public bool UsesDownhillConstraint => _pathType == PathType.River ||
                                               _pathType == PathType.Stream ||
                                               _pathType == PathType.Canal;
        #endregion

        #region Exported Properties
        [ExportGroup("Path Type")]
        [Export]
        public PathType PathType
        {
            get => _pathType;
            set
            {
                if (_pathType != value)
                {
                    _pathType = value;
                    OnPathTypeChanged();
                    NotifyPropertyListChanged();
                }
            }
        }

        [ExportGroup("Path Curve")]
        [Export]
        public Path3D ExternalCurveEditor
        {
            get => _externalCurveEditor;
            set
            {
                if (_externalCurveEditor == value) return;
                DisconnectFromCurveEditor();
                _externalCurveEditor = value;
                if (IsInsideTree()) ConnectToCurveEditor();
                InvalidateCaches();
                MarkPositionDirty();
            }
        }

        [Export(PropertyHint.Range, "8,256,1")]
        public int Resolution
        {
            get => _resolution;
            set { _resolution = Mathf.Clamp(value, 8, 256); InvalidateCaches(); ForceDirty(); }
        }

        [Export]
        public bool AdaptiveResolution
        {
            get => _adaptiveResolution;
            set { _adaptiveResolution = value; InvalidateCaches(); ForceDirty(); }
        }

        [Export(PropertyHint.Range, "1.0,45.0,0.5")]
        public float AdaptiveMinAngle
        {
            get => _adaptiveMinAngle;
            set { _adaptiveMinAngle = Mathf.Clamp(value, 1.0f, 45.0f); InvalidateCaches(); ForceDirty(); }
        }

        [ExportGroup("Profile")]
        [Export]
        public PathProfile Profile
        {
            get => _profile;
            set
            {
                UnsubscribeFromProfile();
                _profile = value;
                SubscribeToProfile();
                _maskBoundsInitialized = false;
                MarkPositionDirty();
            }
        }

        [Export]
        public bool ResetProfileToDefault
        {
            get => false;
            set { if (value) Profile = PathPresets.GetPresetForType(_pathType); }
        }

        [ExportGroup("Smoothing")]
        [Export]
        public bool SmoothCorners
        {
            get => _smoothCorners;
            set { _smoothCorners = value; ForceDirty(); }
        }

        [Export(PropertyHint.Range, "0.0,5.0,0.1")]
        public float CornerSmoothing
        {
            get => _cornerSmoothing;
            set { _cornerSmoothing = Mathf.Max(0, value); ForceDirty(); }
        }

        [Export]
        public bool AutoSmoothCurve
        {
            get => _autoSmoothCurve;
            set
            {
                _autoSmoothCurve = value;
                if (_autoSmoothCurve && Curve != null && Curve.PointCount >= 2)
                {
                    // Apply smoothing immediately when enabled
                    AutoSmoothCurvePoints(_autoSmoothTension);
                }
            }
        }

        [Export(PropertyHint.Range, "0.0,1.0,0.05")]
        public float AutoSmoothTension
        {
            get => _autoSmoothTension;
            set
            {
                _autoSmoothTension = Mathf.Clamp(value, 0f, 1f);
                if (_autoSmoothCurve && Curve != null && Curve.PointCount >= 2)
                {
                    // Re-apply smoothing with new tension
                    AutoSmoothCurvePoints(_autoSmoothTension);
                }
            }
        }

        [ExportGroup("Debug")]
        [Export]
        public bool ShowProfileCrossSection
        {
            get => _showProfile;
            set => _showProfile = value;
        }

        [ExportGroup("Elevation Constraints")]
        [Export]
        public bool EnableGradeConstraint
        {
            get => _enableGradeConstraint;
            set
            {
                if (_enableGradeConstraint != value)
                {
                    _enableGradeConstraint = value;
                    NotifyPropertyListChanged();
                }
            }
        }

        [Export(PropertyHint.Range, "0.5,30.0,0.5")]
        public float MaxGradePercent
        {
            get => _maxGradePercent;
            set => _maxGradePercent = Mathf.Clamp(value, 0.5f, 30.0f);
        }

        [Export]
        public bool EnableDownhillConstraint
        {
            get => _enableDownhillConstraint;
            set
            {
                if (_enableDownhillConstraint != value)
                {
                    _enableDownhillConstraint = value;
                    NotifyPropertyListChanged();
                }
            }
        }

        [Export]
        public bool AllowSwitchbackGeneration
        {
            get => _allowSwitchbackGeneration;
            set
            {
                if (_allowSwitchbackGeneration != value)
                {
                    _allowSwitchbackGeneration = value;
                    NotifyPropertyListChanged();
                }
            }
        }

        [Export(PropertyHint.Range, "2,50,1")]
        public int MaxSwitchbackPoints
        {
            get => _maxSwitchbackPoints;
            set => _maxSwitchbackPoints = Mathf.Clamp(value, 2, 50);
        }

        #endregion

        #region Lifecycle & Notifications
        static PathLayer()
        {
            DebugManager.Instance?.RegisterClass(DEBUG_CLASS_NAME);
        }

        public override void _Ready()
        {
            // Check if this is a newly created layer (not loaded from scene)
            // by checking if essential properties are at their defaults
            bool isNewLayer = string.IsNullOrEmpty(LayerName) ||
                            LayerName.StartsWith("New Layer") ||
                            LayerName.StartsWith("Feature Layer");

            if (isNewLayer)
            {
                // Set sensible defaults for new path layers
                FalloffStrength = 0f;
                FalloffMode = FalloffType.None;
            }
            if (_worldHeightScale < 0.1f) _worldHeightScale = 128.0f;

            base._Ready();

            if (_profile == null) _profile = PathPresets.GetPresetForType(_pathType);

            if (string.IsNullOrEmpty(LayerName) || LayerName.StartsWith("New Layer") || LayerName.StartsWith("Feature Layer"))
            {
                LayerName = $"{PathPresets.GetDisplayName(_pathType)} {IdGenerator.GenerateShortUid()}";
            }

            UpdateModifiesFlags();

            if (Engine.IsEditorHint())
            {
                ResetTransformToOrigin();
                CreatePathVisualizer();
                EnsureCurveEditorExists();
                InvalidateCaches();
                MarkPositionDirty();
            }
        }

        public override void _EnterTree()
        {
            base._EnterTree();
            SubscribeToProfile();
            if (Engine.IsEditorHint())
            {
                CallDeferred(nameof(ConnectToCurveEditor));
                var selection = EditorInterface.Singleton?.GetSelection();
                if (selection != null)
                {
                    selection.SelectionChanged += OnEditorSelectionChanged;
                    CallDeferred(nameof(OnEditorSelectionChanged));
                }
            }
        }

        public override void _ExitTree()
        {
            base._ExitTree();
            DisconnectFromCurveEditor();
            UnsubscribeFromProfile();

            if (Engine.IsEditorHint())
            {
                var selection = EditorInterface.Singleton?.GetSelection();
                if (selection != null) selection.SelectionChanged -= OnEditorSelectionChanged;
            }
        }

        public override void _Notification(int what)
        {
            if (what == NotificationTransformChanged)
            {
                if (Engine.IsEditorHint())
                {
                    if (GlobalPosition != Vector3.Zero || GlobalRotation != Vector3.Zero || Scale != Vector3.One)
                    {
                        CallDeferred(nameof(ResetTransformToOrigin));
                    }
                }
                return;
            }

            base._Notification(what);

            if (what == (int)NotificationPredelete)
            {
                UnsubscribeFromProfile();
                FreeGpuResources();
            }
        }

        public override void _ValidateProperty(Godot.Collections.Dictionary property)
        {
            base._ValidateProperty(property);
            string name = property["name"].AsString();

            if (name == "Masks")
            {
                property["usage"] = (int)PropertyUsageFlags.NoEditor;
            }

            // Hide base falloff properties - we draw them in custom inspector
            if (name == "FalloffStrength" || name == "FalloffCurve" || name == "FalloffMode")
            {
                property["usage"] = (int)PropertyUsageFlags.NoEditor;
            }

            // Show MaxGradePercent only when grade constraint is enabled
            if (name == "MaxGradePercent" && !_enableGradeConstraint)
            {
                property["usage"] = (int)PropertyUsageFlags.NoEditor;
            }

            // Hide grade constraint for path types that don't use it
            if (name == "EnableGradeConstraint" && !UsesGradeConstraint && !_enableGradeConstraint)
            {
                // Still show if manually enabled, but hide for irrelevant types by default
                property["usage"] = (int)PropertyUsageFlags.NoEditor;
            }

            // Hide downhill constraint for path types that don't use it
            if (name == "EnableDownhillConstraint" && !UsesDownhillConstraint && !_enableDownhillConstraint)
            {
                property["usage"] = (int)PropertyUsageFlags.NoEditor;
            }

            if (name == "MaxSwitchbackPoints" && !_allowSwitchbackGeneration)
            {
                property["usage"] = (int)PropertyUsageFlags.NoEditor;
            }

            if (name == "AllowSwitchbackGeneration" && !_enableGradeConstraint)
            {
                property["usage"] = (int)PropertyUsageFlags.NoEditor;
            }
        }

        public override void _Process(double delta)
        {
            base._Process(delta);
            if (Engine.IsEditorHint() && _curveDataDirty && _pathVisualizerMesh?.Visible == true)
            {
                UpdatePathVisualization();
            }
        }

        private void ResetTransformToOrigin()
        {
            GlobalPosition = Vector3.Zero;
            GlobalRotation = Vector3.Zero;
            Scale = Vector3.One;
            if (_externalCurveEditor != null && IsInstanceValid(_externalCurveEditor))
            {
                _externalCurveEditor.Transform = Transform3D.Identity;
            }
        }

        private void OnEditorSelectionChanged()
        {
            if (!Engine.IsEditorHint()) return;
            var selection = EditorInterface.Singleton?.GetSelection();
            if (selection == null) return;
            bool isSelected = selection.GetSelectedNodes().Contains(this);
            SetPathVisualizerVisible(isSelected);
        }
        #endregion

        #region Event Handlers (State Sync)
        private void OnPathTypeChanged()
        {
            UnsubscribeFromProfile();
            _profile = PathPresets.GetPresetForType(_pathType);
            SubscribeToProfile();

            if (LayerName.Contains("Layer ") || LayerName.Contains("New Layer"))
            {
                LayerName = $"{PathPresets.GetDisplayName(_pathType)} {IdGenerator.GenerateShortUid()}";
            }

            // Apply default constraint settings for this path type
            _enableGradeConstraint = PathPresets.GetDefaultGradeConstraintEnabled(_pathType);
            _maxGradePercent = PathPresets.GetDefaultMaxGrade(_pathType);
            _enableDownhillConstraint = PathPresets.GetDefaultDownhillConstraintEnabled(_pathType);

            // Set sensible max grade if not defined for this type
            if (_maxGradePercent <= 0f && _enableGradeConstraint)
            {
                _maxGradePercent = 8.0f; // Fallback default
            }

            UpdateModifiesFlags();
            _maskBoundsInitialized = false;

            DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.LayerLifecycle,
                $"Type changed to {_pathType}, applied preset profile. " +
                $"GradeConstraint: {_enableGradeConstraint} ({_maxGradePercent}%), " +
                $"DownhillConstraint: {_enableDownhillConstraint}");

            MarkPositionDirty();
        }

        private void SubscribeToProfile()
        {
            if (_profileSubscribed || _profile == null || !IsInsideTree()) return;
            _profile.Changed += OnProfileChanged;
            _profileSubscribed = true;
        }

        private void UnsubscribeFromProfile()
        {
            if (!_profileSubscribed || _profile == null) return;
            _profile.Changed -= OnProfileChanged;
            _profileSubscribed = false;
        }

        private void OnProfileChanged()
        {
            DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.UpdateCycle,
                $"OnProfileChanged called at frame {Engine.GetProcessFrames()}, " +
                $"IsDirty before: {IsDirty}, PositionDirty before: {PositionDirty}");

            if (_profile != null)
            {
                var zones = _profile.GetEnabledZones().ToList();
                DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.UpdateCycle,
                    $"OnProfileChanged at frame {Engine.GetProcessFrames()} - Zone values:");
                for (int i = 0; i < zones.Count; i++)
                {
                    var zone = zones[i];
                    DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.UpdateCycle,
                        $"  Zone[{i}]: Width={zone.Width:F2}, HeightOffset={zone.HeightOffset:F2}");
                }
            }

            UpdateModifiesFlags();

            // Recompute bounds based on new profile width
            ComputeBoundsFromCurve(Curve, out Vector2 minWorld, out Vector2 maxWorld);
            _maskWorldMin = minWorld;
            _maskWorldMax = maxWorld;
            _maskBoundsInitialized = true;

            Vector2 sizeF = maxWorld - minWorld;
            Vector2I newSize = new Vector2I(
                Mathf.Max(64, Mathf.CeilToInt(sizeF.X)),
                Mathf.Max(64, Mathf.CeilToInt(sizeF.Y))
            );

            if (Size != newSize)
            {
                Size = newSize;
            }

            InvalidateCaches();

            DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.LayerLifecycle,
                $"Profile changed: Bounds updated to ({_maskWorldMin})-({_maskWorldMax}), Size={Size}");

            MarkPositionDirty();

            DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.UpdateCycle,
                $"OnProfileChanged complete - IsDirty: {IsDirty}, PositionDirty: {PositionDirty}");


        }

        private void UpdateModifiesFlags()
        {
            if (_profile == null)
            {
                ModifiesHeight = true;
                ModifiesTexture = false;
                return;
            }

            var enabledZones = _profile.GetEnabledZones();
            ModifiesHeight = enabledZones.Any(z => z.HeightStrength > 0.001f);
            ModifiesTexture = enabledZones.Any(z => z.TextureId >= 0 && z.TextureStrength > 0.001f);
        }

        public void InvalidateCaches()
        {
            _curveDataDirty = true;
            _segmentDataDirty = true;
            _cachedSamplePoints = null;
            _cachedSegmentData = null;
        }
        #endregion

        #region GPU Resource Management
        public override void PrepareMaskResources(bool isInteractive)
        {
            DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.UpdateCycle,
                $"PrepareMaskResources called at frame {Engine.GetProcessFrames()}");

            // 1. Determine if we need new textures by comparing live bounds against last captured state
            bool needsNewTextures = SizeHasChanged() ||
                                    !_sdfTextureRid.IsValid ||
                                    !_zoneDataTextureRid.IsValid ||
                                    !layerTextureRID.IsValid;

            DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.UpdateCycle,
                $"PrepareMaskResources - needsNewTextures: {needsNewTextures}, SizeHasChanged: {SizeHasChanged()}");

            // 2. Allocate/recreate textures if needed
            if (needsNewTextures)
            {
                int width = Size.X;
                int height = Size.Y;

                DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.UpdateCycle,
                    $"PrepareMaskResources - Recreating textures at size {width}x{height}");

                // Refresh Base Textures (Safe Management via graveyard)
                layerTextureRID = RefreshGpuTexture(layerTextureRID, width, height, RenderingDevice.DataFormat.R32Sfloat);
                layerHeightVisualizationTextureRID = RefreshGpuTexture(layerHeightVisualizationTextureRID, width, height, RenderingDevice.DataFormat.R32Sfloat);

                // Refresh Path-Specific Textures
                _sdfTextureRid = RefreshGpuTexture(_sdfTextureRid, width, height, RenderingDevice.DataFormat.R32Sfloat);
                _zoneDataTextureRid = RefreshGpuTexture(_zoneDataTextureRid, width, height, RenderingDevice.DataFormat.R32G32B32A32Sfloat);
            }

            // 3. Capture state AFTER resources are ready - this is the single source of truth for this cycle
            _activeBakeState = CaptureBakeState();
        }

        private Rid RefreshGpuTexture(Rid existingRid, int width, int height, RenderingDevice.DataFormat format)
        {
            Rid newRid = Gpu.CreateTexture2D(
                (uint)width, (uint)height,
                format,
                RenderingDevice.TextureUsageBits.StorageBit |
                RenderingDevice.TextureUsageBits.SamplingBit |
                RenderingDevice.TextureUsageBits.CanCopyFromBit
            );

            if (newRid.IsValid && existingRid.IsValid)
            {
                // Safe disposal via centralized manager
                QueueRidForDisposal(existingRid);
            }

            return newRid.IsValid ? newRid : existingRid;
        }

        /// <summary>
        /// Uses resource cleanup from the AsyncGpuTaskManager for deferred rid disposal.
        /// </summary>
        private void QueueRidForDisposal(Rid rid)
        {
            if (!rid.IsValid) return;

            DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.UpdateCycle,
                $"QueueRidForDisposal - RID: {rid}, Manager available: {AsyncGpuTaskManager.Instance != null}");

            if (AsyncGpuTaskManager.Instance != null)
            {
                AsyncGpuTaskManager.Instance.QueueCleanup(rid);
            }
            else
            {
                DebugManager.Instance?.LogWarning(DEBUG_CLASS_NAME,
                    $"IMMEDIATE FREE of {rid} - AsyncGpuTaskManager not available!");
                Gpu.FreeRid(rid);
            }
        }

        private void FreeGpuResources()
        {
            if (_sdfTextureRid.IsValid) { Gpu.FreeRid(_sdfTextureRid); _sdfTextureRid = new Rid(); }
            if (_zoneDataTextureRid.IsValid) { Gpu.FreeRid(_zoneDataTextureRid); _zoneDataTextureRid = new Rid(); }
            // Base class RIDs
            if (layerTextureRID.IsValid) { Gpu.FreeRid(layerTextureRID); layerTextureRID = new Rid(); }
            if (layerHeightVisualizationTextureRID.IsValid) { Gpu.FreeRid(layerHeightVisualizationTextureRID); layerHeightVisualizationTextureRID = new Rid(); }
        }
        #endregion

        #region Base Overrides and Implementations
        public override LayerType GetLayerType() => LayerType.Feature;
        public override string LayerTypeName() => $"{PathPresets.GetDisplayName(_pathType)} Path";
        public override (Vector2 Min, Vector2 Max) GetWorldBounds() => (MaskWorldMin, MaskWorldMax);

        /// <summary>
        /// Compares current live bounds against the last captured bake state.
        /// Returns true if textures need to be recreated.
        /// </summary>
        public override bool SizeHasChanged()
        {
            // If we've never captured state, we definitely need to
            if (_activeBakeState == null)
            {
                return true;
            }

            // Compare live values against what we last prepared for
            bool boundsChanged = _maskWorldMin != _activeBakeState.MaskWorldMin ||
                                 _maskWorldMax != _activeBakeState.MaskWorldMax;
            bool sizeChanged = Size != _activeBakeState.Size;

            if (boundsChanged || sizeChanged)
            {
                DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.UpdateCycle,
                    $"SizeHasChanged: true - Live({_maskWorldMin}, {_maskWorldMax}, {Size}) vs " +
                    $"State({_activeBakeState.MaskWorldMin}, {_activeBakeState.MaskWorldMax}, {_activeBakeState.Size})");
            }

            return boundsChanged || sizeChanged;
        }

        /// <summary>
        /// Returns all RIDs this layer writes to during mask generation.
        /// Includes SDF and zone data textures in addition to base layer textures.
        /// </summary>
        public override IEnumerable<Rid> GetMaskWriteTargets()
        {
            foreach (var rid in base.GetMaskWriteTargets())
                yield return rid;

            if (_sdfTextureRid.IsValid)
                yield return _sdfTextureRid;

            if (_zoneDataTextureRid.IsValid)
                yield return _zoneDataTextureRid;
        }
        #endregion
    }
}