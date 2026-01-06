// /Editor/PathLayerInspector.Smoothing.cs
using Godot;
using Terrain3DTools.Editor.Utils;

namespace Terrain3DTools.Editor
{
    public partial class PathLayerInspector
    {
        #region Smoothing Fields
        private CheckBox _autoSmoothCheck;
        private HSlider _autoSmoothTensionSlider;
        private Label _autoSmoothTensionValue;
        private CheckBox _smoothCornersCheck;
        private HSlider _cornerSmoothingSlider;
        private Label _cornerSmoothingValue;
        #endregion

        #region Smoothing Section
        private void AddSmoothingSection()
        {
            CreateCollapsibleSection("Smoothing", false);
            var content = GetSectionContent("Smoothing");
            var layer = CurrentLayer;

            if (content == null || layer == null) return;

            // ═══════════════════════════════════════════════════════════════
            // CURVE SMOOTHING (Bezier Handles)
            // ═══════════════════════════════════════════════════════════════

            var curveLabel = new Label { Text = "Curve Shape (Bezier Handles)" };
            curveLabel.AddThemeColorOverride("font_color", new Color(0.9f, 0.9f, 0.9f));
            content.AddChild(curveLabel);

            var curveInfo = new Label
            {
                Text = "Control how smoothly the path curves between control points.",
                AutowrapMode = TextServer.AutowrapMode.Word
            };
            curveInfo.AddThemeColorOverride("font_color", new Color(0.6f, 0.6f, 0.6f));
            curveInfo.AddThemeFontSizeOverride("font_size", 11);
            content.AddChild(curveInfo);

            EditorUIUtils.AddSeparator(content, 4);

            // Auto-Smooth Checkbox
            _autoSmoothCheck = new CheckBox
            {
                Text = "Auto-Smooth Curve",
                ButtonPressed = layer.AutoSmoothCurve,
                TooltipText = "Automatically smooth the curve whenever points are added or moved"
            };
            _autoSmoothCheck.Toggled += OnAutoSmoothToggled;
            content.AddChild(_autoSmoothCheck);

            // Tension Slider
            var tensionRow = new HBoxContainer();
            tensionRow.AddChild(new Label
            {
                Text = "Tension:",
                CustomMinimumSize = new Vector2(EditorConstants.LABEL_MIN_WIDTH, 0),
                TooltipText = "0 = Very smooth curves, 1 = Sharp corners"
            });

            _autoSmoothTensionSlider = new HSlider
            {
                MinValue = 0.0,
                MaxValue = 1.0,
                Step = 0.05,
                Value = layer.AutoSmoothTension,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                TooltipText = "Controls curve smoothness when auto-smooth is enabled"
            };
            _autoSmoothTensionSlider.ValueChanged += OnAutoSmoothTensionChanged;
            tensionRow.AddChild(_autoSmoothTensionSlider);

            _autoSmoothTensionValue = new Label
            {
                Text = layer.AutoSmoothTension.ToString("F2"),
                CustomMinimumSize = new Vector2(40, 0),
                HorizontalAlignment = HorizontalAlignment.Right
            };
            tensionRow.AddChild(_autoSmoothTensionValue);

            content.AddChild(tensionRow);

            EditorUIUtils.AddSeparator(content, 4);

            // Manual Action Buttons
            var manualLabel = new Label { Text = "Manual Actions:" };
            manualLabel.AddThemeColorOverride("font_color", new Color(0.7f, 0.7f, 0.7f));
            content.AddChild(manualLabel);

            var curveButtonRow = new HBoxContainer();
            curveButtonRow.AddThemeConstantOverride("separation", 8);

            var smoothNowBtn = new Button
            {
                Text = "🔄 Smooth Now",
                TooltipText = "Apply smoothing once with current tension setting",
                CustomMinimumSize = new Vector2(0, 28)
            };
            smoothNowBtn.Pressed += () =>
            {
                var l = CurrentLayer;
                if (l != null)
                {
                    l.ApplyAutoSmooth(l.AutoSmoothTension);
                }
            };
            curveButtonRow.AddChild(smoothNowBtn);

            var clearHandlesBtn = new Button
            {
                Text = "📐 Make Sharp",
                TooltipText = "Remove all bezier handles for sharp corners\n(Also disables auto-smooth)",
                CustomMinimumSize = new Vector2(0, 28)
            };
            clearHandlesBtn.Pressed += OnMakeSharpPressed;
            curveButtonRow.AddChild(clearHandlesBtn);

            var subdivideBtn = new Button
            {
                Text = "➕ Subdivide",
                TooltipText = "Add midpoints between all existing points",
                CustomMinimumSize = new Vector2(0, 28)
            };
            subdivideBtn.Pressed += () =>
            {
                CurrentLayer?.SubdivideCurve();
                UpdateStatLabels();
            };
            curveButtonRow.AddChild(subdivideBtn);

            content.AddChild(curveButtonRow);

            // Quick Tension Presets
            var presetRow = new HBoxContainer();
            presetRow.AddChild(new Label
            {
                Text = "Presets:",
                CustomMinimumSize = new Vector2(EditorConstants.LABEL_MIN_WIDTH, 0)
            });

            var presetContainer = new HBoxContainer();
            presetContainer.AddThemeConstantOverride("separation", 4);

            var sharpBtn = new Button { Text = "Sharp", TooltipText = "Tension = 1.0 (linear)" };
            sharpBtn.Pressed += () => SetTensionPreset(1.0f);
            presetContainer.AddChild(sharpBtn);

            var gentleBtn = new Button { Text = "Gentle", TooltipText = "Tension = 0.7" };
            gentleBtn.Pressed += () => SetTensionPreset(0.7f);
            presetContainer.AddChild(gentleBtn);

            var smoothBtn = new Button { Text = "Smooth", TooltipText = "Tension = 0.4" };
            smoothBtn.Pressed += () => SetTensionPreset(0.4f);
            presetContainer.AddChild(smoothBtn);

            var verySmooth = new Button { Text = "Very Smooth", TooltipText = "Tension = 0.1" };
            verySmooth.Pressed += () => SetTensionPreset(0.1f);
            presetContainer.AddChild(verySmooth);

            presetRow.AddChild(presetContainer);
            content.AddChild(presetRow);

            EditorUIUtils.AddSeparator(content, 12);

            // ═══════════════════════════════════════════════════════════════
            // SDF CORNER SMOOTHING (GPU-based)
            // ═══════════════════════════════════════════════════════════════

            var sdfLabel = new Label { Text = "GPU Corner Smoothing" };
            sdfLabel.AddThemeColorOverride("font_color", new Color(0.9f, 0.9f, 0.9f));
            content.AddChild(sdfLabel);

            var sdfInfo = new Label
            {
                Text = "Additional smoothing applied during terrain generation.\nAffects the SDF distance field calculation.",
                AutowrapMode = TextServer.AutowrapMode.Word
            };
            sdfInfo.AddThemeColorOverride("font_color", new Color(0.6f, 0.6f, 0.6f));
            sdfInfo.AddThemeFontSizeOverride("font_size", 11);
            content.AddChild(sdfInfo);

            EditorUIUtils.AddSeparator(content, 4);

            // Smooth Corners Checkbox
            _smoothCornersCheck = new CheckBox
            {
                Text = "Enable GPU Corner Smoothing",
                ButtonPressed = layer.SmoothCorners,
                TooltipText = "Apply additional smoothing in the GPU shader during terrain generation"
            };
            _smoothCornersCheck.Toggled += OnSmoothCornersToggled;
            content.AddChild(_smoothCornersCheck);

            // Corner Smoothing Radius
            var smoothingRow = new HBoxContainer();
            smoothingRow.AddChild(new Label
            {
                Text = "Radius:",
                CustomMinimumSize = new Vector2(EditorConstants.LABEL_MIN_WIDTH, 0),
                TooltipText = "Smoothing radius in world units"
            });

            _cornerSmoothingSlider = new HSlider
            {
                MinValue = 0.0,
                MaxValue = 5.0,
                Step = 0.1,
                Value = layer.CornerSmoothing,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
            };
            _cornerSmoothingSlider.ValueChanged += OnCornerSmoothingChanged;
            smoothingRow.AddChild(_cornerSmoothingSlider);

            _cornerSmoothingValue = new Label
            {
                Text = layer.CornerSmoothing.ToString("F1"),
                CustomMinimumSize = new Vector2(40, 0),
                HorizontalAlignment = HorizontalAlignment.Right
            };
            smoothingRow.AddChild(_cornerSmoothingValue);

            content.AddChild(smoothingRow);

            // Update enabled states
            UpdateAutoSmoothControlsEnabled();
            UpdateSmoothingControlsEnabled();
        }
        #endregion

