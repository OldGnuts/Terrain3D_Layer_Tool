// /Editor/PathLayerEditorPlugin.cs
#if TOOLS
using Godot;
using System;
using Terrain3DTools.Layers;
using Terrain3DTools.Core;
using TokisanGames;

namespace Terrain3DTools.Editor
{
    /// <summary>
    /// Editor plugin for PathLayer viewport interaction.
    /// Provides interactive handles for moving and adding path points.
    /// </summary>
    [Tool]
    public partial class PathLayerEditorPlugin : EditorPlugin
    {
        #region Constants
        private const float HANDLE_RADIUS = 0.8f;           // World-space radius for handle detection
        private const float HANDLE_SCREEN_RADIUS = 12f;     // Screen-space radius (pixels)
        private const float ADD_POINT_KEY = (float)Key.Shift;
        #endregion

        #region State
        private PathLayer _selectedPathLayer;
        private int _hoveredPointIndex = -1;
        private int _draggedPointIndex = -1;
        private bool _isDragging = false;
        private Vector3 _dragStartPosition;
        private Vector3 _dragOffset;
        private Camera3D _editorCamera;
        #endregion

        #region Plugin Lifecycle
        public override void _EnterTree()
        {
            var selection = EditorInterface.Singleton.GetSelection();
            selection.SelectionChanged += OnSelectionChanged;
            OnSelectionChanged();
        }

        public override void _ExitTree()
        {
            var selection = EditorInterface.Singleton.GetSelection();
            selection.SelectionChanged -= OnSelectionChanged;
            _selectedPathLayer = null;
        }

        public override bool _Handles(GodotObject obj)
        {
            return obj is PathLayer;
        }

        public override string _GetPluginName()
        {
            return "PathLayerEditor";
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
                return (int)EditorPlugin.AfterGuiInput.Pass;
            }

            _editorCamera = viewportCamera;

            // Handle mouse motion (hover detection)
            if (@event is InputEventMouseMotion mouseMotion)
            {
                return HandleMouseMotion(mouseMotion);
            }

            // Handle mouse buttons (click/drag)
            if (@event is InputEventMouseButton mouseButton)
            {
                return HandleMouseButton(mouseButton);
            }

            return (int)EditorPlugin.AfterGuiInput.Pass;
        }

        private int HandleMouseMotion(InputEventMouseMotion mouseMotion)
        {
            if (_isDragging && _draggedPointIndex >= 0)
            {
                // Update dragged point position
                Vector3? terrainHit = RaycastTerrain(mouseMotion.Position);
                if (terrainHit.HasValue)
                {
                    _selectedPathLayer.SetPointWorldPosition(_draggedPointIndex, terrainHit.Value + _dragOffset);
                    return (int)EditorPlugin.AfterGuiInput.Stop;
                }
            }
            else
            {
                // Update hover state
                int newHovered = GetPointIndexAtScreenPosition(mouseMotion.Position);
                if (newHovered != _hoveredPointIndex)
                {
                    _hoveredPointIndex = newHovered;

                    // Update PathLayer's hover state for visual feedback
                    if (_selectedPathLayer != null && IsInstanceValid(_selectedPathLayer))
                    {
                        _selectedPathLayer.HoveredPointIndex = newHovered;
                    }
                }
            }

            return (int)EditorPlugin.AfterGuiInput.Pass;
        }

        private int HandleMouseButton(InputEventMouseButton mouseButton)
        {
            // Left mouse button
            if (mouseButton.ButtonIndex == MouseButton.Left)
            {
                if (mouseButton.Pressed)
                {
                    // Check if Shift is held for adding points
                    if (mouseButton.ShiftPressed)
                    {
                        return HandleAddPoint(mouseButton.Position);
                    }

                    // Check if clicking on a handle
                    int clickedPoint = GetPointIndexAtScreenPosition(mouseButton.Position);
                    if (clickedPoint >= 0)
                    {
                        StartDragging(clickedPoint, mouseButton.Position);
                        return (int)EditorPlugin.AfterGuiInput.Stop;
                    }
                }
                else
                {
                    // Mouse released
                    if (_isDragging)
                    {
                        StopDragging();
                        return (int)EditorPlugin.AfterGuiInput.Stop;
                    }
                }
            }

            // Right mouse button - cancel drag
            if (mouseButton.ButtonIndex == MouseButton.Right && mouseButton.Pressed)
            {
                if (_isDragging)
                {
                    CancelDragging();
                    return (int)EditorPlugin.AfterGuiInput.Stop;
                }
            }

            return (int)EditorPlugin.AfterGuiInput.Pass;
        }
        #endregion

