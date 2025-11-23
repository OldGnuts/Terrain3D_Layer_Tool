// /Nodes/TerrainPath3D.cs
using Godot;
using System;
using Terrain3DTools.Core;

namespace Terrain3DTools.Nodes
{
    [GlobalClass, Tool]
    public partial class TerrainPath3D : Path3D
    {
        #region Private Fields
        private bool _autoSnapToTerrain = true;
        private float _snapOffset = 0.0f;
        private int _lastPointCount = 0;
        private bool _snapOnMove = true;
        private TerrainLayerManager _terrainManager;
        private double _updateTimer = 0.0;
        private const double UPDATE_INTERVAL = 0.1;
        private bool _debugMode = false;
        #endregion

        #region Exported Properties
        [ExportGroup("Terrain Snapping")]
        [Export]
        public bool AutoSnapToTerrain
        {
            get => _autoSnapToTerrain;
            set => _autoSnapToTerrain = value;
        }

        [Export]
        public bool SnapOnMove
        {
            get => _snapOnMove;
            set => _snapOnMove = value;
        }

        [Export(PropertyHint.Range, "-100,100,0.1")]
        public float SnapOffset
        {
            get => _snapOffset;
            set => _snapOffset = value;
        }

        [Export]
        public TerrainLayerManager TerrainManager
        {
            get => _terrainManager;
            set
            {
                _terrainManager = value;
                if (_terrainManager != null)
                {
                    DebugPrint($"TerrainManager assigned: {_terrainManager.Name}");
                }
            }
        }

        [Export]
        public bool DebugMode
        {
            get => _debugMode;
            set => _debugMode = value;
        }

        [ExportGroup("Manual Actions")]
        [Export]
        public bool SnapAllPointsNow
        {
            get => false;
            set
            {
                if (value)
                {
                    SnapAllPoints();
                }
            }
        }

        [Export]
        public bool FindTerrainManagerNow
        {
            get => false;
            set
            {
                if (value)
                {
                    FindTerrainManager();
                }
            }
        }
        #endregion

        #region Godot Lifecycle
        public override void _Ready()
        {
            base._Ready();

            if (Curve == null)
            {
                Curve = new Curve3D();
            }

            _lastPointCount = Curve.PointCount;

            // Try to find terrain manager
            if (_terrainManager == null)
            {
                FindTerrainManager();
            }

            DebugPrint($"TerrainPath3D ready with {Curve.PointCount} points");
        }

        public override void _Process(double delta)
        {
            base._Process(delta);

            if (!Engine.IsEditorHint() || !_autoSnapToTerrain || Curve == null)
                return;

            _updateTimer += delta;
            if (_updateTimer < UPDATE_INTERVAL)
                return;

            _updateTimer = 0;

            // Check if terrain manager is still valid
            if (_terrainManager == null || !GodotObject.IsInstanceValid(_terrainManager))
            {
                FindTerrainManager();
            }

            if (_terrainManager == null)
                return;

            int currentPointCount = Curve.PointCount;

            // New point added
            if (currentPointCount > _lastPointCount)
            {
                SnapPoint(currentPointCount - 1);
                _lastPointCount = currentPointCount;
            }
            // Point removed
            else if (currentPointCount < _lastPointCount)
            {
                _lastPointCount = currentPointCount;
            }
            // Same count - check if position changed (if snap on move is enabled)
            else if (_snapOnMove && currentPointCount > 0)
            {
                // Optionally check last point for movement
                // This can be expensive, so we only do it at intervals
            }
        }
        #endregion

        #region Terrain Snapping
        private void SnapPoint(int pointIndex)
        {
            if (Curve == null || _terrainManager == null)
                return;

            if (pointIndex < 0 || pointIndex >= Curve.PointCount)
                return;

            var point = Curve.GetPointPosition(pointIndex);
            var worldPos = ToGlobal(point);
            var worldPos2D = new Vector2(worldPos.X, worldPos.Z);

            var height = TerrainHeightQuery.QueryHeight(worldPos2D, _terrainManager);

            if (height.HasValue)
            {
                float newY = height.Value + _snapOffset;
                var newWorldPos = new Vector3(worldPos.X, newY, worldPos.Z);
                var newLocalPos = ToLocal(newWorldPos);

                Curve.SetPointPosition(pointIndex, newLocalPos);

                DebugPrint($"Snapped point {pointIndex}: World({worldPos2D.X:F2}, {worldPos2D.Y:F2}) -> Height: {newY:F2} (terrain: {height.Value:F2} + offset: {_snapOffset:F2})");
            }
            else
            {
                DebugPrint($"No terrain height at point {pointIndex}: World({worldPos2D.X:F2}, {worldPos2D.Y:F2})");
            }
        }