        #region Smoothing Event Handlers
        private void OnAutoSmoothToggled(bool enabled)
        {
            var layer = CurrentLayer;
            if (layer == null) return;

            layer.AutoSmoothCurve = enabled;
            UpdateAutoSmoothControlsEnabled();
        }

        private void OnAutoSmoothTensionChanged(double value)
        {
            var layer = CurrentLayer;
            if (layer == null) return;

            layer.AutoSmoothTension = (float)value;

            if (IsInstanceValid(_autoSmoothTensionValue))
            {
                _autoSmoothTensionValue.Text = value.ToString("F2");
            }
        }

        private void SetTensionPreset(float tension)
        {
            var layer = CurrentLayer;
            if (layer == null) return;

            // Update slider
            if (IsInstanceValid(_autoSmoothTensionSlider))
            {
                _autoSmoothTensionSlider.Value = tension;
            }

            // Apply smoothing now
            layer.AutoSmoothTension = tension;
            layer.ApplyAutoSmooth(tension);

            // Enable auto-smooth if it wasn't already
            if (!layer.AutoSmoothCurve)
            {
                layer.AutoSmoothCurve = true;
                if (IsInstanceValid(_autoSmoothCheck))
                {
                    _autoSmoothCheck.ButtonPressed = true;
                }
                UpdateAutoSmoothControlsEnabled();
            }
        }