        #region Dragging Operations
        private void StartDragging(int pointIndex, Vector2 screenPos)
        {
            _draggedPointIndex = pointIndex;
            _isDragging = true;
            _dragStartPosition = _selectedPathLayer.GetPointWorldPosition(pointIndex);

            // Calculate offset from terrain hit to actual point position
            Vector3? terrainHit = RaycastTerrain(screenPos);
            if (terrainHit.HasValue)
            {
                _dragOffset = _dragStartPosition - terrainHit.Value;
            }
            else
            {
                _dragOffset = Vector3.Zero;
            }

            // Create undo action
            var undoRedo = GetUndoRedo();
            undoRedo.CreateAction("Move Path Point");
            undoRedo.AddDoMethod(this, nameof(SetPointPosition), pointIndex, _dragStartPosition);
            undoRedo.AddUndoMethod(this, nameof(SetPointPosition), pointIndex, _dragStartPosition);
        }

        private void StopDragging()
        {
            if (!_isDragging || _draggedPointIndex < 0) return;

            Vector3 finalPosition = _selectedPathLayer.GetPointWorldPosition(_draggedPointIndex);

            // Update undo action with final position
            var undoRedo = GetUndoRedo();
            undoRedo.AddDoMethod(this, nameof(SetPointPosition), _draggedPointIndex, finalPosition);
            undoRedo.CommitAction();

            _isDragging = false;
            _draggedPointIndex = -1;
        }

        private void CancelDragging()
        {
            if (!_isDragging || _draggedPointIndex < 0) return;

            // Restore original position
            _selectedPathLayer.SetPointWorldPosition(_draggedPointIndex, _dragStartPosition);

            _isDragging = false;
            _draggedPointIndex = -1;
        }

        // Called by undo/redo system
        private void SetPointPosition(int index, Vector3 position)
        {
            if (_selectedPathLayer != null && IsInstanceValid(_selectedPathLayer))
            {
                _selectedPathLayer.SetPointWorldPosition(index, position);
            }
        }
        #endregion

        #region Add Point Operations
        private int HandleAddPoint(Vector2 screenPos)
        {
            Vector3? terrainHit = RaycastTerrain(screenPos);
            if (!terrainHit.HasValue) return (int)EditorPlugin.AfterGuiInput.Pass;

            Vector3 newPointPos = terrainHit.Value;

            // Find where to insert the point (closest segment or at end)
            int insertIndex = FindBestInsertIndex(newPointPos);

            // Create undo action
            var undoRedo = GetUndoRedo();
            undoRedo.CreateAction("Add Path Point");
            undoRedo.AddDoMethod(this, nameof(AddPointAtIndex), insertIndex, newPointPos);
            undoRedo.AddUndoMethod(this, nameof(RemovePointAtIndex), insertIndex);
            undoRedo.CommitAction();

            return (int)EditorPlugin.AfterGuiInput.Stop;
        }

