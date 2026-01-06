// /Editor/PathLayerInspector.Falloff.cs
using System;
using Godot;
using Terrain3DTools.Layers;
using Terrain3DTools.Editor.Utils;
using Terrain3DTools.Utils;

namespace Terrain3DTools.Editor
{
    public partial class PathLayerInspector
    {
        #region Falloff Fields
        private HSlider _falloffStrengthSlider;
        private Label _falloffStrengthValue;
        private OptionButton _falloffModeDropdown;
        private CurveMiniPreview _falloffCurvePreview;
        #endregion

        #region Falloff Section
        private void AddFalloffSection()
        {
            CreateCollapsibleSection("Global Falloff", false);
            var content = GetSectionContent("Global Falloff");
            var layer = CurrentLayer;

            if (content == null || layer == null) return;

            // Info label
            var infoLabel = new Label
            {
                Text = "Global falloff is applied after zone-based influence.\nUse this for additional edge softening or to blend the entire path with terrain.",
                AutowrapMode = TextServer.AutowrapMode.Word
            };
            infoLabel.AddThemeColorOverride("font_color", new Color(0.6f, 0.6f, 0.6f));
            infoLabel.AddThemeFontSizeOverride("font_size", 11);
            content.AddChild(infoLabel);

            EditorUIUtils.AddSeparator(content, 4);

            // Falloff Mode
            var modeRow = new HBoxContainer();
            modeRow.AddChild(new Label
            {
                Text = "Mode:",
                CustomMinimumSize = new Vector2(EditorConstants.LABEL_MIN_WIDTH, 0)
            });

            _falloffModeDropdown = new OptionButton { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            _falloffModeDropdown.AddItem("None", (int)FalloffType.None);
            _falloffModeDropdown.AddItem("Linear", (int)FalloffType.Linear);
            _falloffModeDropdown.AddItem("Circular", (int)FalloffType.Circular);
            _falloffModeDropdown.Selected = (int)layer.FalloffMode;
            _falloffModeDropdown.ItemSelected += OnFalloffModeChanged;
            modeRow.AddChild(_falloffModeDropdown);

            content.AddChild(modeRow);

            // Falloff Strength
            var strengthRow = new HBoxContainer();
            strengthRow.AddChild(new Label
            {
                Text = "Strength:",
                CustomMinimumSize = new Vector2(EditorConstants.LABEL_MIN_WIDTH, 0)
            });

            _falloffStrengthSlider = new HSlider
            {
                MinValue = 0.0,
                MaxValue = 1.0,
                Step = 0.01,
                Value = layer.FalloffStrength,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
            };
            _falloffStrengthSlider.ValueChanged += OnFalloffStrengthChanged;
            strengthRow.AddChild(_falloffStrengthSlider);

            _falloffStrengthValue = new Label
            {
                Text = layer.FalloffStrength.ToString("F2"),
                CustomMinimumSize = new Vector2(40, 0),
                HorizontalAlignment = HorizontalAlignment.Right
            };
            strengthRow.AddChild(_falloffStrengthValue);

            content.AddChild(strengthRow);

            // Falloff Curve
            var curveRow = new HBoxContainer();
            curveRow.AddChild(new Label
            {
                Text = "Curve:",
                CustomMinimumSize = new Vector2(EditorConstants.LABEL_MIN_WIDTH, 0)
            });

            _falloffCurvePreview = new CurveMiniPreview(layer.FalloffCurve)
            {
                CustomMinimumSize = new Vector2(100, EditorConstants.CURVE_PREVIEW_HEIGHT),
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
            };
            curveRow.AddChild(_falloffCurvePreview);

            var editCurveBtn = new Button { Text = "Edit..." };
            editCurveBtn.Pressed += OnEditFalloffCurvePressed;
            curveRow.AddChild(editCurveBtn);

            content.AddChild(curveRow);

            // Curve Presets
            var presetsRow = new HBoxContainer();
            presetsRow.AddChild(new Label
            {
                Text = "Presets:",
                CustomMinimumSize = new Vector2(EditorConstants.LABEL_MIN_WIDTH, 0)
            });

            var presetContainer = new HBoxContainer();
            presetContainer.AddThemeConstantOverride("separation", 4);

            var linearBtn = new Button { Text = "Linear", TooltipText = "Linear falloff from center to edge" };
            linearBtn.Pressed += () => SetFalloffCurvePreset("Linear");
            presetContainer.AddChild(linearBtn);

            var smoothBtn = new Button { Text = "Smooth", TooltipText = "Smooth ease-out falloff" };
            smoothBtn.Pressed += () => SetFalloffCurvePreset("EaseOut");
            presetContainer.AddChild(smoothBtn);

            var sharpBtn = new Button { Text = "Sharp", TooltipText = "Sharp falloff near edges" };
            sharpBtn.Pressed += () => SetFalloffCurvePreset("EaseIn");
            presetContainer.AddChild(sharpBtn);

            var bellBtn = new Button { Text = "Bell", TooltipText = "Bell curve - soft center and edges" };
            bellBtn.Pressed += () => SetFalloffCurvePreset("Bell");
            presetContainer.AddChild(bellBtn);

            presetsRow.AddChild(presetContainer);
            content.AddChild(presetsRow);

            // Update enabled state based on mode
            UpdateFalloffControlsEnabled();
        }
        #endregion

        #region Falloff Event Handlers
        private void OnFalloffModeChanged(long index)
        {
            var layer = CurrentLayer;
            if (layer == null) return;

            layer.FalloffMode = (FalloffType)(int)index;
            UpdateFalloffControlsEnabled();
        }

        private void OnFalloffStrengthChanged(double value)
        {
            var layer = CurrentLayer;
            if (layer == null) return;

            layer.FalloffStrength = (float)value;

            if (IsInstanceValid(_falloffStrengthValue))
            {
                _falloffStrengthValue.Text = value.ToString("F2");
            }
        }

        private void OnEditFalloffCurvePressed()
        {
            var layer = CurrentLayer;
            if (layer == null) return;

            EditorUIUtils.ShowCurveEditorWindow(
                "Falloff Curve",
                layer.FalloffCurve,
                (newCurve) =>
                {
                    layer.FalloffCurve = newCurve;
                },
                _falloffCurvePreview,
                _windowTracker
            );
        }

        private void SetFalloffCurvePreset(string presetName)
        {
            var layer = CurrentLayer;
            if (layer == null) return;

            var newCurve = CurveUtils.GetPresetByName(presetName);
            layer.FalloffCurve = newCurve;

            if (IsInstanceValid(_falloffCurvePreview))
            {
                _falloffCurvePreview.SetCurve(newCurve);
            }
        }

        private void UpdateFalloffControlsEnabled()
        {
            var layer = CurrentLayer;
            bool enabled = layer != null && layer.FalloffMode != FalloffType.None;

            if (IsInstanceValid(_falloffStrengthSlider))
            {
                _falloffStrengthSlider.Editable = enabled;
                _falloffStrengthSlider.Modulate = enabled ? Colors.White : new Color(1, 1, 1, 0.5f);
            }

            if (IsInstanceValid(_falloffCurvePreview))
            {
                _falloffCurvePreview.Modulate = enabled ? Colors.White : new Color(1, 1, 1, 0.5f);
            }
        }
        #endregion
    }
}