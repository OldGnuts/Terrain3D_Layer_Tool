// /Editor/PathLayerInspector.Curves.cs
using System;
using System.Collections.Generic;
using Godot;
using Terrain3DTools.Layers.Path;
using Terrain3DTools.Editor.Utils;
using Terrain3DTools.Core;
using TokisanGames;

namespace Terrain3DTools.Editor
{
    public partial class PathLayerInspector
    {
        #region Fields
        private VBoxContainer _pointListContainer;
        private Label _pointsLabel;
        private Label _lengthLabel;
        private Label _widthLabel;
        private Label _boundsLabel;
        #endregion

        #region Curve Section
        private void AddCurveSection()
        {
            var layer = CurrentLayer;
            if (layer == null) return;

            // Build the help content
            string helpContent = EditorHelpTooltip.FormatHelpText(
                "Use these controls to create and edit the path shape.\nPoints can be added, moved, and deleted interactively in the 3D viewport.",
                new List<(string, string)>
                {
            ("Shift + Click", "Add point (extends from nearest endpoint)"),
            ("Alt + Click", "Insert point into nearest segment"),
            ("Click + Drag", "Move point along terrain"),
            ("Drag + Right Click", "Cancel current drag"),
            ("Right Click", "Delete point (on point)"),
            ("Delete / Backspace", "Delete hovered point"),
            ("Ctrl + S", "Apply smoothing to curve")
                },
                "Tip: Enable 'Auto-Smooth Curve' in the Smoothing section for automatic bezier curves."
            );

            // Create section with inline help button
            CreateCollapsibleSection("Path Curve", false, "Path Curve Editor Help", helpContent);
            var content = GetSectionContent("Path Curve");

            if (content == null) return;

            // Edit Curve Button (prominent)
            var editCurveButton = new Button
            {
                Text = "📐 Edit Curve (Select Path3D)",
                TooltipText = "Select the Path3D node to use Godot's native curve editing.\nThis allows editing bezier handles, adding/removing points, etc.",
                CustomMinimumSize = new Vector2(0, 32)
            };
            editCurveButton.Pressed += () => CurrentLayer?.SelectCurveForEditing();
            content.AddChild(editCurveButton);

            // Show current Path3D reference
            var path3DInfo = new HBoxContainer();
            path3DInfo.AddChild(new Label { Text = "Path3D:", CustomMinimumSize = new Vector2(60, 0) });

            var path3DName = layer.GetCurveEditorNode()?.Name ?? "(not created yet)";
            var path3DLabel = new Label
            {
                Text = path3DName,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
            };
            path3DLabel.AddThemeColorOverride("font_color", new Color(0.6f, 0.8f, 1.0f));
            path3DInfo.AddChild(path3DLabel);
            content.AddChild(path3DInfo);

            EditorUIUtils.AddSeparator(content);

            // Stats Grid
            var statsContainer = new GridContainer { Columns = 2 };
            statsContainer.AddThemeConstantOverride("h_separation", 16);
            statsContainer.AddThemeConstantOverride("v_separation", 4);

            _pointsLabel = EditorUIUtils.AddStatRow(statsContainer, "Points:", layer.PointCount.ToString());
            _lengthLabel = EditorUIUtils.AddStatRow(statsContainer, "Length:", $"{layer.PathLength:F1} units");
            _widthLabel = EditorUIUtils.AddStatRow(statsContainer, "Width:", $"{layer.ProfileTotalWidth:F1} units");

            var bounds = layer.GetWorldBounds();
            _boundsLabel = EditorUIUtils.AddStatRow(statsContainer, "Bounds:",
                $"({bounds.Min.X:F0},{bounds.Min.Y:F0}) - ({bounds.Max.X:F0},{bounds.Max.Y:F0})");

            content.AddChild(statsContainer);

            EditorUIUtils.AddSeparator(content);

            // Quick Actions
            var actionLabel = new Label { Text = "Quick Actions:" };
            actionLabel.AddThemeColorOverride("font_color", new Color(0.7f, 0.7f, 0.7f));
            content.AddChild(actionLabel);

            var buttonContainer = new HBoxContainer();
            buttonContainer.AddThemeConstantOverride("separation", 8);

            var addPointButton = new Button { Text = "+ Add Point", TooltipText = "Add a new point extending from the last point" };
            addPointButton.Pressed += OnAddPointPressed;
            buttonContainer.AddChild(addPointButton);

            var clearButton = new Button { Text = "Clear All", TooltipText = "Remove all curve points" };
            clearButton.Pressed += () =>
            {
                CurrentLayer?.ClearPoints();
                UpdateStatLabels();
            };
            buttonContainer.AddChild(clearButton);

            content.AddChild(buttonContainer);

            // Quick Path Creation
            EditorUIUtils.AddSeparator(content);

            var quickPathLabel = new Label { Text = "Quick Path Templates:" };
            quickPathLabel.AddThemeColorOverride("font_color", new Color(0.7f, 0.7f, 0.7f));
            content.AddChild(quickPathLabel);

            var quickPathContainer = new HBoxContainer();
            quickPathContainer.AddThemeConstantOverride("separation", 4);

            var straightBtn = new Button { Text = "Straight", TooltipText = "Create a straight 50-unit path" };
            straightBtn.Pressed += () => CreateQuickPath(QuickPathType.Straight);

            var curveBtn = new Button { Text = "S-Curve", TooltipText = "Create an S-shaped curve" };
            curveBtn.Pressed += () => CreateQuickPath(QuickPathType.SCurve);

            var loopBtn = new Button { Text = "Loop", TooltipText = "Create a closed loop" };
            loopBtn.Pressed += () => CreateQuickPath(QuickPathType.Loop);

            quickPathContainer.AddChild(straightBtn);
            quickPathContainer.AddChild(curveBtn);
            quickPathContainer.AddChild(loopBtn);
            content.AddChild(quickPathContainer);

            // Resolution Settings
            EditorUIUtils.AddSeparator(content);

            var resLabel = new Label { Text = "Sampling Resolution:" };
            resLabel.AddThemeColorOverride("font_color", new Color(0.7f, 0.7f, 0.7f));
            content.AddChild(resLabel);

            var resContainer = new HBoxContainer();
            resContainer.AddChild(new Label { Text = "Samples:", CustomMinimumSize = new Vector2(60, 0) });

            var resSpin = new SpinBox
            {
                MinValue = 8,
                MaxValue = 256,
                Value = layer.Resolution,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
            };
            resSpin.ValueChanged += (v) =>
            {
                if (CurrentLayer != null) CurrentLayer.Resolution = (int)v;
            };
            resContainer.AddChild(resSpin);

            var adaptiveCheck = new CheckBox
            {
                Text = "Adaptive",
                ButtonPressed = layer.AdaptiveResolution,
                TooltipText = "Automatically add more samples at sharp curves"
            };
            adaptiveCheck.Toggled += (v) =>
            {
                if (CurrentLayer != null) CurrentLayer.AdaptiveResolution = v;
            };
            resContainer.AddChild(adaptiveCheck);

            content.AddChild(resContainer);

            // Nested Path Points Section
            EditorUIUtils.AddSeparator(content, 8);
            AddPathPointsInline(content);
        }

