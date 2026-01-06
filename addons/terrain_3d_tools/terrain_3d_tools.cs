// /terrain_3d_tools.cs
#if TOOLS
using Godot;
using System;
using System.Collections.Generic;
using Terrain3DTools.Editor;
using Terrain3DTools.Layers;
using Terrain3DTools.Layers.Path;
using Terrain3DTools.Core;
using TokisanGames;

namespace Terrain3DTools
{
    [Tool]
    public partial class terrain_3d_tools : EditorPlugin
    {
        #region Inspector Plugins
        private TerrainLayerInspector _terrainLayerInspector;
        private TextureLayerInspector _textureLayerInspector;
        private PathLayerInspector _pathLayerInspector;
        private InstancerLayerInspector _instancerLayerInspector;
        #endregion

        #region Toolbar
        private Button _settingsButton;
        private bool _toolbarVisible = false;
        #endregion

        #region PathLayer Viewport Interaction State
        private PathLayer _selectedPathLayer;
        private Path3D _selectedPath3DCurveEditor;
        private int _hoveredPointIndex = -1;
        private int _draggedPointIndex = -1;
        private bool _isDragging = false;
        private bool _undoActionOpen = false;
        private Vector3 _dragStartPosition;
        private Vector3 _dragStartHandleIn;
        private Vector3 _dragStartHandleOut;

        private const float HANDLE_SCREEN_RADIUS = 15f;
        private const float DRAG_THRESHOLD_PIXELS = 5f;
        private const float SEGMENT_INSERT_THRESHOLD = 500f;

        private Vector2 _mouseDownPosition;
        private bool _mouseIsDown = false;
        private int _pendingDragPointIndex = -1;
        #endregion

        #region Plugin Lifecycle
        public override void _EnterTree()
        {
            _textureLayerInspector = new TextureLayerInspector();
            AddInspectorPlugin(_textureLayerInspector);

            _pathLayerInspector = new PathLayerInspector();
            AddInspectorPlugin(_pathLayerInspector);

            _terrainLayerInspector = new TerrainLayerInspector();
            AddInspectorPlugin(_terrainLayerInspector);

            _instancerLayerInspector = new InstancerLayerInspector();  // ADD THIS
            AddInspectorPlugin(_instancerLayerInspector);              // ADD THIS

            // Create toolbar button
            CreateToolbarButton();

            var selection = EditorInterface.Singleton.GetSelection();
            selection.SelectionChanged += OnSelectionChanged;
            OnSelectionChanged();
        }

        public override void _ExitTree()
        {
            var selection = EditorInterface.Singleton.GetSelection();
            selection.SelectionChanged -= OnSelectionChanged;

            if (_selectedPathLayer != null && IsInstanceValid(_selectedPathLayer))
            {
                _selectedPathLayer.HoveredPointIndex = -1;
            }

            CleanupDragState();

            // Remove toolbar button
            RemoveToolbarButton();

            if (_terrainLayerInspector != null)
            {
                RemoveInspectorPlugin(_terrainLayerInspector);
                _terrainLayerInspector = null;
            }

            if (_textureLayerInspector != null)
            {
                RemoveInspectorPlugin(_textureLayerInspector);
                _textureLayerInspector = null;
            }

            if (_pathLayerInspector != null)
            {
                RemoveInspectorPlugin(_pathLayerInspector);
                _pathLayerInspector = null;
            }

            // ADD THIS BLOCK
            if (_instancerLayerInspector != null)
            {
                RemoveInspectorPlugin(_instancerLayerInspector);
                _instancerLayerInspector = null;
            }
        }

        public override bool _Handles(GodotObject obj)
        {
            if (obj is PathLayer) return true;

            if (obj is Path3D path3D && path3D.Name.ToString().EndsWith("_CurveEditor"))
            {
                var parent = path3D.GetParent();
                return parent is PathLayer;
            }

            return false;
        }
        #endregion

        #region Toolbar Management
        private void CreateToolbarButton()
        {
            _settingsButton = new Button
            {
                Text = "⚙️ Terrain Tools",
                TooltipText = "Open Terrain3D Tools Global Settings",
                Flat = false,
                Visible = false
            };
            _settingsButton.Pressed += OnSettingsButtonPressed;

            // Add to the 3D editor toolbar
            AddControlToContainer(CustomControlContainer.SpatialEditorMenu, _settingsButton);
        }

