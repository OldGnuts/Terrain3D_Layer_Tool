// /Editor/PathTerrainSnapPlugin.cs
#if TOOLS
using Godot;
using System;
using Terrain3DTools.Core;

namespace Terrain3DTools.Editor
{
    [Tool]
    public partial class PathTerrainSnapPlugin : EditorPlugin
    {
        private Control _toolbar;
        private CheckBox _autoSnapCheckbox;
        private SpinBox _offsetSpinBox;
        private Button _updateAllButton;
        private Button _refreshTerrainButton;
        private Label _statusLabel;
        
        private Path3D _currentPath;
        private int _lastPointCount = 0;
        private bool _autoSnap = true;
        private float _snapOffset = 0.0f;
        private TerrainLayerManager _terrainManager;

        public override void _EnterTree()
        {
            CreateToolbar();
            
            var editorInterface = GetEditorInterface();
            editorInterface.GetSelection().SelectionChanged += OnSelectionChanged;
            
            FindTerrainManager();
            
            GD.Print("[PathTerrainSnapPlugin] Enabled");
        }

        public override void _ExitTree()
        {
            var editorInterface = GetEditorInterface();
            editorInterface.GetSelection().SelectionChanged -= OnSelectionChanged;
            
            if (_toolbar != null)
            {
                RemoveControlFromContainer(CustomControlContainer.SpatialEditorMenu, _toolbar);
                _toolbar.QueueFree();
            }
            
            GD.Print("[PathTerrainSnapPlugin] Disabled");
        }

        private void CreateToolbar()
        {
            _toolbar = new HBoxContainer();
            _toolbar.AddThemeConstantOverride("separation", 8);
            
            // Auto-snap checkbox
            _autoSnapCheckbox = new CheckBox();
            _autoSnapCheckbox.Text = "Auto-Snap Path to Terrain";
            _autoSnapCheckbox.ButtonPressed = _autoSnap;
            _autoSnapCheckbox.Toggled += OnAutoSnapToggled;
            _toolbar.AddChild(_autoSnapCheckbox);
            
            // Separator
            var separator1 = new VSeparator();
            _toolbar.AddChild(separator1);
            
            // Offset control
            var offsetLabel = new Label();
            offsetLabel.Text = "Offset:";
            _toolbar.AddChild(offsetLabel);
            
            _offsetSpinBox = new SpinBox();
            _offsetSpinBox.MinValue = -100;
            _offsetSpinBox.MaxValue = 100;
            _offsetSpinBox.Step = 0.1;
            _offsetSpinBox.Value = 0;
            _offsetSpinBox.CustomMinimumSize = new Vector2(100, 0);
            _offsetSpinBox.ValueChanged += OnOffsetChanged;
            _offsetSpinBox.TooltipText = "Vertical offset from terrain surface";
            _toolbar.AddChild(_offsetSpinBox);
            
            // Separator
            var separator2 = new VSeparator();
            _toolbar.AddChild(separator2);
            
            // Update all button
            _updateAllButton = new Button();
            _updateAllButton.Text = "Snap All Points";
            _updateAllButton.Pressed += OnUpdateAllPressed;
            _updateAllButton.TooltipText = "Snap all path points to terrain";
            _toolbar.AddChild(_updateAllButton);
            
            // Refresh terrain button
            _refreshTerrainButton = new Button();
            _refreshTerrainButton.Text = "Refresh Terrain";
            _refreshTerrainButton.Pressed += OnRefreshTerrainPressed;
            _refreshTerrainButton.TooltipText = "Re-scan for terrain manager";
            _toolbar.AddChild(_refreshTerrainButton);
            
            // Status label
            _statusLabel = new Label();
            _statusLabel.Text = "  [No terrain found]";
            _toolbar.AddChild(_statusLabel);
            
            AddControlToContainer(CustomControlContainer.SpatialEditorMenu, _toolbar);
        }

        private void OnAutoSnapToggled(bool pressed)
        {
            _autoSnap = pressed;
            UpdateStatusLabel();
        }

        private void OnOffsetChanged(double value)
        {
            _snapOffset = (float)value;
        }

        private void OnUpdateAllPressed()
        {
            if (_currentPath != null && _terrainManager != null)
            {
                SnapAllPoints(_currentPath);
            }
            else
            {
                GD.PrintErr("[PathTerrainSnapPlugin] Cannot snap: No path selected or no terrain found");
            }
        }

        private void OnRefreshTerrainPressed()
        {
            FindTerrainManager();
        }