        /// <summary>
        /// Adds the path points list as a nested inline collapsible within the Curve section.
        /// </summary>
        private void AddPathPointsInline(VBoxContainer parentContent)
        {
            var pointsContent = EditorUIUtils.CreateInlineCollapsible(
                parentContent,
                "Path Points List",
                false,
                "Path Points",
                _sectionExpanded
            );

            _pointListContainer = new VBoxContainer();
            RefreshPointListContent();

            var scroll = new ScrollContainer
            {
                CustomMinimumSize = new Vector2(0, EditorConstants.POINT_LIST_HEIGHT),
                SizeFlagsVertical = Control.SizeFlags.ExpandFill
            };
            scroll.AddChild(_pointListContainer);
            pointsContent.AddChild(scroll);

            var snapBtn = new Button
            {
                Text = "Snap All Points to Terrain",
                TooltipText = "Adjust Y coordinate of all points to match terrain height"
            };
            snapBtn.Pressed += SnapAllPoints;
            pointsContent.AddChild(snapBtn);
        }
        #endregion

        #region Point Operations
        private void OnAddPointPressed()
        {
            var layer = CurrentLayer;
            if (layer == null) return;

            Vector3 newPos;

            if (layer.PointCount > 0)
            {
                // Extend from last point in the direction of travel
                var last = layer.GetPointWorldPosition(layer.PointCount - 1);
                var dir = Vector3.Forward;

                if (layer.PointCount > 1)
                {
                    var prev = layer.GetPointWorldPosition(layer.PointCount - 2);
                    dir = (last - prev).Normalized();
                    if (dir.LengthSquared() < 0.01f)
                        dir = Vector3.Forward;
                }

                newPos = last + dir * 10f;
            }
            else
            {
                // First point - use center of current bounds or camera position
                var bounds = layer.GetWorldBounds();
                var center = (bounds.Min + bounds.Max) * 0.5f;
                newPos = new Vector3(center.X, 0, center.Y);

                // Try to get camera position for better placement
                var camera = EditorInterface.Singleton?.GetEditorViewport3D()?.GetCamera3D();
                if (camera != null)
                {
                    var camPos = camera.GlobalPosition;
                    var camDir = -camera.GlobalTransform.Basis.Z;
                    newPos = camPos + camDir * 20f;
                }
            }

            layer.AddPoint(SnapToTerrain(newPos));
            UpdateStatLabels();
        }