        private void RemoveToolbarButton()
        {
            if (_settingsButton != null)
            {
                RemoveControlFromContainer(CustomControlContainer.SpatialEditorMenu, _settingsButton);
                _settingsButton.QueueFree();
                _settingsButton = null;
            }
        }

        private void UpdateToolbarVisibility(bool shouldShow)
        {
            if (_settingsButton == null) return;

            if (_toolbarVisible != shouldShow)
            {
                _toolbarVisible = shouldShow;
                _settingsButton.Visible = shouldShow;
            }
        }

        private void OnSettingsButtonPressed()
        {
            var editorBase = EditorInterface.Singleton?.GetBaseControl();
            if (editorBase == null) return;

            // Find the terrain layer manager if available
            TerrainLayerManager manager = null;
            var tree = EditorInterface.Singleton?.GetEditedSceneRoot()?.GetTree();
            if (tree != null)
            {
                manager = tree.GetFirstNodeInGroup("terrain_layer_manager") as TerrainLayerManager;
            }

            GlobalSettingsWindow.Show(editorBase, manager);
        }
        #endregion

        #region Selection Handling
        private void OnSelectionChanged()
        {
            if (_selectedPathLayer != null && IsInstanceValid(_selectedPathLayer))
            {
                _selectedPathLayer.HoveredPointIndex = -1;
            }

            CleanupDragState();

            var selection = EditorInterface.Singleton.GetSelection();
            var selectedNodes = selection.GetSelectedNodes();

            _selectedPathLayer = null;
            _selectedPath3DCurveEditor = null;
            _hoveredPointIndex = -1;

            bool shouldShowToolbar = false;

            foreach (var node in selectedNodes)
            {
                // Check for our tool nodes
                if (node is TerrainLayerManager ||
                    node is TerrainLayerBase ||
                    node is PathLayer ||
                    (node is Path3D path3D && path3D.Name.ToString().EndsWith("_CurveEditor")))
                {
                    shouldShowToolbar = true;
                }

                // PathLayer specific handling
                if (node is PathLayer pathLayer)
                {
                    _selectedPathLayer = pathLayer;
                    _selectedPath3DCurveEditor = pathLayer.GetCurveEditorNode();
                    break;
                }

                if (node is Path3D curveEditor && curveEditor.Name.ToString().EndsWith("_CurveEditor"))
                {
                    var parent = curveEditor.GetParent();
                    if (parent is PathLayer ownerLayer)
                    {
                        _selectedPath3DCurveEditor = curveEditor;
                        _selectedPathLayer = ownerLayer;
                        shouldShowToolbar = true;
                        break;
                    }
                }
            }

            UpdateToolbarVisibility(shouldShowToolbar);
        }

        private void CleanupDragState()
        {
            if (_undoActionOpen && _isDragging)
            {
                try
                {
                    var undoRedo = GetUndoRedo();
                    if (_selectedPath3DCurveEditor?.Curve != null && _draggedPointIndex >= 0)
                    {
                        undoRedo.AddDoMethod(this, MethodName.RestoreCurvePoint, _draggedPointIndex,
                            _dragStartPosition, _dragStartHandleIn, _dragStartHandleOut);
                    }
                    else if (_selectedPathLayer != null && _draggedPointIndex >= 0)
                    {
                        undoRedo.AddDoMethod(this, MethodName.SetPointPosition, _draggedPointIndex, _dragStartPosition);
                    }
                    undoRedo.CommitAction();
                }
                catch { }
            }

            _isDragging = false;
            _mouseIsDown = false;
            _draggedPointIndex = -1;
            _pendingDragPointIndex = -1;
            _undoActionOpen = false;
        }
        #endregion

