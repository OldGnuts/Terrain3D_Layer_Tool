// /Layers/Path/PathLayer.cs
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using Terrain3DTools.Core;
using Terrain3DTools.Layers.Path;
using Terrain3DTools.Utils;
using TokisanGames;
using Terrain3DTools.Core.Debug;

namespace Terrain3DTools.Layers
{
    /// <summary>
    /// A terrain feature layer that creates paths, roads, rivers, and similar linear features.
    /// Uses an embedded Curve3D for path editing directly on the layer node.
    /// Supports profile-based cross-sections with multiple zones, each with independent
    /// height, texture, and noise settings.
    /// </summary>
    [GlobalClass, Tool]
    public partial class PathLayer : FeatureLayer
    {

        #region Constants
        private const string DEBUG_CLASS_NAME = "PathLayer";
        private const int MAX_ZONES = 8;
        private const int MAX_CURVE_POINTS = 256;
        private const float MIN_SEGMENT_LENGTH = 0.5f;
        #endregion

        #region Private Fields
        // Core path data
        private PathType _pathType = PathType.Trail;
        private Curve3D _curve;
        private PathProfile _profile;

        // Sampling settings
        private int _resolution = 64;
        private bool _adaptiveResolution = true;
        private float _adaptiveMinAngle = 5.0f; // degrees

        // Corner handling
        private float _cornerSmoothing = 1.0f;
        private bool _smoothCorners = true;

        // Debug
        private bool _showProfile = false;

        // Cached data
        private Vector3[] _cachedSamplePoints;
        private float[] _cachedDistances;
        private float[] _cachedSegmentData;
        private bool _curveDataDirty = true;
        private bool _segmentDataDirty = true;
        private Vector2I _lastSegmentDataSize;        // Track size used for last segment generation

        // GPU Resources
        private Rid _sdfTextureRid;
        private Rid _zoneDataTextureRid;

        // Subscriptions
        private bool _curveSubscribed = false;
        private bool _profileSubscribed = false;

        // Interactive path editing
        private int _hoveredPointIndex = -1;

        #endregion

        /// <summary>
        /// Index of the currently hovered point (for visual feedback). -1 if none.
        /// </summary>
        public int HoveredPointIndex
        {
            get => _hoveredPointIndex;
            set
            {
                if (_hoveredPointIndex != value)
                {
                    _hoveredPointIndex = value;
                    if (Engine.IsEditorHint())
                    {
                        UpdatePathVisualization();
                    }
                }
            }
        }

        #region Constructor 
        // For registering with DebugManager
        static PathLayer()
        {
            DebugManager.Instance?.RegisterClass(DEBUG_CLASS_NAME);
        }

        #endregion


