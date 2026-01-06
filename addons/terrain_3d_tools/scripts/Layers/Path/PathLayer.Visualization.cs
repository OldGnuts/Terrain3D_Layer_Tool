// /Layers/Path/PathLayer.Visualization.cs
using Godot;
using Terrain3DTools.Layers.Path;
using Terrain3DTools.Core;
using TokisanGames;
using System.Linq;

namespace Terrain3DTools.Layers
{
    /// <summary>
    /// Viewport visualization for PathLayer using ImmediateMesh.
    /// Draws path centerline, zone width indicators, and control point markers.
    /// 
    /// Note: Curve is in world coordinates, but the mesh is a child of PathLayer,
    /// so vertices need to be converted to local space for rendering.
    /// Since PathLayer is at origin, ToLocal(worldPos) effectively equals worldPos.
    /// </summary>
    public partial class PathLayer
    {
        #region Private Fields
        private MeshInstance3D _pathVisualizerMesh;
        private ImmediateMesh _immediateMesh;
        private int _hoveredPointIndex = -1;
        #endregion

        #region Public Properties
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
        #endregion

        #region Visualizer Setup
        private void CreatePathVisualizer()
        {
            if (_pathVisualizerMesh != null) return;

            _immediateMesh = new ImmediateMesh();

            _pathVisualizerMesh = new MeshInstance3D
            {
                Mesh = _immediateMesh,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                Visible = false
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
        #endregion

        #region Main Draw Methods
        private void UpdatePathVisualization()
        {
            if (!Engine.IsEditorHint() || _immediateMesh == null) return;

            _immediateMesh.ClearSurfaces();

            var curve = Curve;
            if (curve == null || curve.PointCount == 0) return;

            float totalLength = curve.GetBakedLength();
            if (totalLength <= 0) return;

            DrawZoneWidthIndicators(curve, totalLength);
            DrawPathCenterline(curve, totalLength);
            DrawControlPointMarkers(curve);
            DrawBoundsIndicator();
        }

        private void DrawZoneWidthIndicators(Curve3D curve, float totalLength)
        {
            if (_profile == null) return;
            
            var enabledZones = _profile.GetEnabledZones().ToList();
            if (enabledZones.Count == 0) return;

            int widthSamples = Mathf.Max(8, Mathf.Min(32, (int)(totalLength / 4.0f)));

            // Calculate zone boundaries
            float[] zoneBoundaries = new float[enabledZones.Count];
            float accumulated = 0f;
            for (int z = 0; z < enabledZones.Count; z++)
            {
                accumulated += enabledZones[z].Width;
                zoneBoundaries[z] = accumulated;
            }

            // Draw all zone lines batched per zone
            for (int z = 0; z < enabledZones.Count; z++)
            {
                var zone = enabledZones[z];
                float innerDist = z == 0 ? 0f : zoneBoundaries[z - 1];
                float outerDist = zoneBoundaries[z];
                Color zoneColor = ZoneColors.GetColor(zone.Type, ZoneColors.ColorContext.Viewport);

                _immediateMesh.SurfaceBegin(Mesh.PrimitiveType.Lines);
                _immediateMesh.SurfaceSetColor(zoneColor);

                for (int i = 0; i <= widthSamples; i++)
                {
                    float t = (float)i / widthSamples;
                    float dist = t * totalLength;

                    // Curve is in world coords
                    Vector3 worldPoint = curve.SampleBaked(dist);

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

                    // Convert world to local for mesh (PathLayer is at origin, so this is essentially a no-op)
                    _immediateMesh.SurfaceAddVertex(ToLocal(innerLeft));
                    _immediateMesh.SurfaceAddVertex(ToLocal(outerLeft));
                    _immediateMesh.SurfaceAddVertex(ToLocal(innerRight));
                    _immediateMesh.SurfaceAddVertex(ToLocal(outerRight));
                }

                _immediateMesh.SurfaceEnd();
            }
        }

        private void DrawPathCenterline(Curve3D curve, float totalLength)
        {
            int samples = Mathf.Max(16, Mathf.Min(64, (int)(totalLength / 2.0f)));
            var pathColor = new Color(1.0f, 0.9f, 0.3f, 0.95f);

            _immediateMesh.SurfaceBegin(Mesh.PrimitiveType.LineStrip);
            _immediateMesh.SurfaceSetColor(pathColor);

            for (int i = 0; i <= samples; i++)
            {
                float t = (float)i / samples;
                float dist = t * totalLength;

                Vector3 worldPoint = curve.SampleBaked(dist);

                float terrainHeight = QueryTerrainHeight(worldPoint);
                worldPoint.Y = terrainHeight + 0.2f;

                _immediateMesh.SurfaceAddVertex(ToLocal(worldPoint));
            }

            _immediateMesh.SurfaceEnd();
        }

        private void DrawControlPointMarkers(Curve3D curve)
        {
            if (curve.PointCount == 0) return;

            float markerSize = 0.5f;
            float hoveredMarkerSize = 0.7f;

            _immediateMesh.SurfaceBegin(Mesh.PrimitiveType.Lines);

            for (int i = 0; i < curve.PointCount; i++)
            {
                // Curve stores world positions
                Vector3 worldPos = curve.GetPointPosition(i);

                float terrainHeight = QueryTerrainHeight(worldPos);
                worldPos.Y = terrainHeight + 0.25f;

                Vector3 center = ToLocal(worldPos);

                bool isHovered = (i == _hoveredPointIndex);
                float size = isHovered ? hoveredMarkerSize : markerSize;

                Color markerColor = GetPointMarkerColor(i, curve.PointCount, isHovered);

                _immediateMesh.SurfaceSetColor(markerColor);
                DrawDiamondMarker(center, size);

                // Vertical drop line
                Color dropColor = markerColor with { A = 0.5f };
                _immediateMesh.SurfaceSetColor(dropColor);
                _immediateMesh.SurfaceAddVertex(center);
                _immediateMesh.SurfaceAddVertex(center - new Vector3(0, 2.0f, 0));

                if (isHovered)
                {
                    DrawSelectionRing(center, size * 1.3f);
                }
            }

            _immediateMesh.SurfaceEnd();
        }

        /// <summary>
        /// Draw the mask bounds rectangle for debugging.
        /// </summary>
        private void DrawBoundsIndicator()
        {
            if (!_maskBoundsInitialized) return;

            var boundsColor = new Color(0.5f, 0.5f, 1.0f, 0.3f);

            _immediateMesh.SurfaceBegin(Mesh.PrimitiveType.Lines);
            _immediateMesh.SurfaceSetColor(boundsColor);

            float y = 0.1f; // Slight offset above ground

            Vector3 corner1 = ToLocal(new Vector3(_maskWorldMin.X, y, _maskWorldMin.Y));
            Vector3 corner2 = ToLocal(new Vector3(_maskWorldMax.X, y, _maskWorldMin.Y));
            Vector3 corner3 = ToLocal(new Vector3(_maskWorldMax.X, y, _maskWorldMax.Y));
            Vector3 corner4 = ToLocal(new Vector3(_maskWorldMin.X, y, _maskWorldMax.Y));

            // Draw rectangle
            _immediateMesh.SurfaceAddVertex(corner1);
            _immediateMesh.SurfaceAddVertex(corner2);
            
            _immediateMesh.SurfaceAddVertex(corner2);
            _immediateMesh.SurfaceAddVertex(corner3);
            
            _immediateMesh.SurfaceAddVertex(corner3);
            _immediateMesh.SurfaceAddVertex(corner4);
            
            _immediateMesh.SurfaceAddVertex(corner4);
            _immediateMesh.SurfaceAddVertex(corner1);

            _immediateMesh.SurfaceEnd();
        }
        #endregion

        #region Drawing Helpers
        private Color GetPointMarkerColor(int index, int totalPoints, bool isHovered)
        {
            Color color;
            if (index == 0)
                color = new Color(0.2f, 1.0f, 0.3f, 1.0f); // Green - start
            else if (index == totalPoints - 1)
                color = new Color(1.0f, 0.3f, 0.2f, 1.0f); // Red - end
            else
                color = new Color(1.0f, 0.8f, 0.2f, 1.0f); // Yellow - middle

            if (isHovered)
                color = color.Lightened(0.3f);

            return color;
        }

        private void DrawDiamondMarker(Vector3 center, float size)
        {
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
        }

        private void DrawSelectionRing(Vector3 center, float ringSize)
        {
            Color ringColor = Colors.White with { A = 0.8f };
            _immediateMesh.SurfaceSetColor(ringColor);

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
        #endregion

        #region Terrain Height Query
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
                // Silently fall back
            }

            return worldPos.Y;
        }
        #endregion
    }
}