        private void OnSelectionChanged()
        {
            var selection = GetEditorInterface().GetSelection();
            var selectedNodes = selection.GetSelectedNodes();
            
            if (selectedNodes.Count > 0 && selectedNodes[0] is Path3D path3D)
            {
                _currentPath = path3D;
                _lastPointCount = path3D.Curve?.PointCount ?? 0;
                UpdateStatusLabel();
            }
            else
            {
                _currentPath = null;
                _lastPointCount = 0;
                UpdateStatusLabel();
            }
        }

        public override void _Process(double delta)
        {
            base._Process(delta);
            
            if (!_autoSnap || _currentPath == null || _terrainManager == null)
                return;
            
            var curve = _currentPath.Curve;
            if (curve == null)
                return;
            
            int currentPointCount = curve.PointCount;
            
            // Check if a new point was added
            if (currentPointCount > _lastPointCount)
            {
                // Snap the newly added point (last point)
                int newPointIndex = currentPointCount - 1;
                if (SnapPoint(_currentPath, newPointIndex))
                {
                    UpdateStatusLabel($"Snapped point {newPointIndex}");
                }
                _lastPointCount = currentPointCount;
            }
            else if (currentPointCount < _lastPointCount)
            {
                // Point was removed
                _lastPointCount = currentPointCount;
            }
        }

        private bool SnapPoint(Path3D path, int pointIndex)
        {
            if (path?.Curve == null || _terrainManager == null)
                return false;
            
            var curve = path.Curve;
            if (pointIndex < 0 || pointIndex >= curve.PointCount)
                return false;
            
            var point = curve.GetPointPosition(pointIndex);
            var worldPos = path.ToGlobal(point);
            var worldPos2D = new Vector2(worldPos.X, worldPos.Z);
            
            var height = TerrainHeightQuery.QueryHeight(worldPos2D, _terrainManager);
            
            if (height.HasValue)
            {
                float newY = height.Value + _snapOffset;
                var newWorldPos = new Vector3(worldPos.X, newY, worldPos.Z);
                var newLocalPos = path.ToLocal(newWorldPos);
                
                curve.SetPointPosition(pointIndex, newLocalPos);
                return true;
            }
            
            return false;
        }

        private void SnapAllPoints(Path3D path)
        {
            if (path?.Curve == null || _terrainManager == null)
                return;
            
            var curve = path.Curve;
            int snappedCount = 0;
            int failedCount = 0;
            
            for (int i = 0; i < curve.PointCount; i++)
            {
                if (SnapPoint(path, i))
                {
                    snappedCount++;
                }
                else
                {
                    failedCount++;
                }
            }
            
            string message = $"Snapped {snappedCount}/{curve.PointCount} points";
            if (failedCount > 0)
            {
                message += $" ({failedCount} outside terrain)";
            }
            
            UpdateStatusLabel(message);
            GD.Print($"[PathTerrainSnapPlugin] {message}");
        }

        private void FindTerrainManager()
        {
            var editorInterface = GetEditorInterface();
            var editedScene = editorInterface.GetEditedSceneRoot();
            
            if (editedScene != null)
            {
                _terrainManager = FindTerrainManagerRecursive(editedScene);
                
                if (_terrainManager != null)
                {
                    TerrainHeightQuery.SetTerrainLayerManager(_terrainManager);
                    UpdateStatusLabel($"Terrain found (Scale: {_terrainManager.WorldHeightScale})");
                    GD.Print($"[PathTerrainSnapPlugin] Found terrain manager: {_terrainManager.Name}");
                }
                else
                {
                    UpdateStatusLabel("No terrain found");
                    GD.PushWarning("[PathTerrainSnapPlugin] No TerrainLayerManager found in scene");
                }
            }
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

        private void UpdateStatusLabel(string message = null)
        {
            if (_statusLabel == null)
                return;
            
            if (message != null)
            {
                _statusLabel.Text = $"  {message}";
                return;
            }
            
            string status;
            
            if (_terrainManager == null)
            {
                status = "[No terrain found]";
            }
            else if (_currentPath == null)
            {
                status = "[No path selected]";
            }
            else
            {
                int pointCount = _currentPath.Curve?.PointCount ?? 0;
                string snapStatus = _autoSnap ? "ON" : "OFF";
                status = $"Path: {pointCount} pts | Snap: {snapStatus} | Height Scale: {_terrainManager.WorldHeightScale:F1}";
            }
            
            _statusLabel.Text = $"  {status}";
        }

        public override bool _Handles(GodotObject @object)
        {
            return @object is Path3D;
        }

        public override void _Edit(GodotObject @object)
        {
            if (@object is Path3D path3D)
            {
                _currentPath = path3D;
                _lastPointCount = path3D.Curve?.PointCount ?? 0;
                
                if (_terrainManager == null)
                {
                    FindTerrainManager();
                }
                
                UpdateStatusLabel();
            }
        }
    }
}
#endif