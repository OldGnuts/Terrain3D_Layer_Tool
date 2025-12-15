// /Editor/PathLayerInspector.Curves.cs
using System;
using Godot;
using Terrain3DTools.Layers.Path;
using Terrain3DTools.Editor.Utils;
using Terrain3DTools.Core;
using TokisanGames;

namespace Terrain3DTools.Editor
{
    public partial class PathLayerInspector
    {
        private VBoxContainer _pointListContainer;
        private Label _pointsLabel;
        private Label _lengthLabel;
        private Label _widthLabel;

        private void AddCurveSection()
        {
            CreateCollapsibleSection("Path Curve", true);
            var content = GetSectionContent("Path Curve");
            var layer = CurrentLayer;
            if (content == null || layer == null) return;

            // Stats
            var statsContainer = new GridContainer { Columns = 2 };
            statsContainer.AddThemeConstantOverride("h_separation", 16);
            statsContainer.AddThemeConstantOverride("v_separation", 4);

            _pointsLabel = EditorUIUtils.AddStatRow(statsContainer, "Points:", layer.PointCount.ToString());
            _lengthLabel = EditorUIUtils.AddStatRow(statsContainer, "Length:", $"{layer.PathLength:F1} units");
            _widthLabel = EditorUIUtils.AddStatRow(statsContainer, "Width:", $"{layer.ProfileTotalWidth:F1} units");

            content.AddChild(statsContainer);

            // Action Buttons
            var buttonContainer = new HBoxContainer();
            buttonContainer.AddThemeConstantOverride("separation", 8);

            var addPointButton = new Button { Text = "+ Point", TooltipText = "Add a new point at end" };
            addPointButton.Pressed += OnAddPointPressed;
            buttonContainer.AddChild(addPointButton);

            var clearButton = new Button { Text = "Clear All" };
            clearButton.Pressed += () => { CurrentLayer?.ClearPoints(); UpdateStatLabels(); };
            buttonContainer.AddChild(clearButton);

            var autoFitButton = new Button { Text = "Auto-Fit Size" };
            autoFitButton.Pressed += () => CurrentLayer?.AutoResizeToCurve();
            buttonContainer.AddChild(autoFitButton);

            content.AddChild(buttonContainer);

            // Quick Path
            var quickPathLabel = new Label { Text = "Quick Path Creation:" };
            quickPathLabel.AddThemeColorOverride("font_color", new Color(0.7f, 0.7f, 0.7f));
            content.AddChild(quickPathLabel);

            var quickPathContainer = new HBoxContainer();
            
            var straightBtn = new Button { Text = "Straight" };
            straightBtn.Pressed += () => CreateQuickPath(QuickPathType.Straight);
            
            var curveBtn = new Button { Text = "S-Curve" };
            curveBtn.Pressed += () => CreateQuickPath(QuickPathType.SCurve);
            
            var loopBtn = new Button { Text = "Loop" };
            loopBtn.Pressed += () => CreateQuickPath(QuickPathType.Loop);

            quickPathContainer.AddChild(straightBtn);
            quickPathContainer.AddChild(curveBtn);
            quickPathContainer.AddChild(loopBtn);
            content.AddChild(quickPathContainer);

            // Resolution
            EditorUIUtils.AddSeparator(content);
            var resContainer = new HBoxContainer();
            resContainer.AddChild(new Label { Text = "Resolution:", CustomMinimumSize = new Vector2(80, 0) });
            
            var resSpin = new SpinBox { MinValue = 8, MaxValue = 256, Value = layer.Resolution, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            resSpin.ValueChanged += (v) => layer.Resolution = (int)v;
            resContainer.AddChild(resSpin);

            var adaptiveCheck = new CheckBox { Text = "Adaptive", ButtonPressed = layer.AdaptiveResolution };
            adaptiveCheck.Toggled += (v) => layer.AdaptiveResolution = v;
            resContainer.AddChild(adaptiveCheck);
            
            content.AddChild(resContainer);
        }

        private void OnAddPointPressed()
        {
            var l = CurrentLayer;
            if (l == null) return;
            Vector3 newPos = l.GlobalPosition;
            
            if (l.PointCount > 0)
            {
                var last = l.GetPointWorldPosition(l.PointCount - 1);
                var dir = Vector3.Forward;
                if (l.PointCount > 1) 
                    dir = (last - l.GetPointWorldPosition(l.PointCount - 2)).Normalized();
                newPos = last + dir * 10f;
            }
            l.AddPoint(SnapToTerrain(newPos));
            UpdateStatLabels();
        }

        private void AddPathPointsSection()
        {
            CreateCollapsibleSection("Path Points", false);
            var content = GetSectionContent("Path Points");
            if (content == null) return;

            _pointListContainer = new VBoxContainer();
            RefreshPointList();

            var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(0, 120), SizeFlagsVertical = Control.SizeFlags.ExpandFill };
            scroll.AddChild(_pointListContainer);
            content.AddChild(scroll);

            var snapBtn = new Button { Text = "Snap All to Terrain" };
            snapBtn.Pressed += SnapAllPoints;
            content.AddChild(snapBtn);
        }

