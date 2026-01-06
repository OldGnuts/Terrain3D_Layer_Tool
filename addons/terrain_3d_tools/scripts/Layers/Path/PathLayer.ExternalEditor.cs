//PathLayer.ExternalEditor.cs
using Godot;
using Terrain3DTools.Core.Debug;

namespace Terrain3DTools.Layers
{
    public partial class PathLayer
    {
        #region Curve Editor Lifecycle
        private void EnsureCurveEditorExists()
        {
            if (_externalCurveEditor != null && IsInstanceValid(_externalCurveEditor))
            {
                EnsureCurveEditorAtOrigin();
                ConnectToCurveEditor();
                return;
            }

            _externalCurveEditor = FindExistingCurveEditor();

            if (_externalCurveEditor == null)
            {
                _externalCurveEditor = CreateCurveEditor();
            }

            ConnectToCurveEditor();
        }

        private Path3D FindExistingCurveEditor()
        {
            // First, check children (new structure)
            foreach (var child in GetChildren())
            {
                if (child is Path3D path3D && path3D.Name.ToString().EndsWith("_CurveEditor"))
                {
                    DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.LayerLifecycle,
                        $"Found existing curve editor as child: {path3D.Name}");
                    EnsureCurveEditorAtOrigin(path3D);
                    return path3D;
                }
            }

            // Also check siblings for backwards compatibility (old structure)
            var parent = GetParent();
            if (parent != null)
            {
                string expectedName = $"{Name}_CurveEditor";
                foreach (var sibling in parent.GetChildren())
                {
                    if (sibling is Path3D path3D && path3D.Name == expectedName && sibling != this)
                    {
                        DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.LayerLifecycle,
                            $"Found existing curve editor as sibling, migrating to child: {path3D.Name}");

                        // Migrate: reparent from sibling to child
                        MigrateCurveEditorToChild(path3D);
                        return path3D;
                    }
                }
            }

            return null;
        }

        private void MigrateCurveEditorToChild(Path3D path3D)
        {
            var curveData = path3D.Curve;

            path3D.GetParent()?.RemoveChild(path3D);
            AddChild(path3D);
            path3D.Curve = curveData;
            path3D.Transform = Transform3D.Identity;

            if (Engine.IsEditorHint())
            {
                var sceneRoot = GetTree()?.EditedSceneRoot;
                if (sceneRoot != null)
                {
                    path3D.Owner = sceneRoot;
                }
            }
        }

        private Path3D CreateCurveEditor()
        {
            string editorName = $"{Name}_CurveEditor";

            var newEditor = new Path3D
            {
                Name = editorName,
                Transform = Transform3D.Identity,
                Curve = new Curve3D { BakeInterval = CURVE_BAKE_INTERVAL }
            };

            AddChild(newEditor, forceReadableName: true);

            if (Engine.IsEditorHint())
            {
                var sceneRoot = GetTree()?.EditedSceneRoot;
                if (sceneRoot != null)
                {
                    newEditor.Owner = sceneRoot;
                }
            }

            DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.LayerLifecycle,
                $"Created curve editor as child: {editorName}");

            return newEditor;
        }

        private void EnsureCurveEditorAtOrigin(Path3D editor = null)
        {
            editor ??= _externalCurveEditor;
            if (editor == null) return;

            if (editor.Transform != Transform3D.Identity)
            {
                DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.LayerLifecycle,
                    $"Resetting curve editor transform from {editor.Transform.Origin} to origin");
                editor.Transform = Transform3D.Identity;
            }
        }
        #endregion

        #region Connection Management
        internal void ConnectToCurveEditor()
        {
            if (!Engine.IsEditorHint()) return;
            if (_listeningToCurveEditor) return;

            if (_externalCurveEditor == null || !IsInstanceValid(_externalCurveEditor))
            {
                return;
            }

            if (_externalCurveEditor.Curve == null)
            {
                _externalCurveEditor.Curve = new Curve3D { BakeInterval = CURVE_BAKE_INTERVAL };
            }

            _externalCurveEditor.Curve.Changed += OnCurveChanged;
            _listeningToCurveEditor = true;

            InvalidateCaches();
            OnCurveChanged();

            DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.LayerLifecycle,
                $"Connected to curve editor: {_externalCurveEditor.Name}, " +
                $"Points: {Curve?.PointCount ?? 0}, " +
                $"GlobalPos: {_externalCurveEditor.GlobalPosition}");
        }

        internal void DisconnectFromCurveEditor()
        {
            if (!_listeningToCurveEditor) return;

            if (_externalCurveEditor != null && IsInstanceValid(_externalCurveEditor) &&
                _externalCurveEditor.Curve != null)
            {
                _externalCurveEditor.Curve.Changed -= OnCurveChanged;
            }

            _listeningToCurveEditor = false;
        }
        #endregion

        #region Curve Change Handler
        private void OnCurveChanged()
        {
            var curve = Curve;

            if (curve != null && curve.PointCount > 0)
            {
                var firstPoint = curve.GetPointPosition(0);
                var lastPoint = curve.PointCount > 1 ? curve.GetPointPosition(curve.PointCount - 1) : firstPoint;

                DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.LayerDetails,
                    $"Curve changed: {curve.PointCount} points, " +
                    $"First: ({firstPoint.X:F1}, {firstPoint.Y:F1}, {firstPoint.Z:F1}), " +
                    $"Last: ({lastPoint.X:F1}, {lastPoint.Y:F1}, {lastPoint.Z:F1})");
            }

            ComputeBoundsFromCurve(curve, out Vector2 minWorld, out Vector2 maxWorld);

            _maskWorldMin = minWorld;
            _maskWorldMax = maxWorld;
            _maskBoundsInitialized = true;

            Vector2 sizeF = maxWorld - minWorld;
            Vector2I newSize = new Vector2I(
                Mathf.Max(64, Mathf.CeilToInt(sizeF.X)),
                Mathf.Max(64, Mathf.CeilToInt(sizeF.Y))
            );

            InvalidateCaches();

            if (Size != newSize)
            {
                Size = newSize;
            }

            ForceDirty();

            // CRITICAL FIX: Curve change implies bounds change implies position dirty.
            // This ensures the manager detects overlaps with the NEW curve position.
            MarkPositionDirty();

            if (Engine.IsEditorHint())
            {
                UpdatePathVisualization();
            }

            DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.LayerDetails,
                $"Bounds updated: ({_maskWorldMin.X:F1},{_maskWorldMin.Y:F1})-({_maskWorldMax.X:F1},{_maskWorldMax.Y:F1}), Size={Size}");
        }
        #endregion

        #region Public API
        public void SelectCurveForEditing()
        {
            if (!Engine.IsEditorHint()) return;

            if (_externalCurveEditor == null || !IsInstanceValid(_externalCurveEditor))
            {
                EnsureCurveEditorExists();
            }

            if (_externalCurveEditor != null && IsInstanceValid(_externalCurveEditor))
            {
                EnsureCurveEditorAtOrigin();

                var selection = EditorInterface.Singleton?.GetSelection();
                if (selection != null)
                {
                    selection.Clear();
                    selection.AddNode(_externalCurveEditor);

                    DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.LayerLifecycle,
                        $"Selected curve editor for editing: {_externalCurveEditor.Name}, " +
                        $"GlobalPos: {_externalCurveEditor.GlobalPosition}");
                }
            }
        }

        public Path3D GetCurveEditorNode() => _externalCurveEditor;
        #endregion
    }
}