        private void SnapAllPoints()
        {
            var layer = CurrentLayer;
            if (layer == null) return;

            for (int i = 0; i < layer.PointCount; i++)
            {
                layer.SetPointWorldPosition(i, SnapToTerrain(layer.GetPointWorldPosition(i)));
            }

            RefreshPointListContent();
        }

        private Vector3 SnapToTerrain(Vector3 pos)
        {
            var layer = CurrentLayer;
            if (layer == null || !layer.IsInsideTree()) return pos;

            var manager = layer.GetTree().GetFirstNodeInGroup("terrain_layer_manager") as TerrainLayerManager;
            if (manager?.Terrain3DNode != null)
            {
                var t3d = Terrain3D.Bind(manager.Terrain3DNode);
                if (t3d?.Data != null)
                {
                    try
                    {
                        float h = (float)t3d.Data.GetHeight(pos);
                        if (!float.IsNaN(h))
                            pos.Y = h;
                    }
                    catch { /* Ignore terrain query errors */ }
                }
            }
            return pos;
        }

        private void RefreshPointList()
        {
            RefreshPointListContent();
        }

        private void RefreshPointListContent()
        {
            if (!IsInstanceValid(_pointListContainer)) return;

            foreach (Node c in _pointListContainer.GetChildren())
                c.QueueFree();

            var layer = CurrentLayer;
            if (layer == null || layer.PointCount == 0)
            {
                var emptyLabel = new Label
                {
                    Text = "No points defined. Click 'Edit Curve' or '+ Add Point' to begin.",
                    AutowrapMode = TextServer.AutowrapMode.Word
                };
                emptyLabel.AddThemeColorOverride("font_color", new Color(0.6f, 0.6f, 0.6f));
                _pointListContainer.AddChild(emptyLabel);
                return;
            }

            for (int i = 0; i < layer.PointCount; i++)
            {
                var row = new HBoxContainer();
                row.AddThemeConstantOverride("separation", 4);

                // Index label with color coding
                var indexLabel = new Label
                {
                    Text = $"[{i}]",
                    CustomMinimumSize = new Vector2(30, 0)
                };

                Color indexColor;
                if (i == 0)
                    indexColor = new Color(0.3f, 1.0f, 0.4f); // Green for start
                else if (i == layer.PointCount - 1)
                    indexColor = new Color(1.0f, 0.4f, 0.3f); // Red for end
                else
                    indexColor = new Color(0.6f, 0.8f, 1.0f); // Blue for middle

                indexLabel.AddThemeColorOverride("font_color", indexColor);
                row.AddChild(indexLabel);

                // Position
                Vector3 pos = layer.GetPointWorldPosition(i);
                var posLabel = new Label
                {
                    Text = $"({pos.X:F1}, {pos.Y:F1}, {pos.Z:F1})",
                    SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
                };
                row.AddChild(posLabel);

                // Edit button
                var editBtn = new Button { Text = "✏️", TooltipText = "Edit Position" };
                int idx = i;
                editBtn.Pressed += () => ShowPointEditor(idx);
                row.AddChild(editBtn);

                // Delete button
                var delBtn = new Button { Text = "🗑️", TooltipText = "Delete Point" };
                delBtn.Pressed += () =>
                {
                    layer.RemovePoint(idx);
                    UpdateStatLabels();
                };
                row.AddChild(delBtn);

                _pointListContainer.AddChild(row);
            }
        }