        #region Viewport Input Handling
        public override int _Forward3DGuiInput(Camera3D viewportCamera, InputEvent @event)
        {
            if (_selectedPathLayer == null || !IsInstanceValid(_selectedPathLayer))
            {
                return (int)AfterGuiInput.Pass;
            }

            if (_selectedPath3DCurveEditor == null)
            {
                _selectedPath3DCurveEditor = _selectedPathLayer.GetCurveEditorNode();
            }

            if (@event is InputEventMouseButton mouseButton)
            {
                return HandleMouseButton(viewportCamera, mouseButton);
            }

            if (@event is InputEventMouseMotion mouseMotion)
            {
                return HandleMouseMotion(viewportCamera, mouseMotion);
            }

            if (@event is InputEventKey keyEvent)
            {
                return HandleKeyInput(keyEvent);
            }

            return (int)AfterGuiInput.Pass;
        }

        private int HandleKeyInput(InputEventKey keyEvent)
        {
            if (!keyEvent.Pressed) return (int)AfterGuiInput.Pass;

            if (keyEvent.CtrlPressed && keyEvent.Keycode == Key.S)
            {
                if (_selectedPathLayer != null)
                {
                    _selectedPathLayer.ApplyAutoSmooth(_selectedPathLayer.AutoSmoothTension);
                    return (int)AfterGuiInput.Stop;
                }
            }

            if ((keyEvent.Keycode == Key.Delete || keyEvent.Keycode == Key.Backspace) && _hoveredPointIndex >= 0)
            {
                DeleteCurvePoint(_hoveredPointIndex);
                return (int)AfterGuiInput.Stop;
            }

            return (int)AfterGuiInput.Pass;
        }

        private int HandleMouseButton(Camera3D camera, InputEventMouseButton mouseButton)
        {
            var curve = _selectedPath3DCurveEditor?.Curve;

            if (mouseButton.ButtonIndex == MouseButton.Left)
            {
                if (mouseButton.Pressed)
                {
                    _mouseDownPosition = mouseButton.Position;
                    _mouseIsDown = true;

                    if (mouseButton.AltPressed)
                    {
                        GD.Print($"=== Ctrl+Click Debug ===");
                        GD.Print($"Mouse position: {mouseButton.Position}");
                        GD.Print($"Curve null: {curve == null}");
                        GD.Print($"Curve point count: {curve?.PointCount ?? 0}");

                        Vector3? terrainHit = RaycastTerrain(camera, mouseButton.Position);
                        GD.Print($"Terrain hit: {terrainHit.HasValue}");
                        if (terrainHit.HasValue)
                        {
                            GD.Print($"Terrain hit position: {terrainHit.Value}");
                        }

                        if (terrainHit.HasValue && curve != null)
                        {
                            int segmentIndex = GetNearestSegmentIndex(camera, mouseButton.Position, curve);
                            GD.Print($"Nearest segment index: {segmentIndex}");

                            if (curve.PointCount >= 2)
                            {
                                for (int i = 0; i < curve.PointCount - 1; i++)
                                {
                                    Vector3 p1 = curve.GetPointPosition(i);
                                    Vector3 p2 = curve.GetPointPosition(i + 1);

                                    bool p1InFrustum = camera.IsPositionInFrustum(p1);
                                    bool p2InFrustum = camera.IsPositionInFrustum(p2);

                                    GD.Print($"Segment {i}: p1InFrustum={p1InFrustum}, p2InFrustum={p2InFrustum}");

                                    if (p1InFrustum || p2InFrustum)
                                    {
                                        Vector2 sp1 = camera.UnprojectPosition(p1);
                                        Vector2 sp2 = camera.UnprojectPosition(p2);

                                        float dist = DistanceToLineSegment2D(mouseButton.Position, sp1, sp2, out float t);
                                        GD.Print($"  Screen p1: {sp1}, p2: {sp2}");
                                        GD.Print($"  Distance: {dist}, t: {t}");
                                        GD.Print($"  Threshold check: dist < 20 = {dist < 20f}, t in range = {t > 0.1f && t < 0.9f}");
                                    }
                                }
                            }

                            if (segmentIndex >= 0)
                            {
                                GD.Print($"Inserting point into segment {segmentIndex}");
                                InsertPointIntoSegment(curve, segmentIndex, terrainHit.Value);
                                return (int)AfterGuiInput.Stop;
                            }
                            else
                            {
                                GD.Print($"No valid segment found for insertion");
                            }
                        }

                        GD.Print($"=== End Ctrl+Click Debug ===");
                    }

                    int clickedPoint = -1;
                    if (curve != null && curve.PointCount > 0)
                    {
                        clickedPoint = GetCurvePointAtScreenPosition(camera, mouseButton.Position, curve);
                    }

                    if (clickedPoint >= 0)
                    {
                        _pendingDragPointIndex = clickedPoint;
                        _dragStartPosition = curve.GetPointPosition(clickedPoint);
                        _dragStartHandleIn = curve.GetPointIn(clickedPoint);
                        _dragStartHandleOut = curve.GetPointOut(clickedPoint);

                        _hoveredPointIndex = clickedPoint;
                        _selectedPathLayer.HoveredPointIndex = clickedPoint;

                        return (int)AfterGuiInput.Pass;
                    }
                    else
                    {
                        _pendingDragPointIndex = -1;

                        Vector3? terrainHit = RaycastTerrain(camera, mouseButton.Position);
                        if (!terrainHit.HasValue) return (int)AfterGuiInput.Pass;

                        if (mouseButton.ShiftPressed)
                        {
                            AddPointToCurve(curve, terrainHit.Value);
                            return (int)AfterGuiInput.Stop;
                        }

                        return (int)AfterGuiInput.Pass;
                    }
                }
                else
                {
                    bool wasDragging = _isDragging;
                    _mouseIsDown = false;

                    if (_isDragging)
                    {
                        StopCurveDragging(curve);
                    }

                    _pendingDragPointIndex = -1;
                    return wasDragging ? (int)AfterGuiInput.Stop : (int)AfterGuiInput.Pass;
                }
            }

            if (mouseButton.ButtonIndex == MouseButton.Right && mouseButton.Pressed)
            {
                if (_isDragging)
                {
                    CancelCurveDragging(curve);
                    return (int)AfterGuiInput.Stop;
                }
                else if (_hoveredPointIndex >= 0)
                {
                    DeleteCurvePoint(_hoveredPointIndex);
                    return (int)AfterGuiInput.Stop;
                }
            }

            return (int)AfterGuiInput.Pass;
        }