        public void SnapAllPoints()
        {
            if (Curve == null)
            {
                GD.PushWarning("[TerrainPath3D] Cannot snap points: Curve is null");
                return;
            }

            if (_terrainManager == null)
            {
                FindTerrainManager();
                if (_terrainManager == null)
                {
                    GD.PrintErr("[TerrainPath3D] Cannot snap points: No TerrainLayerManager found");
                    return;
                }
            }

            int snappedCount = 0;
            int outsideCount = 0;

            DebugPrint($"Snapping all {Curve.PointCount} points...");

            for (int i = 0; i < Curve.PointCount; i++)
            {
                var point = Curve.GetPointPosition(i);
                var worldPos = ToGlobal(point);
                var worldPos2D = new Vector2(worldPos.X, worldPos.Z);

                var height = TerrainHeightQuery.QueryHeight(worldPos2D, _terrainManager);

                if (height.HasValue)
                {
                    float newY = height.Value + _snapOffset;
                    var newWorldPos = new Vector3(worldPos.X, newY, worldPos.Z);
                    var newLocalPos = ToLocal(newWorldPos);

                    Curve.SetPointPosition(i, newLocalPos);
                    snappedCount++;
                }
                else
                {
                    outsideCount++;
                }
            }

            string message = $"Snapped {snappedCount}/{Curve.PointCount} points";
            if (outsideCount > 0)
            {
                message += $", {outsideCount} points outside terrain bounds";
            }

            GD.Print($"[TerrainPath3D] {message}");
        }

        public void SnapPointsByIndices(int[] indices)
        {
            if (Curve == null || _terrainManager == null || indices == null)
                return;

            foreach (int index in indices)
            {
                SnapPoint(index);
            }
        }
        #endregion

        #region Terrain Manager Discovery
        private void FindTerrainManager()
        {
            // Try to use the static reference first
            _terrainManager = TerrainHeightQuery.GetTerrainLayerManager();
            
            if (_terrainManager != null)
            {
                DebugPrint($"Found terrain manager via static reference: {_terrainManager.Name}");
                return;
            }

            // Search up the scene tree
            Node current = GetParent();
            while (current != null)
            {
                if (current is TerrainLayerManager manager)
                {
                    _terrainManager = manager;
                    DebugPrint($"Found terrain manager in parent hierarchy: {_terrainManager.Name}");
                    return;
                }
                current = current.GetParent();
            }

            // Search the entire scene tree as last resort
            var root = GetTree()?.EditedSceneRoot ?? GetTree()?.Root;
            if (root != null)
            {
                _terrainManager = FindTerrainManagerRecursive(root);
                if (_terrainManager != null)
                {
                    DebugPrint($"Found terrain manager in scene tree: {_terrainManager.Name}");
                    return;
                }
            }

            DebugPrint("No TerrainLayerManager found");
        }

        private TerrainLayerManager FindTerrainManagerRecursive(Node node)
        {
            if (node is TerrainLayerManager manager)
                return manager;

            foreach (Node child in node.GetChildren())
            {
                var result = FindTerrainManagerRecursive(child);
                if (result != null)
                    return result;
            }

            return null;
        }
        #endregion

        #region Helper Methods
        private void DebugPrint(string message)
        {
            if (_debugMode)
            {
                GD.Print($"[TerrainPath3D:{Name}] {message}");
            }
        }

        /// <summary>
        /// Get the world height at a specific point index
        /// </summary>
        public float? GetTerrainHeightAtPoint(int pointIndex)
        {
            if (Curve == null || pointIndex < 0 || pointIndex >= Curve.PointCount)
                return null;

            if (_terrainManager == null)
            {
                FindTerrainManager();
                if (_terrainManager == null)
                    return null;
            }

            var point = Curve.GetPointPosition(pointIndex);
            var worldPos = ToGlobal(point);
            var worldPos2D = new Vector2(worldPos.X, worldPos.Z);

            return TerrainHeightQuery.QueryHeight(worldPos2D, _terrainManager);
        }

        /// <summary>
        /// Get the world height at a specific curve parameter t (0 to 1)
        /// </summary>
        public float? GetTerrainHeightAtParameter(float t)
        {
            if (Curve == null || _terrainManager == null)
                return null;

            var point = Curve.SampleBaked(t * Curve.GetBakedLength());
            var worldPos = ToGlobal(point);
            var worldPos2D = new Vector2(worldPos.X, worldPos.Z);

            return TerrainHeightQuery.QueryHeight(worldPos2D, _terrainManager);
        }

        /// <summary>
        /// Check if a point is within terrain bounds
        /// </summary>
        public bool IsPointOnTerrain(int pointIndex)
        {
            return GetTerrainHeightAtPoint(pointIndex).HasValue;
        }
        #endregion
    }
}