        private void SnapAllPoints()
        {
            var l = CurrentLayer;
            if (l == null) return;
            for(int i=0; i<l.PointCount; i++)
                l.SetPointWorldPosition(i, SnapToTerrain(l.GetPointWorldPosition(i)));
            RefreshPointList();
        }

        private Vector3 SnapToTerrain(Vector3 pos)
        {
            var layer = CurrentLayer;
            if (layer == null || !layer.IsInsideTree()) return pos;
            
            var manager = layer.GetTree().GetFirstNodeInGroup("terrain_layer_manager") as TerrainLayerManager;
            if (manager?.Terrain3DNode != null)
            {
                var t3d = Terrain3D.Bind(manager.Terrain3DNode);
                if (t3d != null)
                {
                    try {
                        // Terrain3D binding specific call
                        float h = (float)t3d.Data.GetHeight(pos); 
                        if (!float.IsNaN(h)) pos.Y = h;
                    } catch {}
                }
            }
            return pos;
        }

        private void RefreshPointList()
        {
            if (!IsInstanceValid(_pointListContainer)) return;
            foreach(Node c in _pointListContainer.GetChildren()) c.QueueFree();

            var layer = CurrentLayer;
            if (layer == null || layer.PointCount == 0) return;

            for (int i = 0; i < layer.PointCount; i++)
            {
                var row = new HBoxContainer();
                row.AddThemeConstantOverride("separation", 4);

                var indexLabel = new Label { Text = $"[{i}]", CustomMinimumSize = new Vector2(30, 0) };
                indexLabel.AddThemeColorOverride("font_color", new Color(0.6f, 0.8f, 1.0f));
                row.AddChild(indexLabel);
                
                Vector3 pos = layer.GetPointWorldPosition(i);
                var posLabel = new Label { 
                    Text = $"({pos.X:F1}, {pos.Y:F1}, {pos.Z:F1})", 
                    SizeFlagsHorizontal = Control.SizeFlags.ExpandFill 
                };
                row.AddChild(posLabel);
                
                var editBtn = new Button { Text = "✏️", TooltipText = "Edit Position" };
                int idx = i;
                editBtn.Pressed += () => ShowPointEditor(idx);
                row.AddChild(editBtn);

                var delBtn = new Button { Text = "🗑️", TooltipText = "Delete Point" };
                delBtn.Pressed += () => { layer.RemovePoint(idx); UpdateStatLabels(); };
                row.AddChild(delBtn);

                _pointListContainer.AddChild(row);
            }
        }