        private int HandleMouseMotion(Camera3D camera, InputEventMouseMotion mouseMotion)
        {
            var curve = _selectedPath3DCurveEditor?.Curve;

            if (_mouseIsDown && _pendingDragPointIndex >= 0 && !_isDragging)
            {
                float distanceMoved = mouseMotion.Position.DistanceTo(_mouseDownPosition);
                if (distanceMoved > DRAG_THRESHOLD_PIXELS)
                {
                    StartCurveDragging(_pendingDragPointIndex, curve);
                }
            }

            if (_isDragging && _draggedPointIndex >= 0)
            {
                Vector3? terrainHit = RaycastTerrain(camera, mouseMotion.Position);
                if (terrainHit.HasValue && curve != null)
                {
                    curve.SetPointPosition(_draggedPointIndex, terrainHit.Value);
                    return (int)AfterGuiInput.Stop;
                }
                return (int)AfterGuiInput.Stop;
            }
            else if (!_isDragging && curve != null && curve.PointCount > 0)
            {
                int newHovered = GetCurvePointAtScreenPosition(camera, mouseMotion.Position, curve);
                if (newHovered != _hoveredPointIndex)
                {
                    _hoveredPointIndex = newHovered;
                    if (_selectedPathLayer != null)
                    {
                        _selectedPathLayer.HoveredPointIndex = newHovered;
                    }
                }
            }

            return (int)AfterGuiInput.Pass;
        }
        #endregion

        #region Point Operations
        private void AddPointToCurve(Curve3D curve, Vector3 position)
        {
            if (curve == null)
            {
                if (_selectedPath3DCurveEditor != null)
                {
                    _selectedPath3DCurveEditor.Curve = new Curve3D();
                    curve = _selectedPath3DCurveEditor.Curve;
                }
                else return;
            }

            int insertIndex = FindExtendIndex(curve, position);

            var undoRedo = GetUndoRedo();
            undoRedo.CreateAction("Add Curve Point");
            undoRedo.AddDoMethod(this, MethodName.DoAddCurvePoint, insertIndex, position);
            undoRedo.AddUndoMethod(this, MethodName.DoRemoveCurvePoint, insertIndex);
            undoRedo.CommitAction();
        }

