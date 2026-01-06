#if TOOLS
using Godot;
using Terrain3DTools.Layers;
using Terrain3DTools.Core;

namespace Terrain3DTools.Editor
{
    /// <summary>
    /// Editor plugin for PathLayer. 
    /// Handles selection synchronization and visualization updates.
    /// Curve editing is delegated to the native Path3D editor via helper node.
    /// </summary>
    [Tool]
    public partial class PathLayerEditorPlugin : EditorPlugin
    {
        private PathLayer _selectedPathLayer;

        public override void _EnterTree()
        {
            var selection = EditorInterface.Singleton.GetSelection();
            selection.SelectionChanged += OnSelectionChanged;
        }

        public override void _ExitTree()
        {
            var selection = EditorInterface.Singleton.GetSelection();
            selection.SelectionChanged -= OnSelectionChanged;
            _selectedPathLayer = null;
        }

        public override bool _Handles(GodotObject obj)
        {
            // Handle both PathLayer and its helper Path3D
            if (obj is PathLayer) return true;
            if (obj is Path3D path3D && path3D.GetParent() is PathLayer) return true;
            return false;
        }

        public override string _GetPluginName() => "PathLayerEditor";

        private void OnSelectionChanged()
        {
            var selection = EditorInterface.Singleton.GetSelection();
            var selectedNodes = selection.GetSelectedNodes();

            _selectedPathLayer = null;

            foreach (var node in selectedNodes)
            {
                if (node is PathLayer pathLayer)
                {
                    _selectedPathLayer = pathLayer;
                    break;
                }
                // If the helper Path3D is selected, track its parent PathLayer
                if (node is Path3D path3D && path3D.GetParent() is PathLayer parentLayer)
                {
                    _selectedPathLayer = parentLayer;
                    break;
                }
            }
        }

        public override int _Forward3DGuiInput(Camera3D viewportCamera, InputEvent @event)
        {
            // Let Godot's native Path3D editor handle curve editing
            // We only need to handle PathLayer-specific interactions here

            if (_selectedPathLayer == null)
                return (int)EditorPlugin.AfterGuiInput.Pass;

            // Optional: Add shift+click to add points at terrain position
            if (@event is InputEventMouseButton mouseButton &&
                mouseButton.ButtonIndex == MouseButton.Left &&
                mouseButton.Pressed &&
                mouseButton.ShiftPressed)
            {
                return HandleAddPointAtClick(viewportCamera, mouseButton.Position);
            }

            return (int)EditorPlugin.AfterGuiInput.Pass;
        }

        private int HandleAddPointAtClick(Camera3D camera, Vector2 screenPos)
        {
            Vector3? terrainHit = RaycastTerrain(camera, screenPos);
            if (!terrainHit.HasValue)
                return (int)EditorPlugin.AfterGuiInput.Pass;

            // Find best insert position
            int insertIndex = FindBestInsertIndex(terrainHit.Value);

            var undoRedo = GetUndoRedo();
            undoRedo.CreateAction("Add Path Point");
            undoRedo.AddDoMethod(this, nameof(DoInsertPoint), insertIndex, terrainHit.Value);
            undoRedo.AddUndoMethod(this, nameof(DoRemovePoint), insertIndex);
            undoRedo.CommitAction();

            return (int)EditorPlugin.AfterGuiInput.Stop;
        }

        // Methods called by undo/redo system
        private void DoInsertPoint(int index, Vector3 position)
        {
            if (_selectedPathLayer != null && IsInstanceValid(_selectedPathLayer))
            {
                _selectedPathLayer.InsertPoint(index, position);
            }
        }

        private void DoRemovePoint(int index)
        {
            if (_selectedPathLayer != null && IsInstanceValid(_selectedPathLayer))
            {
                _selectedPathLayer.RemovePoint(index);
            }
        }

        private int FindBestInsertIndex(Vector3 worldPos)
        {
            if (_selectedPathLayer == null || _selectedPathLayer.PointCount == 0)
                return 0;
            if (_selectedPathLayer.PointCount == 1)
                return 1;

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

            // Check if closer to start or end
            float distToStart = worldPos.DistanceTo(_selectedPathLayer.GetPointWorldPosition(0));
            float distToEnd = worldPos.DistanceTo(_selectedPathLayer.GetPointWorldPosition(_selectedPathLayer.PointCount - 1));

            if (distToStart < minDist && distToStart < distToEnd)
                return 0;
            if (distToEnd < minDist)
                return _selectedPathLayer.PointCount;

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
            return point.DistanceTo(segStart + seg * t);
        }

        private Vector3? RaycastTerrain(Camera3D camera, Vector2 screenPos)
        {
            if (camera == null || _selectedPathLayer == null) return null;

            Vector3 rayOrigin = camera.ProjectRayOrigin(screenPos);
            Vector3 rayDir = camera.ProjectRayNormal(screenPos);

            float estimatedY = _selectedPathLayer.GlobalPosition.Y;

            try
            {
                var manager = TerrainHeightQuery.GetTerrainLayerManager();
                if (manager?.Terrain3DNode != null)
                {
                    var terrain = TokisanGames.Terrain3D.Bind(manager.Terrain3DNode);
                    if (terrain?.Data != null)
                    {
                        for (int iteration = 0; iteration < 3; iteration++)
                        {
                            if (Mathf.Abs(rayDir.Y) < 0.0001f) break;

                            float t = (estimatedY - rayOrigin.Y) / rayDir.Y;
                            if (t < 0) break;

                            Vector3 hitPoint = rayOrigin + rayDir * t;
                            float terrainHeight = (float)terrain.Data.GetHeight(hitPoint);

                            if (!float.IsNaN(terrainHeight))
                            {
                                hitPoint.Y = terrainHeight;
                                if (Mathf.Abs(estimatedY - terrainHeight) < 0.1f)
                                    return hitPoint;
                                estimatedY = terrainHeight;
                            }
                            else break;
                        }
                    }
                }
            }
            catch { }

            // Fallback
            if (Mathf.Abs(rayDir.Y) > 0.0001f)
            {
                float t = (estimatedY - rayOrigin.Y) / rayDir.Y;
                if (t > 0) return rayOrigin + rayDir * t;
            }

            return null;
        }
    }
}
#endif