        private int FindBestInsertIndex(Vector3 worldPos)
        {
            if (_selectedPathLayer.PointCount == 0) return 0;
            if (_selectedPathLayer.PointCount == 1) return 1;

            float minDist = float.MaxValue;
            int bestIndex = _selectedPathLayer.PointCount; // Default: append at end

            // Check distance to each segment
            for (int i = 0; i < _selectedPathLayer.PointCount - 1; i++)
            {
                Vector3 p1 = _selectedPathLayer.GetPointWorldPosition(i);
                Vector3 p2 = _selectedPathLayer.GetPointWorldPosition(i + 1);

                float dist = DistanceToSegment(worldPos, p1, p2, out float t);

                // Only consider if point projects onto segment (not extensions)
                if (t > 0.1f && t < 0.9f && dist < minDist)
                {
                    minDist = dist;
                    bestIndex = i + 1;
                }
            }

            // If no good segment found, check if closer to start or end
            float distToStart = worldPos.DistanceTo(_selectedPathLayer.GetPointWorldPosition(0));
            float distToEnd = worldPos.DistanceTo(_selectedPathLayer.GetPointWorldPosition(_selectedPathLayer.PointCount - 1));

            if (distToStart < minDist && distToStart < distToEnd)
            {
                bestIndex = 0;
            }
            else if (distToEnd < minDist)
            {
                bestIndex = _selectedPathLayer.PointCount;
            }

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

        // Called by undo/redo system
        private void AddPointAtIndex(int index, Vector3 position)
        {
            if (_selectedPathLayer != null && IsInstanceValid(_selectedPathLayer))
            {
                _selectedPathLayer.InsertPoint(index, position);
            }
        }

        // Called by undo/redo system
        private void RemovePointAtIndex(int index)
        {
            if (_selectedPathLayer != null && IsInstanceValid(_selectedPathLayer))
            {
                _selectedPathLayer.RemovePoint(index);
            }
        }
        #endregion

        #region Raycasting
        private int GetPointIndexAtScreenPosition(Vector2 screenPos)
        {
            if (_selectedPathLayer == null || _editorCamera == null) return -1;

            float closestDist = HANDLE_SCREEN_RADIUS;
            int closestIndex = -1;

            for (int i = 0; i < _selectedPathLayer.PointCount; i++)
            {
                Vector3 worldPos = _selectedPathLayer.GetPointWorldPosition(i);

                // Check if point is in front of camera
                if (!_editorCamera.IsPositionInFrustum(worldPos)) continue;

                // Project to screen space
                Vector2 screenPoint = _editorCamera.UnprojectPosition(worldPos);
                float dist = screenPos.DistanceTo(screenPoint);

                if (dist < closestDist)
                {
                    closestDist = dist;
                    closestIndex = i;
                }
            }

            return closestIndex;
        }

        private Vector3? RaycastTerrain(Vector2 screenPos)
        {
            if (_editorCamera == null) return null;

            // Get ray from camera
            Vector3 rayOrigin = _editorCamera.ProjectRayOrigin(screenPos);
            Vector3 rayDir = _editorCamera.ProjectRayNormal(screenPos);

            // Try to get terrain height at intersection
            // We'll raycast against a large horizontal plane first, then refine with terrain height

            // Simple plane intersection at Y = estimated terrain height
            float estimatedY = _selectedPathLayer.GlobalPosition.Y;

            // Try to get actual terrain
            try
            {
                var manager = TerrainHeightQuery.GetTerrainLayerManager();
                if (manager?.Terrain3DNode != null)
                {
                    var terrain = Terrain3D.Bind(manager.Terrain3DNode);
                    if (terrain?.Data != null)
                    {
                        // Iterative refinement - start with plane intersection, then get actual height
                        for (int iteration = 0; iteration < 3; iteration++)
                        {
                            // Intersect ray with horizontal plane at estimatedY
                            if (Mathf.Abs(rayDir.Y) < 0.0001f) break;

                            float t = (estimatedY - rayOrigin.Y) / rayDir.Y;
                            if (t < 0) break;

                            Vector3 hitPoint = rayOrigin + rayDir * t;

                            // Get actual terrain height at this XZ position
                            float terrainHeight = (float)terrain.Data.GetHeight(hitPoint);
                            if (!float.IsNaN(terrainHeight))
                            {
                                estimatedY = terrainHeight;
                                hitPoint.Y = terrainHeight;

                                // If we're close enough, return
                                if (Mathf.Abs(hitPoint.Y - terrainHeight) < 0.1f)
                                {
                                    return hitPoint;
                                }
                            }
                            else
                            {
                                break;
                            }
                        }
                    }
                }
            }
            catch
            {
                // Fallback to simple plane
            }

            // Fallback: intersect with plane at layer Y position
            if (Mathf.Abs(rayDir.Y) > 0.0001f)
            {
                float t = (estimatedY - rayOrigin.Y) / rayDir.Y;
                if (t > 0)
                {
                    return rayOrigin + rayDir * t;
                }
            }

            return null;
        }
        #endregion

        #region Visual Feedback
        private void UpdateHandleVisuals()
        {
            // The PathLayer's visualizer will update based on hover state
            // We can pass hover info to it or trigger a redraw
            // For now, the visualizer handles its own drawing
        }
        #endregion
    }
}
#endif