        private void InsertPointIntoSegment(Curve3D curve, int segmentIndex, Vector3 position)
        {
            if (curve == null || segmentIndex < 0 || segmentIndex >= curve.PointCount - 1)
                return;

            int insertIndex = segmentIndex + 1;

            var undoRedo = GetUndoRedo();
            undoRedo.CreateAction("Insert Curve Point");
            undoRedo.AddDoMethod(this, MethodName.DoAddCurvePoint, insertIndex, position);
            undoRedo.AddUndoMethod(this, MethodName.DoRemoveCurvePoint, insertIndex);
            undoRedo.CommitAction();
        }

        private void DeleteCurvePoint(int index)
        {
            var curve = _selectedPath3DCurveEditor?.Curve;
            if (curve == null || index < 0 || index >= curve.PointCount) return;
            if (curve.PointCount <= 2) return;

            Vector3 position = curve.GetPointPosition(index);
            Vector3 handleIn = curve.GetPointIn(index);
            Vector3 handleOut = curve.GetPointOut(index);

            var undoRedo = GetUndoRedo();
            undoRedo.CreateAction("Delete Curve Point");
            undoRedo.AddDoMethod(this, MethodName.DoRemoveCurvePoint, index);
            undoRedo.AddUndoMethod(this, MethodName.DoInsertCurvePointWithHandles, index, position, handleIn, handleOut);
            undoRedo.CommitAction();

            _hoveredPointIndex = -1;
            if (_selectedPathLayer != null)
            {
                _selectedPathLayer.HoveredPointIndex = -1;
            }
        }

        private int FindExtendIndex(Curve3D curve, Vector3 position)
        {
            if (curve == null || curve.PointCount == 0) return 0;
            if (curve.PointCount == 1) return 1;

            Vector3 startPoint = curve.GetPointPosition(0);
            Vector3 endPoint = curve.GetPointPosition(curve.PointCount - 1);

            float distToStart = new Vector2(position.X - startPoint.X, position.Z - startPoint.Z).Length();
            float distToEnd = new Vector2(position.X - endPoint.X, position.Z - endPoint.Z).Length();

            return distToStart < distToEnd ? 0 : curve.PointCount;
        }

        private int GetNearestSegmentIndex(Camera3D camera, Vector2 screenPos, Curve3D curve)
        {
            if (curve == null || curve.PointCount < 2) return -1;

            float minDist = SEGMENT_INSERT_THRESHOLD;
            int bestSegment = -1;

            for (int i = 0; i < curve.PointCount - 1; i++)
            {
                Vector3 p1 = curve.GetPointPosition(i);
                Vector3 p2 = curve.GetPointPosition(i + 1);

                if (!camera.IsPositionInFrustum(p1) && !camera.IsPositionInFrustum(p2))
                    continue;

                Vector2 sp1 = camera.UnprojectPosition(p1);
                Vector2 sp2 = camera.UnprojectPosition(p2);

                float dist = DistanceToLineSegment2D(screenPos, sp1, sp2, out float t);

                if (t > 0.1f && t < 0.9f && dist < minDist)
                {
                    minDist = dist;
                    bestSegment = i;
                }
            }

            return bestSegment;
        }

        private float DistanceToLineSegment2D(Vector2 point, Vector2 lineStart, Vector2 lineEnd, out float t)
        {
            Vector2 line = lineEnd - lineStart;
            float lineLengthSq = line.LengthSquared();

            if (lineLengthSq < 0.0001f)
            {
                t = 0f;
                return point.DistanceTo(lineStart);
            }

            t = Mathf.Clamp((point - lineStart).Dot(line) / lineLengthSq, 0f, 1f);
            Vector2 closest = lineStart + line * t;
            return point.DistanceTo(closest);
        }
        #endregion

        #region Undo/Redo Methods
        private void DoAddCurvePoint(int index, Vector3 position)
        {
            var curve = _selectedPath3DCurveEditor?.Curve;
            if (curve == null) return;

            if (index <= 0)
            {
                InsertCurvePointAt(curve, 0, position);
            }
            else if (index >= curve.PointCount)
            {
                curve.AddPoint(position);
            }
            else
            {
                InsertCurvePointAt(curve, index, position);
            }

            ApplySmoothingAfterEdit();
        }