        private void ShowPointEditor(int index)
        {
            var layer = CurrentLayer;
            if (layer == null || index < 0 || index >= layer.PointCount) return;

            Vector3 currentPos = layer.GetPointWorldPosition(index);

            var window = CreateTrackedWindow($"Edit Point [{index}]", new Vector2I(300, 220));

            var vbox = new VBoxContainer();
            vbox.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            vbox.AddThemeConstantOverride("separation", 8);

            var margin = new MarginContainer();
            margin.AddThemeConstantOverride("margin_left", 8);
            margin.AddThemeConstantOverride("margin_right", 8);
            margin.AddThemeConstantOverride("margin_top", 8);
            margin.AddThemeConstantOverride("margin_bottom", 8);

            var innerVbox = new VBoxContainer();
            innerVbox.AddThemeConstantOverride("separation", 8);

            // Declare all spinboxes FIRST so they can be referenced in lambdas
            var xSpin = new SpinBox
            {
                MinValue = -10000,
                MaxValue = 10000,
                Step = 0.1,
                Value = currentPos.X,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
            };

            var ySpin = new SpinBox
            {
                MinValue = -10000,
                MaxValue = 10000,
                Step = 0.1,
                Value = currentPos.Y,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
            };

            var zSpin = new SpinBox
            {
                MinValue = -10000,
                MaxValue = 10000,
                Step = 0.1,
                Value = currentPos.Z,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
            };

            // X Row
            var xRow = new HBoxContainer();
            xRow.AddChild(new Label { Text = "X:", CustomMinimumSize = new Vector2(30, 0) });
            xRow.AddChild(xSpin);
            innerVbox.AddChild(xRow);

            // Y Row
            var yRow = new HBoxContainer();
            yRow.AddChild(new Label { Text = "Y:", CustomMinimumSize = new Vector2(30, 0) });
            yRow.AddChild(ySpin);

            var snapYBtn = new Button { Text = "Snap Y", TooltipText = "Set Y to terrain height" };
            snapYBtn.Pressed += () =>
            {
                var testPos = new Vector3((float)xSpin.Value, 0, (float)zSpin.Value);
                var snapped = SnapToTerrain(testPos);
                ySpin.Value = snapped.Y;
            };
            yRow.AddChild(snapYBtn);
            innerVbox.AddChild(yRow);

            // Z Row
            var zRow = new HBoxContainer();
            zRow.AddChild(new Label { Text = "Z:", CustomMinimumSize = new Vector2(30, 0) });
            zRow.AddChild(zSpin);
            innerVbox.AddChild(zRow);

            // Buttons
            var buttonRow = new HBoxContainer();
            var spacer = new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            buttonRow.AddChild(spacer);

            var cancelButton = new Button { Text = "Cancel" };
            cancelButton.Pressed += () =>
            {
                window.Hide();
                window.QueueFree();
                _windowTracker.Untrack(window);
            };
            buttonRow.AddChild(cancelButton);

            var okButton = new Button { Text = "OK" };
            okButton.Pressed += () =>
            {
                var l = CurrentLayer;
                if (l != null && index < l.PointCount)
                {
                    l.SetPointWorldPosition(index, new Vector3(
                        (float)xSpin.Value,
                        (float)ySpin.Value,
                        (float)zSpin.Value));
                    UpdateStatLabels();
                }
                window.Hide();
                window.QueueFree();
                _windowTracker.Untrack(window);
            };
            buttonRow.AddChild(okButton);
            innerVbox.AddChild(buttonRow);

            margin.AddChild(innerVbox);
            vbox.AddChild(margin);
            window.AddChild(vbox);
            window.PopupCentered();
        }
        #endregion