        private void ShowPointEditor(int index)
        {
            var layer = CurrentLayer;
            if (layer == null || index < 0 || index >= layer.PointCount) return;

            Vector3 currentPos = layer.GetPointWorldPosition(index);

            var window = new Window
            {
                Title = $"Edit Point [{index}]",
                Size = new Vector2I(300, 200),
                Exclusive = true,
                Transient = true
            };

            var vbox = new VBoxContainer();
            vbox.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            vbox.AddThemeConstantOverride("separation", 8);

            // X
            var xRow = new HBoxContainer();
            xRow.AddChild(new Label { Text = "X:", CustomMinimumSize = new Vector2(30, 0) });
            var xSpin = new SpinBox { MinValue = -10000, MaxValue = 10000, Step = 0.1, Value = currentPos.X, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            xRow.AddChild(xSpin);
            vbox.AddChild(xRow);

            // Y
            var yRow = new HBoxContainer();
            yRow.AddChild(new Label { Text = "Y:", CustomMinimumSize = new Vector2(30, 0) });
            var ySpin = new SpinBox { MinValue = -10000, MaxValue = 10000, Step = 0.1, Value = currentPos.Y, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            yRow.AddChild(ySpin);
            vbox.AddChild(yRow);

            // Z
            var zRow = new HBoxContainer();
            zRow.AddChild(new Label { Text = "Z:", CustomMinimumSize = new Vector2(30, 0) });
            var zSpin = new SpinBox { MinValue = -10000, MaxValue = 10000, Step = 0.1, Value = currentPos.Z, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            zRow.AddChild(zSpin);
            vbox.AddChild(zRow);

            // Buttons
            var buttonRow = new HBoxContainer();
            var spacer = new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            buttonRow.AddChild(spacer);

            var cancelButton = new Button { Text = "Cancel" };
            cancelButton.Pressed += () => { window.Hide(); window.QueueFree(); };
            buttonRow.AddChild(cancelButton);

            var okButton = new Button { Text = "OK" };
            okButton.Pressed += () => {
                if (layer != null && index < layer.PointCount) {
                    layer.SetPointWorldPosition(index, new Vector3((float)xSpin.Value, (float)ySpin.Value, (float)zSpin.Value));
                    UpdateStatLabels();
                }
                window.Hide();
                window.QueueFree();
            };
            buttonRow.AddChild(okButton);
            vbox.AddChild(buttonRow);
            window.AddChild(vbox);

            EditorInterface.Singleton.GetBaseControl().AddChild(window);
            window.PopupCentered();
        }

        private void CreateQuickPath(QuickPathType type)
        {
            var l = CurrentLayer;
            if (l == null) return;
            l.ClearPoints();
            Vector3 origin = SnapToTerrain(l.GlobalPosition);
            
            switch (type)
            {
                case QuickPathType.Straight:
                    l.AddPoint(origin);
                    l.AddPoint(SnapToTerrain(origin + Vector3.Forward * 50f));
                    break;
                case QuickPathType.SCurve:
                    l.AddPoint(origin);
                    l.AddPoint(SnapToTerrain(origin + new Vector3(15f, 0f, 20f)));
                    l.AddPoint(SnapToTerrain(origin + new Vector3(-15f, 0f, 40f)));
                    l.AddPoint(SnapToTerrain(origin + new Vector3(0f, 0f, 60f)));
                    break;
                case QuickPathType.Loop:
                    float r = 20f;
                    l.AddPoint(SnapToTerrain(origin + new Vector3(0f, 0f, -r)));
                    l.AddPoint(SnapToTerrain(origin + new Vector3(r, 0f, 0f)));
                    l.AddPoint(SnapToTerrain(origin + new Vector3(0f, 0f, r)));
                    l.AddPoint(SnapToTerrain(origin + new Vector3(-r, 0f, 0f)));
                    l.AddPoint(SnapToTerrain(origin + new Vector3(0f, 0f, -r)));
                    break;
            }
            l.AutoResizeToCurve();
            UpdateStatLabels();
        }

        private void RefreshCurveStats() => UpdateStatLabels();
        private void UpdateStatLabels()
        {
            var l = CurrentLayer;
            if (l == null) return;
            if (IsInstanceValid(_pointsLabel)) _pointsLabel.Text = l.PointCount.ToString();
            if (IsInstanceValid(_lengthLabel)) _lengthLabel.Text = $"{l.PathLength:F1} units";
            if (IsInstanceValid(_widthLabel)) _widthLabel.Text = $"{l.ProfileTotalWidth:F1} units";
            RefreshPointList();
        }
    }
}