        private void DoRemoveCurvePoint(int index)
        {
            var curve = _selectedPath3DCurveEditor?.Curve;
            if (curve == null || index < 0 || index >= curve.PointCount) return;

            curve.RemovePoint(index);
            ApplySmoothingAfterEdit();
        }

        private void DoInsertCurvePointWithHandles(int index, Vector3 position, Vector3 handleIn, Vector3 handleOut)
        {
            var curve = _selectedPath3DCurveEditor?.Curve;
            if (curve == null) return;

            var points = new List<(Vector3 pos, Vector3 inH, Vector3 outH)>();

            for (int i = 0; i < curve.PointCount; i++)
            {
                if (i == index)
                {
                    points.Add((position, handleIn, handleOut));
                }
                points.Add((
                    curve.GetPointPosition(i),
                    curve.GetPointIn(i),
                    curve.GetPointOut(i)
                ));
            }

            if (index >= curve.PointCount)
            {
                points.Add((position, handleIn, handleOut));
            }

            curve.ClearPoints();
            foreach (var (pos, inH, outH) in points)
            {
                curve.AddPoint(pos, inH, outH);
            }

            ApplySmoothingAfterEdit();
        }

        private void InsertCurvePointAt(Curve3D curve, int index, Vector3 position)
        {
            var points = new List<(Vector3 pos, Vector3 inH, Vector3 outH)>();

            for (int i = 0; i < curve.PointCount; i++)
            {
                if (i == index)
                {
                    points.Add((position, Vector3.Zero, Vector3.Zero));
                }
                points.Add((
                    curve.GetPointPosition(i),
                    curve.GetPointIn(i),
                    curve.GetPointOut(i)
                ));
            }

            if (index >= curve.PointCount)
            {
                points.Add((position, Vector3.Zero, Vector3.Zero));
            }

            curve.ClearPoints();
            foreach (var (pos, inH, outH) in points)
            {
                curve.AddPoint(pos, inH, outH);
            }
        }

        private void ApplySmoothingAfterEdit()
        {
            if (_selectedPathLayer == null || !IsInstanceValid(_selectedPathLayer)) return;
            if (!_selectedPathLayer.AutoSmoothCurve) return;

            var curve = _selectedPath3DCurveEditor?.Curve;
            if (curve == null || curve.PointCount < 2) return;

            _selectedPathLayer.ApplyAutoSmooth(_selectedPathLayer.AutoSmoothTension);
        }
        #endregion

        #region Curve Point Dragging
        private void StartCurveDragging(int pointIndex, Curve3D curve)
        {
            if (curve == null || pointIndex < 0 || pointIndex >= curve.PointCount) return;

            _draggedPointIndex = pointIndex;
            _isDragging = true;
            _pendingDragPointIndex = -1;

            var undoRedo = GetUndoRedo();
            undoRedo.CreateAction("Move Curve Point");
            _undoActionOpen = true;

            undoRedo.AddUndoMethod(this, MethodName.RestoreCurvePoint, pointIndex,
                _dragStartPosition, _dragStartHandleIn, _dragStartHandleOut);
        }

        private void StopCurveDragging(Curve3D curve)
        {
            if (!_isDragging || _draggedPointIndex < 0) return;

            if (_undoActionOpen)
            {
                try
                {
                    if (curve != null && _draggedPointIndex < curve.PointCount)
                    {
                        Vector3 finalPosition = curve.GetPointPosition(_draggedPointIndex);
                        Vector3 finalHandleIn = curve.GetPointIn(_draggedPointIndex);
                        Vector3 finalHandleOut = curve.GetPointOut(_draggedPointIndex);

                        var undoRedo = GetUndoRedo();
                        undoRedo.AddDoMethod(this, MethodName.RestoreCurvePoint, _draggedPointIndex,
                            finalPosition, finalHandleIn, finalHandleOut);
                        undoRedo.CommitAction();

                        ApplySmoothingAfterEdit();
                    }
                }
                catch { }
                _undoActionOpen = false;
            }

            _isDragging = false;
            _draggedPointIndex = -1;
        }