        private void OnMakeSharpPressed()
        {
            var layer = CurrentLayer;
            if (layer == null) return;

            // Disable auto-smooth first
            layer.AutoSmoothCurve = false;
            if (IsInstanceValid(_autoSmoothCheck))
            {
                _autoSmoothCheck.ButtonPressed = false;
            }
            UpdateAutoSmoothControlsEnabled();

            // Then clear handles
            layer.ClearCurveHandles();
        }

        private void UpdateAutoSmoothControlsEnabled()
        {
            var layer = CurrentLayer;
            bool enabled = layer != null && layer.AutoSmoothCurve;

            if (IsInstanceValid(_autoSmoothTensionSlider))
            {
                // Slider is always editable, but visual feedback when auto is on
                _autoSmoothTensionSlider.Modulate = enabled ? Colors.White : new Color(1, 1, 1, 0.7f);
            }
        }

        private void OnSmoothCornersToggled(bool enabled)
        {
            var layer = CurrentLayer;
            if (layer == null) return;

            layer.SmoothCorners = enabled;
            UpdateSmoothingControlsEnabled();
        }

        private void OnCornerSmoothingChanged(double value)
        {
            var layer = CurrentLayer;
            if (layer == null) return;

            layer.CornerSmoothing = (float)value;

            if (IsInstanceValid(_cornerSmoothingValue))
            {
                _cornerSmoothingValue.Text = value.ToString("F1");
            }
        }

        private void UpdateSmoothingControlsEnabled()
        {
            var layer = CurrentLayer;
            bool enabled = layer != null && layer.SmoothCorners;

            if (IsInstanceValid(_cornerSmoothingSlider))
            {
                _cornerSmoothingSlider.Editable = enabled;
                _cornerSmoothingSlider.Modulate = enabled ? Colors.White : new Color(1, 1, 1, 0.5f);
            }

            if (IsInstanceValid(_cornerSmoothingValue))
            {
                _cornerSmoothingValue.Modulate = enabled ? Colors.White : new Color(1, 1, 1, 0.5f);
            }
        }
        #endregion
    }
}