        #region Exported Properties - Path Type
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
                    ForceDirty();
                }
            }
        }
        #endregion

        #region Exported Properties - Curve
        [ExportGroup("Path Curve")]

        /// <summary>
        /// The embedded curve defining the path shape. Edit directly in viewport.
        /// </summary>
        [Export]
        public Curve3D Curve
        {
            get => _curve;
            set
            {
                // Unsubscribe from old curve if we're in the tree
                if (_curveSubscribed && _curve != null)
                {
                    _curve.Changed -= OnCurveChanged;
                    _curveSubscribed = false;
                }

                _curve = value;

                // Only subscribe if we're in the tree (avoids double-subscription during deserialization)
                if (IsInsideTree() && _curve != null)
                {
                    _curve.Changed += OnCurveChanged;
                    _curveSubscribed = true;
                }

                _curveDataDirty = true;
                ForceDirty();
            }
        }

        [Export(PropertyHint.Range, "8,256,1")]
        public int Resolution
        {
            get => _resolution;
            set
            {
                _resolution = Mathf.Clamp(value, 8, 256);
                _curveDataDirty = true;
                ForceDirty();
            }
        }

        [Export]
        public bool AdaptiveResolution
        {
            get => _adaptiveResolution;
            set
            {
                _adaptiveResolution = value;
                _curveDataDirty = true;
                ForceDirty();
            }
        }

        [Export(PropertyHint.Range, "1.0,45.0,0.5")]
        public float AdaptiveMinAngle
        {
            get => _adaptiveMinAngle;
            set
            {
                _adaptiveMinAngle = Mathf.Clamp(value, 1.0f, 45.0f);
                _curveDataDirty = true;
                ForceDirty();
            }
        }
        #endregion

        #region Exported Properties - Profile
        [ExportGroup("Profile")]

        /// <summary>
        /// The cross-section profile defining zones, heights, and textures.
        /// </summary>
        [Export]
        public PathProfile Profile
        {
            get => _profile;
            set
            {
                // Unsubscribe from old profile if we're in the tree
                if (_profileSubscribed && _profile != null)
                {
                    _profile.Changed -= OnProfileChanged;
                    _profileSubscribed = false;
                }

                _profile = value;

                // Only subscribe if we're in the tree
                if (IsInsideTree() && _profile != null)
                {
                    _profile.Changed += OnProfileChanged;
                    _profileSubscribed = true;
                }

                ForceDirty();
            }
        }

        /// <summary>
        /// Button to reset profile to current path type's default.
        /// </summary>
        [Export]
        public bool ResetProfileToDefault
        {
            get => false;
            set
            {
                if (value)
                {
                    Profile = PathPresets.GetPresetForType(_pathType);
                }
            }
        }
        #endregion

        #region Exported Properties - Smoothing
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
        #endregion

        #region Exported Properties - Debug
        [ExportGroup("Debug")]

        [Export]
        public bool ShowProfileCrossSection
        {
            get => _showProfile;
            set => _showProfile = value;
        }

        #endregion

        #region Computed Properties
        /// <summary>
        /// Total path length from curve.
        /// </summary>
        public float PathLength => _curve?.GetBakedLength() ?? 0f;

        /// <summary>
        /// Number of control points in curve.
        /// </summary>
        public int PointCount => _curve?.PointCount ?? 0;

        /// <summary>
        /// Half-width of the path profile.
        /// </summary>
        public float ProfileHalfWidth => _profile?.HalfWidth ?? 2.0f;

        /// <summary>
        /// Full width of the path including all zones.
        /// </summary>
        public float ProfileTotalWidth => _profile?.TotalWidth ?? 4.0f;
        #endregion


        #region Viewport Visualization
        private MeshInstance3D _pathVisualizerMesh;
        private ImmediateMesh _immediateMesh;

        private void CreatePathVisualizer()
        {
            if (_pathVisualizerMesh != null) return;

            _immediateMesh = new ImmediateMesh();

            _pathVisualizerMesh = new MeshInstance3D
            {
                Mesh = _immediateMesh,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                Visible = false  // Start hidden, selection will show it
            };

            var material = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                VertexColorUseAsAlbedo = true,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                NoDepthTest = true,
                RenderPriority = 10
            };
            _pathVisualizerMesh.MaterialOverride = material;

            AddChild(_pathVisualizerMesh);
        }

        private void SetPathVisualizerVisible(bool visible)
        {
            if (_pathVisualizerMesh != null)
            {
                _pathVisualizerMesh.Visible = visible;

                if (visible)
                {
                    UpdatePathVisualization();
                }
            }
        }

        private void UpdatePathVisualization()
        {
            if (!Engine.IsEditorHint()) return;
            if (_immediateMesh == null) return;

            _immediateMesh.ClearSurfaces();

            if (_curve == null || _curve.PointCount == 0)
            {
                return;
            }

            float totalLength = _curve.GetBakedLength();
            if (totalLength <= 0) return;

            // Draw everything in batched surfaces to avoid surface limit
            DrawZoneWidthIndicatorsBatched(totalLength);
            DrawPathCenterlineBatched(totalLength);
            DrawControlPointMarkersBatched();
        }

        private void DrawZoneWidthIndicatorsBatched(float totalLength)
        {
            if (_profile?.Zones == null || _profile.Zones.Count == 0) return;

            int widthSamples = Mathf.Max(8, Mathf.Min(32, (int)(totalLength / 4.0f)));

            // Calculate zone boundaries
            float[] zoneBoundaries = new float[_profile.Zones.Count];
            float accumulated = 0f;
            for (int z = 0; z < _profile.Zones.Count; z++)
            {
                var zone = _profile.Zones[z];
                if (zone != null && zone.Enabled)
                    accumulated += zone.Width;
                zoneBoundaries[z] = accumulated;
            }

            // Draw all zone lines in ONE surface per zone (batched)
            for (int z = 0; z < _profile.Zones.Count; z++)
            {
                var zone = _profile.Zones[z];
                if (zone == null || !zone.Enabled) continue;

                float innerDist = z == 0 ? 0f : zoneBoundaries[z - 1];
                float outerDist = zoneBoundaries[z];
                Color zoneColor = GetZoneDisplayColor(zone.Type, z);

                // Start ONE surface for all lines of this zone
                _immediateMesh.SurfaceBegin(Mesh.PrimitiveType.Lines);
                _immediateMesh.SurfaceSetColor(zoneColor);

                for (int i = 0; i <= widthSamples; i++)
                {
                    float t = (float)i / widthSamples;
                    float dist = t * totalLength;

                    Vector3 localPoint = _curve.SampleBaked(dist);
                    Vector3 worldPoint = ToGlobal(localPoint);

                    float terrainHeight = QueryTerrainHeight(worldPoint);
                    worldPoint.Y = terrainHeight + 0.15f;

                    Vector3 tangent = GetTangentAtDistance(dist);
                    Vector3 perpendicular = tangent.Cross(Vector3.Up).Normalized();
                    if (perpendicular.LengthSquared() < 0.01f)
                        perpendicular = tangent.Cross(Vector3.Forward).Normalized();

                    Vector3 innerLeft = worldPoint - perpendicular * innerDist;
                    Vector3 outerLeft = worldPoint - perpendicular * outerDist;
                    Vector3 innerRight = worldPoint + perpendicular * innerDist;
                    Vector3 outerRight = worldPoint + perpendicular * outerDist;

                    // Left side segment (2 vertices per line)
                    _immediateMesh.SurfaceAddVertex(ToLocal(innerLeft));
                    _immediateMesh.SurfaceAddVertex(ToLocal(outerLeft));

                    // Right side segment (2 vertices per line)
                    _immediateMesh.SurfaceAddVertex(ToLocal(innerRight));
                    _immediateMesh.SurfaceAddVertex(ToLocal(outerRight));
                }

                _immediateMesh.SurfaceEnd();
            }
        }

        private void DrawPathCenterlineBatched(float totalLength)
        {
            int samples = Mathf.Max(16, Mathf.Min(64, (int)(totalLength / 2.0f)));

            var pathColor = new Color(1.0f, 0.9f, 0.3f, 0.95f); // Bright yellow

            // Single surface for the entire centerline
            _immediateMesh.SurfaceBegin(Mesh.PrimitiveType.LineStrip);
            _immediateMesh.SurfaceSetColor(pathColor);

            for (int i = 0; i <= samples; i++)
            {
                float t = (float)i / samples;
                float dist = t * totalLength;

                Vector3 localPoint = _curve.SampleBaked(dist);
                Vector3 worldPoint = ToGlobal(localPoint);

                float terrainHeight = QueryTerrainHeight(worldPoint);
                worldPoint.Y = terrainHeight + 0.2f;

                _immediateMesh.SurfaceAddVertex(ToLocal(worldPoint));
            }

            _immediateMesh.SurfaceEnd();
        }

        private void DrawControlPointMarkersBatched()
        {
            if (_curve.PointCount == 0) return;

            float markerSize = 0.5f;
            float hoveredMarkerSize = 0.7f;

            _immediateMesh.SurfaceBegin(Mesh.PrimitiveType.Lines);

            for (int i = 0; i < _curve.PointCount; i++)
            {
                Vector3 localPos = _curve.GetPointPosition(i);
                Vector3 worldPos = ToGlobal(localPos);

                float terrainHeight = QueryTerrainHeight(worldPos);
                worldPos.Y = terrainHeight + 0.25f;

                Vector3 center = ToLocal(worldPos);

                bool isHovered = (i == _hoveredPointIndex);
                float size = isHovered ? hoveredMarkerSize : markerSize;

                // Color based on position in path
                Color markerColor;
                if (i == 0)
                    markerColor = new Color(0.2f, 1.0f, 0.3f, 1.0f);
                else if (i == _curve.PointCount - 1)
                    markerColor = new Color(1.0f, 0.3f, 0.2f, 1.0f);
                else
                    markerColor = new Color(1.0f, 0.8f, 0.2f, 1.0f);

                // Brighten if hovered
                if (isHovered)
                {
                    markerColor = markerColor.Lightened(0.3f);
                }

                _immediateMesh.SurfaceSetColor(markerColor);

                // Diamond marker vertices
                Vector3 north = center + new Vector3(0, 0, -size);
                Vector3 south = center + new Vector3(0, 0, size);
                Vector3 east = center + new Vector3(size, 0, 0);
                Vector3 west = center + new Vector3(-size, 0, 0);
                Vector3 top = center + new Vector3(0, size * 0.6f, 0);

                // Base diamond
                _immediateMesh.SurfaceAddVertex(north);
                _immediateMesh.SurfaceAddVertex(east);
                _immediateMesh.SurfaceAddVertex(east);
                _immediateMesh.SurfaceAddVertex(south);
                _immediateMesh.SurfaceAddVertex(south);
                _immediateMesh.SurfaceAddVertex(west);
                _immediateMesh.SurfaceAddVertex(west);
                _immediateMesh.SurfaceAddVertex(north);

                // Lines to top
                _immediateMesh.SurfaceAddVertex(north);
                _immediateMesh.SurfaceAddVertex(top);
                _immediateMesh.SurfaceAddVertex(east);
                _immediateMesh.SurfaceAddVertex(top);
                _immediateMesh.SurfaceAddVertex(south);
                _immediateMesh.SurfaceAddVertex(top);
                _immediateMesh.SurfaceAddVertex(west);
                _immediateMesh.SurfaceAddVertex(top);

                // Vertical drop line
                Color dropColor = markerColor with { A = 0.5f };
                _immediateMesh.SurfaceSetColor(dropColor);
                _immediateMesh.SurfaceAddVertex(center);
                _immediateMesh.SurfaceAddVertex(center - new Vector3(0, 2.0f, 0));

                // Draw selection ring if hovered
                if (isHovered)
                {
                    Color ringColor = Colors.White with { A = 0.8f };
                    _immediateMesh.SurfaceSetColor(ringColor);

                    float ringSize = size * 1.3f;
                    int segments = 12;
                    for (int s = 0; s < segments; s++)
                    {
                        float angle1 = (float)s / segments * Mathf.Tau;
                        float angle2 = (float)(s + 1) / segments * Mathf.Tau;

                        Vector3 p1 = center + new Vector3(Mathf.Cos(angle1) * ringSize, 0, Mathf.Sin(angle1) * ringSize);
                        Vector3 p2 = center + new Vector3(Mathf.Cos(angle2) * ringSize, 0, Mathf.Sin(angle2) * ringSize);

                        _immediateMesh.SurfaceAddVertex(p1);
                        _immediateMesh.SurfaceAddVertex(p2);
                    }
                }
            }

            _immediateMesh.SurfaceEnd();
        }

        private Color GetZoneDisplayColor(ZoneType type, int zoneIndex)
        {
            return type switch
            {
                ZoneType.Center => new Color(0.3f, 0.7f, 1.0f, 0.9f),
                ZoneType.Inner => new Color(0.4f, 0.6f, 0.95f, 0.85f),
                ZoneType.Shoulder => new Color(0.3f, 0.9f, 0.4f, 0.85f),
                ZoneType.Edge => new Color(0.7f, 0.9f, 0.3f, 0.75f),
                ZoneType.Wall => new Color(1.0f, 0.5f, 0.2f, 0.85f),
                ZoneType.Rim => new Color(1.0f, 0.8f, 0.2f, 0.85f),
                ZoneType.Slope => new Color(0.75f, 0.55f, 0.35f, 0.8f),
                ZoneType.Transition => new Color(0.6f, 0.6f, 0.6f, 0.5f),
                _ => new Color(0.5f, 0.5f, 0.5f, 0.7f)
            };
        }

        private float QueryTerrainHeight(Vector3 worldPos)
        {
            try
            {
                var manager = TerrainHeightQuery.GetTerrainLayerManager();
                if (manager?.Terrain3DNode != null)
                {
                    var terrain = Terrain3D.Bind(manager.Terrain3DNode);
                    if (terrain?.Data != null)
                    {
                        float height = (float)terrain.Data.GetHeight(worldPos);
                        if (!float.IsNaN(height))
                            return height;
                    }
                }
            }
            catch
            {
                // Silently fail
            }

            return worldPos.Y;
        }
        #endregion

        #region Godot Lifecycle
        public override void _Ready()
        {
            base._Ready();

            // Initialize curve if not set
            if (_curve == null)
            {
                _curve = new Curve3D();
                _curve.BakeInterval = 1.0f;
            }

            // Initialize profile if not set
            if (_profile == null)
            {
                _profile = PathPresets.GetPresetForType(_pathType);
            }

            // Set layer name
            if (string.IsNullOrEmpty(LayerName) || LayerName.StartsWith("New Layer"))
            {
                LayerName = $"{PathPresets.GetDisplayName(_pathType)} {IdGenerator.GenerateShortUid()}";
            }

            UpdateModifiesFlags();

            if (Engine.IsEditorHint())
            {
                CreatePathVisualizer();
            }
        }

        public override void _EnterTree()
        {
            base._EnterTree();

            // Subscribe to curve/profile events
            if (_curve != null && !_curveSubscribed)
            {
                _curve.Changed += OnCurveChanged;
                _curveSubscribed = true;
            }

            if (_profile != null && !_profileSubscribed)
            {
                _profile.Changed += OnProfileChanged;
                _profileSubscribed = true;
            }

            // Subscribe to editor selection changes
            if (Engine.IsEditorHint())
            {
                var selection = EditorInterface.Singleton?.GetSelection();
                if (selection != null)
                {
                    selection.SelectionChanged += OnEditorSelectionChanged;
                    // Check initial selection state
                    CallDeferred(nameof(OnEditorSelectionChanged));
                }
            }
        }

        public override void _ExitTree()
        {
            base._ExitTree();

            // Unsubscribe from curve/profile events
            if (_curve != null && _curveSubscribed)
            {
                _curve.Changed -= OnCurveChanged;
                _curveSubscribed = false;
            }

            if (_profile != null && _profileSubscribed)
            {
                _profile.Changed -= OnProfileChanged;
                _profileSubscribed = false;
            }

            // Unsubscribe from editor selection
            if (Engine.IsEditorHint())
            {
                var selection = EditorInterface.Singleton?.GetSelection();
                if (selection != null)
                {
                    selection.SelectionChanged -= OnEditorSelectionChanged;
                }
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

        public override void _Notification(int what)
        {
            base._Notification(what);

            if (what == (int)NotificationPredelete)
            {
                // Final cleanup
                if (_curve != null && _curveSubscribed)
                {
                    _curve.Changed -= OnCurveChanged;
                    _curveSubscribed = false;
                }

                if (_profile != null && _profileSubscribed)
                {
                    _profile.Changed -= OnProfileChanged;
                    _profileSubscribed = false;
                }

                FreeGpuResources();
            }
        }

        public override void _Process(double delta)
        {
            base._Process(delta);

            // Only update visualization if curve data changed and visualizer is visible
            if (Engine.IsEditorHint() && _curveDataDirty && _pathVisualizerMesh?.Visible == true)
            {
                UpdatePathVisualization();
            }
        }
        #endregion

        #region Property Visibility (Context-Aware UI)
        public override void _ValidateProperty(Godot.Collections.Dictionary property)
        {
            string name = property["name"].AsString();

            // Hide properties that don't apply to current path type
            // This creates a cleaner, context-aware interface

            bool shouldHide = false;

            // River/Stream specific - hide road-specific properties
            if (_pathType == PathType.River || _pathType == PathType.Stream)
            {
                // These would be road-specific if we had them at top level
                // For now, profile handles everything
            }

            // Road specific - hide water-specific properties
            if (_pathType == PathType.Road)
            {
                // Water properties would be hidden here
            }

            // Custom shows everything
            if (_pathType == PathType.Custom)
            {
                shouldHide = false;
            }

            if (shouldHide)
            {
                property["usage"] = (int)(property["usage"].AsInt64() | (long)PropertyUsageFlags.ReadOnly);
            }
        }
        #endregion

        #region Event Handlers
        private void OnPathTypeChanged()
        {
            // Unsubscribe from old profile
            if (_profile != null && _profileSubscribed)
            {
                _profile.Changed -= OnProfileChanged;
                _profileSubscribed = false;
            }

            // Apply new preset profile
            _profile = PathPresets.GetPresetForType(_pathType);

            // Subscribe to new profile if in tree
            if (IsInsideTree() && _profile != null)
            {
                _profile.Changed += OnProfileChanged;
                _profileSubscribed = true;
            }

            // Update layer name if it's still default
            if (LayerName.Contains("Layer ") || LayerName.Contains("New Layer"))
            {
                LayerName = $"{PathPresets.GetDisplayName(_pathType)} {IdGenerator.GenerateShortUid()}";
            }

            UpdateModifiesFlags();

            DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.LayerLifecycle,
                $"Type changed to {_pathType}, applied preset profile");
        }

        private void OnCurveChanged()
        {
            _curveDataDirty = true;
            _segmentDataDirty = true;
            AutoResizeToCurve();
            ForceDirty();


            // Update viewport visualization
            if (Engine.IsEditorHint())
            {
                UpdatePathVisualization();
            }
        }

        private void OnProfileChanged()
        {
            UpdateModifiesFlags();
            ForceDirty();
        }

        private void UpdateModifiesFlags()
        {
            if (_profile == null)
            {
                ModifiesHeight = true;
                ModifiesTexture = false;
                return;
            }

            // Check if any zone modifies height
            ModifiesHeight = _profile.Zones.Any(z =>
                z != null && z.Enabled &&
                z.HeightStrength > 0.001f);

            // Check if any zone modifies texture
            ModifiesTexture = _profile.Zones.Any(z =>
                z != null && z.Enabled &&
                z.TextureId >= 0 && z.TextureStrength > 0.001f);
        }
        #endregion

        #region Curve Management
        /// <summary>
        /// Add a point to the curve at world position.
        /// </summary>
        public void AddPoint(Vector3 worldPosition)
        {
            if (_curve == null) return;

            Vector3 localPos = ToLocal(worldPosition);
            _curve.AddPoint(localPos);
            _curveDataDirty = true;
        }

        /// <summary>
        /// Insert a point at a specific index.
        /// </summary>
        public void InsertPoint(int index, Vector3 worldPosition)
        {
            if (_curve == null) return;

            Vector3 localPos = ToLocal(worldPosition);

            if (index >= _curve.PointCount)
            {
                _curve.AddPoint(localPos);
            }
            else
            {
                // Curve3D doesn't have InsertPoint, so we need to rebuild
                var points = new List<Vector3>();
                var inHandles = new List<Vector3>();
                var outHandles = new List<Vector3>();

                for (int i = 0; i < _curve.PointCount; i++)
                {
                    if (i == index)
                    {
                        points.Add(localPos);
                        inHandles.Add(Vector3.Zero);
                        outHandles.Add(Vector3.Zero);
                    }
                    points.Add(_curve.GetPointPosition(i));
                    inHandles.Add(_curve.GetPointIn(i));
                    outHandles.Add(_curve.GetPointOut(i));
                }

                _curve.ClearPoints();
                for (int i = 0; i < points.Count; i++)
                {
                    _curve.AddPoint(points[i], inHandles[i], outHandles[i]);
                }
            }

            _curveDataDirty = true;
        }

        /// <summary>
        /// Remove a point by index.
        /// </summary>
        public void RemovePoint(int index)
        {
            if (_curve == null || index < 0 || index >= _curve.PointCount) return;

            _curve.RemovePoint(index);
            _curveDataDirty = true;
        }

        /// <summary>
        /// Clear all points from the curve.
        /// </summary>
        public void ClearPoints()
        {
            _curve?.ClearPoints();
            _curveDataDirty = true;
        }

        /// <summary>
        /// Get world position of a curve point.
        /// </summary>
        public Vector3 GetPointWorldPosition(int index)
        {
            if (_curve == null || index < 0 || index >= _curve.PointCount)
                return GlobalPosition;

            return ToGlobal(_curve.GetPointPosition(index));
        }

        /// <summary>
        /// Set world position of a curve point.
        /// </summary>
        public void SetPointWorldPosition(int index, Vector3 worldPosition)
        {
            if (_curve == null || index < 0 || index >= _curve.PointCount) return;

            _curve.SetPointPosition(index, ToLocal(worldPosition));
            _curveDataDirty = true;
        }

        /// <summary>
        /// Auto-resize the layer Size to encompass the curve with margin.
        /// </summary>
        public void AutoResizeToCurve()
        {
            if (_curve == null || _curve.PointCount == 0) return;

            // Find bounds of all curve points
            Vector2 minBounds = new Vector2(float.MaxValue, float.MaxValue);
            Vector2 maxBounds = new Vector2(float.MinValue, float.MinValue);

            float bakedLength = _curve.GetBakedLength();
            if (bakedLength <= 0) return;

            // Use control points + a few samples instead of many samples along curve
            for (int i = 0; i < _curve.PointCount; i++)
            {
                Vector3 localPoint = _curve.GetPointPosition(i);
                Vector3 worldPoint = ToGlobal(localPoint);

                minBounds.X = Mathf.Min(minBounds.X, worldPoint.X);
                minBounds.Y = Mathf.Min(minBounds.Y, worldPoint.Z);
                maxBounds.X = Mathf.Max(maxBounds.X, worldPoint.X);
                maxBounds.Y = Mathf.Max(maxBounds.Y, worldPoint.Z);
            }

            // Also sample curve midpoints to catch any bulges from bezier handles
            int extraSamples = Mathf.Min(16, _curve.PointCount * 2);
            for (int i = 0; i < extraSamples; i++)
            {
                float t = (float)i / extraSamples;
                Vector3 localPoint = _curve.SampleBaked(t * bakedLength);
                Vector3 worldPoint = ToGlobal(localPoint);

                minBounds.X = Mathf.Min(minBounds.X, worldPoint.X);
                minBounds.Y = Mathf.Min(minBounds.Y, worldPoint.Z);
                maxBounds.X = Mathf.Max(maxBounds.X, worldPoint.X);
                maxBounds.Y = Mathf.Max(maxBounds.Y, worldPoint.Z);
            }

            // Add margin for profile width
            float margin = ProfileHalfWidth + 10.0f;
            minBounds -= new Vector2(margin, margin);
            maxBounds += new Vector2(margin, margin);

            // Calculate new size
            Vector2 newSize = maxBounds - minBounds;
            Vector2I newSizeValue = new Vector2I(
                Mathf.Max(64, Mathf.CeilToInt(newSize.X)),
                Mathf.Max(64, Mathf.CeilToInt(newSize.Y))
            );

            // Only update if size actually changed
            if (Size != newSizeValue)
            {
                DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.LayerDetails,
                    $"AutoResizeToCurve: {Size} → {newSizeValue}");

                Size = newSizeValue;
                PrepareMaskResources(isInteractive: false);
                _segmentDataDirty = true;
                ForceDirty();
            }
            else
            {
                DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.LayerDetails,
                    $"AutoResizeToCurve: Size unchanged at {Size}");
            }
        }
        #endregion

        #region Curve Sampling
        /// <summary>
        /// Get adaptively sampled points along the curve.
        /// Returns world-space positions.
        /// </summary>
        public Vector3[] GetSampledPoints()
        {
            if (!_curveDataDirty && _cachedSamplePoints != null)
            {
                return _cachedSamplePoints;
            }

            if (_curve == null || _curve.PointCount < 2)
            {
                _cachedSamplePoints = Array.Empty<Vector3>();
                _cachedDistances = Array.Empty<float>();
                return _cachedSamplePoints;
            }

            var points = new List<Vector3>();
            var distances = new List<float>();

            float totalLength = _curve.GetBakedLength();
            if (totalLength <= 0)
            {
                _cachedSamplePoints = Array.Empty<Vector3>();
                _cachedDistances = Array.Empty<float>();
                return _cachedSamplePoints;
            }

            if (_adaptiveResolution)
            {
                SampleAdaptive(points, distances, totalLength);
            }
            else
            {
                SampleUniform(points, distances, totalLength);
            }

            _cachedSamplePoints = points.ToArray();
            _cachedDistances = distances.ToArray();
            _curveDataDirty = false;

            return _cachedSamplePoints;
        }

        private void SampleUniform(List<Vector3> points, List<float> distances, float totalLength)
        {
            float step = totalLength / (_resolution - 1);

            for (int i = 0; i < _resolution; i++)
            {
                float dist = i * step;
                Vector3 localPoint = _curve.SampleBaked(dist);
                points.Add(ToGlobal(localPoint));
                distances.Add(dist);
            }
        }

        private void SampleAdaptive(List<Vector3> points, List<float> distances, float totalLength)
        {
            float minAngleRad = Mathf.DegToRad(_adaptiveMinAngle);
            float minStep = MIN_SEGMENT_LENGTH;
            float maxStep = totalLength / 8.0f;

            float currentDist = 0f;
            Vector3 lastPoint = ToGlobal(_curve.SampleBaked(0));
            Vector3 lastTangent = (_curve.SampleBaked(minStep) - _curve.SampleBaked(0)).Normalized();

            points.Add(lastPoint);
            distances.Add(0f);

            while (currentDist < totalLength)
            {
                // Binary search for next sample point based on angle change
                float step = maxStep;

                for (int iteration = 0; iteration < 8; iteration++)
                {
                    float testDist = Mathf.Min(currentDist + step, totalLength);
                    Vector3 testPoint = _curve.SampleBaked(testDist);

                    // Calculate tangent at test point
                    float tangentDist = Mathf.Min(testDist + 0.1f, totalLength);
                    Vector3 testTangent = (_curve.SampleBaked(tangentDist) - testPoint).Normalized();

                    float angle = lastTangent.AngleTo(testTangent);

                    if (angle > minAngleRad && step > minStep)
                    {
                        step *= 0.5f;
                    }
                    else
                    {
                        break;
                    }
                }

                step = Mathf.Max(step, minStep);
                currentDist = Mathf.Min(currentDist + step, totalLength);

                Vector3 point = ToGlobal(_curve.SampleBaked(currentDist));
                points.Add(point);
                distances.Add(currentDist);

                // Update for next iteration
                lastPoint = point;
                float nextDist = Mathf.Min(currentDist + 0.1f, totalLength);
                lastTangent = (_curve.SampleBaked(nextDist) - _curve.SampleBaked(currentDist)).Normalized();

                if (currentDist >= totalLength) break;
            }

            // Ensure we have the end point
            if (distances.Count == 0 || distances[^1] < totalLength - 0.01f)
            {
                points.Add(ToGlobal(_curve.SampleBaked(totalLength)));
                distances.Add(totalLength);
            }

            DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.MaskGeneration,
                $"Adaptive sampling: {points.Count} points for length {totalLength:F1}");
        }

        /// <summary>
        /// Get tangent direction at a distance along the curve.
        /// </summary>
        public Vector3 GetTangentAtDistance(float distance)
        {
            if (_curve == null) return Vector3.Forward;

            float totalLength = _curve.GetBakedLength();
            distance = Mathf.Clamp(distance, 0, totalLength);

            float delta = 0.1f;
            Vector3 p1 = _curve.SampleBaked(Mathf.Max(0, distance - delta));
            Vector3 p2 = _curve.SampleBaked(Mathf.Min(totalLength, distance + delta));

            return (p2 - p1).Normalized();
        }

        /// <summary>
        /// Get perpendicular (right) direction at a distance along the curve.
        /// </summary>
        public Vector3 GetPerpendicularAtDistance(float distance)
        {
            Vector3 tangent = GetTangentAtDistance(distance);
            return tangent.Cross(Vector3.Up).Normalized();
        }
        #endregion

        #region GPU Resource Management
        public override void PrepareMaskResources(bool isInteractive)
        {
            base.PrepareMaskResources(isInteractive);

            if (isInteractive && SizeHasChanged) return;

            int width = Size.X;
            int height = Size.Y;

            // SDF texture: stores signed distance to path centerline
            if (!_sdfTextureRid.IsValid || SizeHasChanged)
            {
                Gpu.FreeRid(_sdfTextureRid);
                _sdfTextureRid = Gpu.CreateTexture2D(
                    (uint)width, (uint)height,
                    RenderingDevice.DataFormat.R32Sfloat,
                    RenderingDevice.TextureUsageBits.StorageBit |
                    RenderingDevice.TextureUsageBits.SamplingBit |
                    RenderingDevice.TextureUsageBits.CanCopyFromBit
                );
            }

            // Zone data texture: stores zone index, blend factors, AND segment data
            // R = zone index, G = parameter within zone
            // B = segment index (normalized), A = parameter on segment
            if (!_zoneDataTextureRid.IsValid || SizeHasChanged)
            {
                Gpu.FreeRid(_zoneDataTextureRid);
                _zoneDataTextureRid = Gpu.CreateTexture2D(
                    (uint)width, (uint)height,
                    RenderingDevice.DataFormat.R32G32B32A32Sfloat,
                    RenderingDevice.TextureUsageBits.StorageBit |
                    RenderingDevice.TextureUsageBits.SamplingBit |
                    RenderingDevice.TextureUsageBits.CanCopyFromBit
                );
            }
        }

        private void FreeGpuResources()
        {
            Gpu.FreeRid(_sdfTextureRid);
            _sdfTextureRid = new Rid();

            Gpu.FreeRid(_zoneDataTextureRid);
            _zoneDataTextureRid = new Rid();
        }
        #endregion

        #region Abstract Implementation
        public override LayerType GetLayerType() => LayerType.Feature;

        public override string LayerTypeName() => $"{PathPresets.GetDisplayName(_pathType)} Path";
        #endregion
    }
}