        private void CancelCurveDragging(Curve3D curve)
        {
            if (!_isDragging || _draggedPointIndex < 0) return;

            if (curve != null && _draggedPointIndex < curve.PointCount)
            {
                curve.SetPointPosition(_draggedPointIndex, _dragStartPosition);
                curve.SetPointIn(_draggedPointIndex, _dragStartHandleIn);
                curve.SetPointOut(_draggedPointIndex, _dragStartHandleOut);
            }

            if (_undoActionOpen)
            {
                try
                {
                    var undoRedo = GetUndoRedo();
                    undoRedo.AddDoMethod(this, MethodName.RestoreCurvePoint, _draggedPointIndex,
                        _dragStartPosition, _dragStartHandleIn, _dragStartHandleOut);
                    undoRedo.CommitAction();
                }
                catch { }
                _undoActionOpen = false;
            }

            _isDragging = false;
            _draggedPointIndex = -1;
        }

        private void RestoreCurvePoint(int index, Vector3 position, Vector3 handleIn, Vector3 handleOut)
        {
            var curve = _selectedPath3DCurveEditor?.Curve;
            if (curve == null || index < 0 || index >= curve.PointCount) return;

            curve.SetPointPosition(index, position);
            curve.SetPointIn(index, handleIn);
            curve.SetPointOut(index, handleOut);

            ApplySmoothingAfterEdit();
        }
        #endregion

        #region Point/Curve Queries
        private int GetCurvePointAtScreenPosition(Camera3D camera, Vector2 screenPos, Curve3D curve)
        {
            if (curve == null || curve.PointCount == 0) return -1;

            float closestDist = HANDLE_SCREEN_RADIUS;
            int closestIndex = -1;

            for (int i = 0; i < curve.PointCount; i++)
            {
                Vector3 worldPos = curve.GetPointPosition(i);
                if (!camera.IsPositionInFrustum(worldPos)) continue;

                Vector2 screenPoint = camera.UnprojectPosition(worldPos);
                float dist = screenPos.DistanceTo(screenPoint);

                if (dist < closestDist)
                {
                    closestDist = dist;
                    closestIndex = i;
                }
            }

            return closestIndex;
        }
        #endregion

        #region Raycasting
        private Vector3? RaycastTerrain(Camera3D camera, Vector2 screenPos)
        {
            Vector3 rayOrigin = camera.ProjectRayOrigin(screenPos);
            Vector3 rayDir = camera.ProjectRayNormal(screenPos);

            try
            {
                var manager = TerrainHeightQuery.GetTerrainLayerManager();
                if (manager?.Terrain3DNode != null)
                {
                    var terrain = Terrain3D.Bind(manager.Terrain3DNode);
                    if (terrain?.Data != null)
                    {
                        float nearT = 1f;
                        float farT = 2000f;

                        for (int i = 0; i < 20; i++)
                        {
                            float midT = (nearT + farT) * 0.5f;
                            Vector3 midPoint = rayOrigin + rayDir * midT;
                            float terrainHeight = (float)terrain.Data.GetHeight(midPoint);

                            if (float.IsNaN(terrainHeight))
                            {
                                farT = midT;
                                continue;
                            }

                            float diff = midPoint.Y - terrainHeight;

                            if (Mathf.Abs(diff) < 0.05f)
                            {
                                midPoint.Y = terrainHeight;
                                return midPoint;
                            }

                            if (diff > 0)
                                nearT = midT;
                            else
                                farT = midT;
                        }

                        float finalT = (nearT + farT) * 0.5f;
                        Vector3 finalPoint = rayOrigin + rayDir * finalT;
                        float finalHeight = (float)terrain.Data.GetHeight(finalPoint);

                        if (!float.IsNaN(finalHeight))
                        {
                            finalPoint.Y = finalHeight;
                            return finalPoint;
                        }
                    }
                }
            }
            catch { }

            // Fallback
            if (Mathf.Abs(rayDir.Y) > 0.0001f)
            {
                float t = -rayOrigin.Y / rayDir.Y;
                if (t > 0)
                    return rayOrigin + rayDir * t;
            }

            return null;
        }
        #endregion

        #region PathLayer Direct Methods
        private void SetPointPosition(int index, Vector3 position)
        {
            if (_selectedPathLayer != null && IsInstanceValid(_selectedPathLayer))
            {
                _selectedPathLayer.SetPointWorldPosition(index, position);
            }
        }
        #endregion
    }
}
#endif