        #region Quick Path Creation
        private void CreateQuickPath(QuickPathType type)
        {
            var layer = CurrentLayer;
            if (layer == null) return;

            layer.ClearPoints();

            // Determine origin - use camera look point or world origin
            Vector3 origin = Vector3.Zero;

            var camera = EditorInterface.Singleton?.GetEditorViewport3D()?.GetCamera3D();
            if (camera != null)
            {
                var camPos = camera.GlobalPosition;
                var camDir = -camera.GlobalTransform.Basis.Z;
                // Project to ground plane
                if (Mathf.Abs(camDir.Y) > 0.01f)
                {
                    float t = -camPos.Y / camDir.Y;
                    if (t > 0 && t < 1000)
                    {
                        origin = camPos + camDir * t;
                    }
                }
                else
                {
                    origin = camPos + camDir * 30f;
                    origin.Y = 0;
                }
            }

            origin = SnapToTerrain(origin);

            switch (type)
            {
                case QuickPathType.Straight:
                    layer.AddPoint(origin);
                    layer.AddPoint(SnapToTerrain(origin + Vector3.Forward * 50f));
                    break;

                case QuickPathType.SCurve:
                    layer.AddPoint(origin);
                    layer.AddPoint(SnapToTerrain(origin + new Vector3(15f, 0f, 20f)));
                    layer.AddPoint(SnapToTerrain(origin + new Vector3(-15f, 0f, 40f)));
                    layer.AddPoint(SnapToTerrain(origin + new Vector3(0f, 0f, 60f)));
                    break;

                case QuickPathType.Loop:
                    float r = 20f;
                    layer.AddPoint(SnapToTerrain(origin + new Vector3(0f, 0f, -r)));
                    layer.AddPoint(SnapToTerrain(origin + new Vector3(r, 0f, 0f)));
                    layer.AddPoint(SnapToTerrain(origin + new Vector3(0f, 0f, r)));
                    layer.AddPoint(SnapToTerrain(origin + new Vector3(-r, 0f, 0f)));
                    layer.AddPoint(SnapToTerrain(origin + new Vector3(0f, 0f, -r)));
                    break;
            }

            UpdateStatLabels();
        }
        #endregion

        #region Stats Updates
        private void RefreshCurveStats() => UpdateStatLabels();

        private void UpdateStatLabels()
        {
            var layer = CurrentLayer;
            if (layer == null) return;

            if (IsInstanceValid(_pointsLabel))
                _pointsLabel.Text = layer.PointCount.ToString();

            if (IsInstanceValid(_lengthLabel))
                _lengthLabel.Text = $"{layer.PathLength:F1} units";

            if (IsInstanceValid(_widthLabel))
                _widthLabel.Text = $"{layer.ProfileTotalWidth:F1} units";

            if (IsInstanceValid(_boundsLabel))
            {
                var bounds = layer.GetWorldBounds();
                _boundsLabel.Text = $"({bounds.Min.X:F0},{bounds.Min.Y:F0}) - ({bounds.Max.X:F0},{bounds.Max.Y:F0})";
            }

            RefreshPointList();
        }
        #endregion
    }
}