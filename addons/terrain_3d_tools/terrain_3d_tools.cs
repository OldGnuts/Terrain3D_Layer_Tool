// terrain_3d_tools.cs
#if TOOLS
using Godot;
using System;
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
        private PathLayerInspector _pathLayerInspector;
        #endregion

        #region PathLayer Viewport Interaction State
        private PathLayer _selectedPathLayer;
        private int _hoveredPointIndex = -1;
        private int _draggedPointIndex = -1;
        private bool _isDragging = false;
        private Vector3 _dragStartPosition;
        private Vector3 _dragOffset;

        private const float HANDLE_SCREEN_RADIUS = 15f;
        #endregion

        #region Plugin Lifecycle
        public override void _EnterTree()
        {
            // Register inspector plugins
            // Order matters - more specific inspectors should be registered first
            _pathLayerInspector = new PathLayerInspector();
            AddInspectorPlugin(_pathLayerInspector);

            _terrainLayerInspector = new TerrainLayerInspector();
            AddInspectorPlugin(_terrainLayerInspector);

            // Subscribe to selection changes for viewport interaction
            var selection = EditorInterface.Singleton.GetSelection();
            selection.SelectionChanged += OnSelectionChanged;
            OnSelectionChanged();
        }

        public override void _ExitTree()
        {
            // Unsubscribe from selection changes
            var selection = EditorInterface.Singleton.GetSelection();
            selection.SelectionChanged -= OnSelectionChanged;

            // Clear hover state on selected layer
            if (_selectedPathLayer != null && IsInstanceValid(_selectedPathLayer))
            {
                _selectedPathLayer.HoveredPointIndex = -1;
            }

            // Unregister inspector plugins (reverse order)
            if (_terrainLayerInspector != null)
            {
                RemoveInspectorPlugin(_terrainLayerInspector);
                _terrainLayerInspector = null;
            }

            if (_pathLayerInspector != null)
            {
                RemoveInspectorPlugin(_pathLayerInspector);
                _pathLayerInspector = null;
            }
        }

        public override bool _Handles(GodotObject obj)
        {
            return obj is PathLayer;
        }
        #endregion

        #region Selection Handling
        private void OnSelectionChanged()
        {
            // Clear hover state on previous layer
            if (_selectedPathLayer != null && IsInstanceValid(_selectedPathLayer))
            {
                _selectedPathLayer.HoveredPointIndex = -1;
            }

            var selection = EditorInterface.Singleton.GetSelection();
            var selectedNodes = selection.GetSelectedNodes();

            _selectedPathLayer = null;
            _hoveredPointIndex = -1;
            _draggedPointIndex = -1;
            _isDragging = false;

            foreach (var node in selectedNodes)
            {
                if (node is PathLayer pathLayer)
                {
                    _selectedPathLayer = pathLayer;
                    break;
                }
            }
        }
        #endregion

        #region Viewport Input Handling
        public override int _Forward3DGuiInput(Camera3D viewportCamera, InputEvent @event)
        {
            if (_selectedPathLayer == null || !IsInstanceValid(_selectedPathLayer))
            {
                return (int)AfterGuiInput.Pass;
            }

            if (@event is InputEventMouseMotion mouseMotion)
            {
                return HandleMouseMotion(viewportCamera, mouseMotion);
            }

            if (@event is InputEventMouseButton mouseButton)
            {
                return HandleMouseButton(viewportCamera, mouseButton);
            }

            return (int)AfterGuiInput.Pass;
        }

        private int HandleMouseMotion(Camera3D camera, InputEventMouseMotion mouseMotion)
        {
            if (_isDragging && _draggedPointIndex >= 0)
            {
                Vector3? terrainHit = RaycastTerrain(camera, mouseMotion.Position);
                if (terrainHit.HasValue)
                {
                    _selectedPathLayer.SetPointWorldPosition(_draggedPointIndex, terrainHit.Value + _dragOffset);
                    return (int)AfterGuiInput.Stop;
                }
            }
            else
            {
                int newHovered = GetPointIndexAtScreenPosition(camera, mouseMotion.Position);
                if (newHovered != _hoveredPointIndex)
                {
                    _hoveredPointIndex = newHovered;
                    _selectedPathLayer.HoveredPointIndex = newHovered;
                }
            }

            return (int)AfterGuiInput.Pass;
        }

        private int HandleMouseButton(Camera3D camera, InputEventMouseButton mouseButton)
        {
            if (mouseButton.ButtonIndex == MouseButton.Left)
            {
                if (mouseButton.Pressed)
                {
                    // Shift + click to add point
                    if (mouseButton.ShiftPressed)
                    {
                        return HandleAddPoint(camera, mouseButton.Position);
                    }

                    // Click on handle to start drag
                    int clickedPoint = GetPointIndexAtScreenPosition(camera, mouseButton.Position);
                    if (clickedPoint >= 0)
                    {
                        StartDragging(camera, clickedPoint, mouseButton.Position);
                        return (int)AfterGuiInput.Stop;
                    }
                }
                else
                {
                    if (_isDragging)
                    {
                        StopDragging();
                        return (int)AfterGuiInput.Stop;
                    }
                }
            }

            if (mouseButton.ButtonIndex == MouseButton.Right && mouseButton.Pressed && _isDragging)
            {
                CancelDragging();
                return (int)AfterGuiInput.Stop;
            }

            return (int)AfterGuiInput.Pass;
        }
        #endregion

        #region Point Dragging
        private void StartDragging(Camera3D camera, int pointIndex, Vector2 screenPos)
        {
            _draggedPointIndex = pointIndex;
            _isDragging = true;
            _dragStartPosition = _selectedPathLayer.GetPointWorldPosition(pointIndex);

            Vector3? terrainHit = RaycastTerrain(camera, screenPos);
            _dragOffset = terrainHit.HasValue ? _dragStartPosition - terrainHit.Value : Vector3.Zero;

            var undoRedo = GetUndoRedo();
            undoRedo.CreateAction("Move Path Point");
            undoRedo.AddUndoMethod(this, MethodName.SetPointPosition, pointIndex, _dragStartPosition);
        }

        private void StopDragging()
        {
            if (!_isDragging || _draggedPointIndex < 0) return;

            Vector3 finalPosition = _selectedPathLayer.GetPointWorldPosition(_draggedPointIndex);

            var undoRedo = GetUndoRedo();
            undoRedo.AddDoMethod(this, MethodName.SetPointPosition, _draggedPointIndex, finalPosition);
            undoRedo.CommitAction();

            _isDragging = false;
            _draggedPointIndex = -1;
        }

        private void CancelDragging()
        {
            if (!_isDragging || _draggedPointIndex < 0) return;

            _selectedPathLayer.SetPointWorldPosition(_draggedPointIndex, _dragStartPosition);

            _isDragging = false;
            _draggedPointIndex = -1;
        }

        private void SetPointPosition(int index, Vector3 position)
        {
            if (_selectedPathLayer != null && IsInstanceValid(_selectedPathLayer))
            {
                _selectedPathLayer.SetPointWorldPosition(index, position);
            }
        }
        #endregion

        #region Point Adding
        private int HandleAddPoint(Camera3D camera, Vector2 screenPos)
        {
            Vector3? terrainHit = RaycastTerrain(camera, screenPos);
            if (!terrainHit.HasValue) return (int)AfterGuiInput.Pass;

            Vector3 newPointPos = terrainHit.Value;
            int insertIndex = FindBestInsertIndex(newPointPos);

            var undoRedo = GetUndoRedo();
            undoRedo.CreateAction("Add Path Point");
            undoRedo.AddDoMethod(this, MethodName.InsertPoint, insertIndex, newPointPos);
            undoRedo.AddUndoMethod(this, MethodName.RemovePoint, insertIndex);
            undoRedo.CommitAction();

            return (int)AfterGuiInput.Stop;
        }

        private int FindBestInsertIndex(Vector3 worldPos)
        {
            if (_selectedPathLayer.PointCount == 0) return 0;
            if (_selectedPathLayer.PointCount == 1) return 1;

            float minDist = float.MaxValue;
            int bestIndex = _selectedPathLayer.PointCount;

            for (int i = 0; i < _selectedPathLayer.PointCount - 1; i++)
            {
                Vector3 p1 = _selectedPathLayer.GetPointWorldPosition(i);
                Vector3 p2 = _selectedPathLayer.GetPointWorldPosition(i + 1);

                float dist = DistanceToSegment(worldPos, p1, p2, out float t);

                if (t > 0.1f && t < 0.9f && dist < minDist)
                {
                    minDist = dist;
                    bestIndex = i + 1;
                }
            }

            float distToStart = worldPos.DistanceTo(_selectedPathLayer.GetPointWorldPosition(0));
            float distToEnd = worldPos.DistanceTo(_selectedPathLayer.GetPointWorldPosition(_selectedPathLayer.PointCount - 1));

            if (distToStart < minDist && distToStart < distToEnd)
                bestIndex = 0;
            else if (distToEnd < minDist)
                bestIndex = _selectedPathLayer.PointCount;

            return bestIndex;
        }

        private float DistanceToSegment(Vector3 point, Vector3 segStart, Vector3 segEnd, out float t)
        {
            Vector3 seg = segEnd - segStart;
            float segLengthSq = seg.LengthSquared();

            if (segLengthSq < 0.0001f)
            {
                t = 0f;
                return point.DistanceTo(segStart);
            }

            t = Mathf.Clamp((point - segStart).Dot(seg) / segLengthSq, 0f, 1f);
            Vector3 closest = segStart + seg * t;
            return point.DistanceTo(closest);
        }

        private void InsertPoint(int index, Vector3 position)
        {
            _selectedPathLayer?.InsertPoint(index, position);
        }

        private void RemovePoint(int index)
        {
            _selectedPathLayer?.RemovePoint(index);
        }
        #endregion

        #region Raycasting
        private int GetPointIndexAtScreenPosition(Camera3D camera, Vector2 screenPos)
        {
            if (_selectedPathLayer == null) return -1;

            float closestDist = HANDLE_SCREEN_RADIUS;
            int closestIndex = -1;

            for (int i = 0; i < _selectedPathLayer.PointCount; i++)
            {
                Vector3 worldPos = _selectedPathLayer.GetPointWorldPosition(i);
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

        private Vector3? RaycastTerrain(Camera3D camera, Vector2 screenPos)
        {
            Vector3 rayOrigin = camera.ProjectRayOrigin(screenPos);
            Vector3 rayDir = camera.ProjectRayNormal(screenPos);

            float estimatedY = _selectedPathLayer?.GlobalPosition.Y ?? 0f;

            try
            {
                var manager = TerrainHeightQuery.GetTerrainLayerManager();
                if (manager?.Terrain3DNode != null)
                {
                    var terrain = Terrain3D.Bind(manager.Terrain3DNode);
                    if (terrain?.Data != null)
                    {
                        for (int i = 0; i < 3; i++)
                        {
                            if (Mathf.Abs(rayDir.Y) < 0.0001f) break;

                            float t = (estimatedY - rayOrigin.Y) / rayDir.Y;
                            if (t < 0) break;

                            Vector3 hitPoint = rayOrigin + rayDir * t;
                            float terrainHeight = (float)terrain.Data.GetHeight(hitPoint);

                            if (!float.IsNaN(terrainHeight))
                            {
                                estimatedY = terrainHeight;
                                hitPoint.Y = terrainHeight;
                                if (Mathf.Abs(hitPoint.Y - terrainHeight) < 0.1f)
                                    return hitPoint;
                            }
                            else break;
                        }
                    }
                }
            }
            catch { }

            // Fallback: intersect with horizontal plane
            if (Mathf.Abs(rayDir.Y) > 0.0001f)
            {
                float t = (estimatedY - rayOrigin.Y) / rayDir.Y;
                if (t > 0) return rayOrigin + rayDir * t;
            }

            return null;
        }
        #endregion
    }
}
#endif