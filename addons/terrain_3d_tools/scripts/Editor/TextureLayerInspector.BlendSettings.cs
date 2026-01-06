// /Editor/TextureLayerInspector.BlendSettings.cs
using System;
using System.Collections.Generic;
using Godot;
using Terrain3DTools.Layers;
using Terrain3DTools.Editor.Utils;

namespace Terrain3DTools.Editor
{
    public partial class TextureLayerInspector
    {
        #region UI References - Blend Settings

        private OptionButton _blendModeDropdown;

        #endregion

        #region UI References - Noise

        private VBoxContainer _noiseSettingsContainer;
        private VBoxContainer _customNoiseTextureContainer;
        private TextureRect _noiseTexturePreview;
        private VBoxContainer _edgeAwareContainer;

        #endregion

        #region UI References - Smoothing

        private VBoxContainer _smoothingSettingsContainer;

        #endregion

        #region Build Blend Settings Section

        partial void BuildBlendSettingsSection()
        {
            var layer = CurrentLayer;
            if (layer == null) return;

            // Header row with help
            var headerRow = new HBoxContainer();
            headerRow.AddThemeConstantOverride("separation", 0);

            bool isExpanded = _sectionExpanded.TryGetValue("Blend Settings", out var expanded) ? expanded : true;

            var headerButton = new Button
            {
                Text = (isExpanded ? "▼ " : "▶ ") + "Blend Settings",
                Flat = true,
                Alignment = HorizontalAlignment.Left,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
            };
            headerButton.AddThemeColorOverride("font_color", new Color(0.9f, 0.9f, 0.9f));
            headerButton.AddThemeFontSizeOverride("font_size", 14);
            headerRow.AddChild(headerButton);

            var helpBtn = EditorHelpTooltip.CreateHelpButtonStyled(
                "Blend Settings",
                EditorHelpTooltip.FormatHelpText(
                    "Controls how this layer's textures blend with existing terrain data.",
                    new List<(string, string)>
                    {
                        ("Replace", "Overwrites existing textures based on mask strength"),
                        ("Strengthen", "Only affects textures already present"),
                        ("Max", "Takes the maximum of current and new values"),
                        ("Additive", "Adds to existing values (clamped)"),
                        ("Blend Strength", "Overall intensity (0 = none, 1 = full)")
                    },
                    "In Gradient Mode, blend mode is ignored - zone-based blending is used instead."
                )
            );
            headerRow.AddChild(helpBtn);
            _mainContainer.AddChild(headerRow);

            var separator = new HSeparator();
            separator.AddThemeConstantOverride("separation", 2);
            _mainContainer.AddChild(separator);

            var section = new VBoxContainer();
            section.AddThemeConstantOverride("separation", 4);
            section.Visible = isExpanded;
            _sectionContents["Blend Settings"] = section;

            headerButton.Pressed += () =>
            {
                bool newState = !section.Visible;
                section.Visible = newState;
                headerButton.Text = (newState ? "▼ " : "▶ ") + "Blend Settings";
                _sectionExpanded["Blend Settings"] = newState;
            };

            _mainContainer.AddChild(section);

            // === BLEND MODE (non-gradient only) ===
            if (!layer.GradientModeEnabled)
            {
                var modeRow = new HBoxContainer();
                modeRow.AddThemeConstantOverride("separation", 8);

                modeRow.AddChild(new Label
                {
                    Text = "Mode:",
                    CustomMinimumSize = new Vector2(EditorConstants.LABEL_MIN_WIDTH, 0)
                });

                _blendModeDropdown = new OptionButton
                {
                    SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
                };
                _blendModeDropdown.AddItem("Replace", (int)TextureBlendMode.Replace);
                _blendModeDropdown.AddItem("Strengthen", (int)TextureBlendMode.Strengthen);
                _blendModeDropdown.AddItem("Max", (int)TextureBlendMode.Max);
                _blendModeDropdown.AddItem("Additive", (int)TextureBlendMode.Additive);
                _blendModeDropdown.Selected = (int)layer.BlendMode;
                _blendModeDropdown.ItemSelected += OnBlendModeChanged;

                modeRow.AddChild(_blendModeDropdown);
                section.AddChild(modeRow);

                _binder.AddSeparator(section, 4);
            }
            else
            {
                // Show info that blend mode is ignored in gradient mode
                var infoLabel = new Label
                {
                    Text = "ℹ️ Gradient mode uses zone-based blending",
                    Modulate = new Color(0.6f, 0.6f, 0.6f)
                };
                infoLabel.AddThemeFontSizeOverride("font_size", 11);
                section.AddChild(infoLabel);
                _binder.AddSeparator(section, 4);
            }

            // === BLEND STRENGTH ===
            _binder.AddSlider(
                section,
                "Strength",
                () => layer.BlendStrength,
                v => layer.BlendStrength = v,
                0f, 1f, 0.01f, "F2",
                tooltip: "Overall intensity of the layer's texture effect",
                leftLabel: "None",
                rightLabel: "Full"
            );
        }

        private void OnBlendModeChanged(long index)
        {
            var layer = CurrentLayer;
            if (layer == null) return;
            layer.BlendMode = (TextureBlendMode)(int)index;
        }

        #endregion

        #region Build Noise Section

        partial void BuildNoiseSection()
        {
            var layer = CurrentLayer;
            if (layer == null) return;

            var headerRow = new HBoxContainer();
            headerRow.AddThemeConstantOverride("separation", 0);

            bool isExpanded = _sectionExpanded.TryGetValue("Noise & Variation", out var expanded) && expanded;

            var headerButton = new Button
            {
                Text = (isExpanded ? "▼ " : "▶ ") + "Noise & Variation",
                Flat = true,
                Alignment = HorizontalAlignment.Left,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
            };
            headerButton.AddThemeColorOverride("font_color", new Color(0.9f, 0.9f, 0.9f));
            headerButton.AddThemeFontSizeOverride("font_size", 14);
            headerRow.AddChild(headerButton);

            var helpBtn = EditorHelpTooltip.CreateHelpButtonStyled(
                "Noise & Variation",
                EditorHelpTooltip.FormatHelpText(
                    "Adds organic variation to texture blending.\n\n" +
                    "In zone-based mode, noise affects BLEND VALUES only, not zone selection. " +
                    "This creates natural variation without causing texture flip-flopping.",
                    new List<(string, string)>
                    {
                        ("Amount", "Strength of blend variation (0 = none, 0.5 = max)"),
                        ("Scale", "World-space frequency (smaller = larger features)"),
                        ("Type", "Value (blocky), Perlin (smooth), Simplex (organic)"),
                        ("Edge-Aware", "Concentrate noise at zone transitions")
                    },
                    "Tip: Use moderate Amount (0.1-0.2) with Edge-Aware for natural transitions."
                )
            );
            headerRow.AddChild(helpBtn);
            _mainContainer.AddChild(headerRow);

            var separator = new HSeparator();
            separator.AddThemeConstantOverride("separation", 2);
            _mainContainer.AddChild(separator);

            var section = new VBoxContainer();
            section.AddThemeConstantOverride("separation", 4);
            section.Visible = isExpanded;
            _sectionContents["Noise & Variation"] = section;

            headerButton.Pressed += () =>
            {
                bool newState = !section.Visible;
                section.Visible = newState;
                headerButton.Text = (newState ? "▼ " : "▶ ") + "Noise & Variation";
                _sectionExpanded["Noise & Variation"] = newState;
            };

            _mainContainer.AddChild(section);

            // === ENABLE NOISE ===
            _binder.AddCheckBox(
                section,
                "Enable Noise",
                () => layer.EnableNoise,
                v => layer.EnableNoise = v,
                "Add procedural noise to blend values (not zone selection)",
                OnNoiseEnabledChanged
            );

            // === NOISE SETTINGS CONTAINER ===
            _noiseSettingsContainer = new VBoxContainer();
            _noiseSettingsContainer.AddThemeConstantOverride("separation", 8);
            _noiseSettingsContainer.Visible = layer.EnableNoise;
            section.AddChild(_noiseSettingsContainer);

            if (layer.EnableNoise)
            {
                BuildNoiseSettingsContent();
            }
        }

        private void BuildNoiseSettingsContent()
        {
            var layer = CurrentLayer;
            if (layer == null || _noiseSettingsContainer == null) return;

            _noiseSettingsContainer.QueueFreeChildren();

            // === NOISE AMOUNT ===
            _binder.AddSlider(
                _noiseSettingsContainer,
                "Amount",
                () => layer.NoiseAmount,
                v => layer.NoiseAmount = v,
                0f, 0.5f, 0.01f, "F2",
                leftLabel: "Subtle",
                rightLabel: "Strong"
            );

            // === NOISE SCALE ===
            _binder.AddSlider(
                _noiseSettingsContainer,
                "Scale",
                () => layer.NoiseScale,
                v => layer.NoiseScale = v,
                0.001f, 0.2f, 0.001f, "F3",
                leftLabel: "Large",
                rightLabel: "Small"
            );

            // === NOISE TYPE ===
            _binder.AddEnumDropdown(
                _noiseSettingsContainer,
                "Type",
                () => layer.NoiseType,
                v => layer.NoiseType = v
            );

            // === NOISE SEED ===
            _binder.AddSpinBoxWithRandomize(
                _noiseSettingsContainer,
                "Seed",
                () => layer.NoiseSeed,
                v => layer.NoiseSeed = v
            );

            _binder.AddSeparator(_noiseSettingsContainer, 4);

            // === EDGE-AWARE NOISE ===
            _binder.AddCheckBox(
                _noiseSettingsContainer,
                "Edge-Aware",
                () => layer.EdgeAwareNoise,
                v => layer.EdgeAwareNoise = v,
                "Concentrate noise at zone transitions (where blend ≈ 0.5)",
                OnEdgeAwareNoiseChanged
            );

            _edgeAwareContainer = new VBoxContainer();
            _edgeAwareContainer.AddThemeConstantOverride("separation", 4);
            _edgeAwareContainer.Visible = layer.EdgeAwareNoise;

            var edgeMargin = _binder.CreateIndent(_noiseSettingsContainer, 16);
            edgeMargin.AddChild(_edgeAwareContainer);

            if (layer.EdgeAwareNoise)
            {
                _binder.AddSlider(
                    _edgeAwareContainer,
                    "Falloff",
                    () => layer.EdgeNoiseFalloff,
                    v => layer.EdgeNoiseFalloff = v,
                    0f, 1f, 0.01f, "F2",
                    tooltip: "How quickly noise fades from zone center",
                    leftLabel: "Everywhere",
                    rightLabel: "Edges Only"
                );
            }

                       _binder.AddSeparator(_noiseSettingsContainer, 4);

            // === CUSTOM NOISE TEXTURE (collapsible) ===
            BuildCustomNoiseTextureSection(_noiseSettingsContainer, layer);
        }

        private void BuildCustomNoiseTextureSection(VBoxContainer parent, TextureLayer layer)
        {
            _customNoiseTextureContainer = EditorUIUtils.CreateInlineCollapsibleWithHelp(
                parent,
                "Custom Noise Texture",
                false,
                "Custom Noise Texture",
                _sectionExpanded,
                "Custom Noise Texture",
                "Use a pre-baked noise texture instead of procedural noise.\n\n" +
                "• Should tile seamlessly\n" +
                "• Grayscale recommended\n" +
                "• Overrides the procedural noise type when set"
            );

            BuildCustomNoiseTextureContent(layer);
        }

        private void BuildCustomNoiseTextureContent(TextureLayer layer)
        {
            if (_customNoiseTextureContainer == null) return;

            _customNoiseTextureContainer.QueueFreeChildren();

            var textureRow = new HBoxContainer();
            textureRow.AddThemeConstantOverride("separation", 8);

            _noiseTexturePreview = new TextureRect
            {
                CustomMinimumSize = new Vector2(THUMBNAIL_SIZE, THUMBNAIL_SIZE),
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                Texture = layer.NoiseTexture
            };
            ApplyTexturePreviewStyle(_noiseTexturePreview);
            textureRow.AddChild(_noiseTexturePreview);

            var textureButtons = new VBoxContainer();
            textureButtons.AddThemeConstantOverride("separation", 4);

            var selectBtn = new Button
            {
                Text = "Select...",
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
            };
            selectBtn.Pressed += OnSelectNoiseTexturePressed;
            textureButtons.AddChild(selectBtn);

            var clearBtn = new Button
            {
                Text = "Clear",
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
            };
            clearBtn.Pressed += OnClearNoiseTexturePressed;
            textureButtons.AddChild(clearBtn);

            textureRow.AddChild(textureButtons);
            _customNoiseTextureContainer.AddChild(textureRow);
        }

        private void OnNoiseEnabledChanged(bool enabled)
        {
            if (_noiseSettingsContainer == null) return;

            _noiseSettingsContainer.Visible = enabled;

            if (enabled && _noiseSettingsContainer.GetChildCount() == 0)
            {
                BuildNoiseSettingsContent();
            }
        }

        private void OnEdgeAwareNoiseChanged(bool enabled)
        {
            var layer = CurrentLayer;
            if (layer == null || _edgeAwareContainer == null) return;

            _edgeAwareContainer.Visible = enabled;

            if (enabled && _edgeAwareContainer.GetChildCount() == 0)
            {
                _binder.AddSlider(
                    _edgeAwareContainer,
                    "Falloff",
                    () => layer.EdgeNoiseFalloff,
                    v => layer.EdgeNoiseFalloff = v,
                    0f, 1f, 0.01f, "F2",
                    tooltip: "How quickly noise fades from zone center",
                    leftLabel: "Everywhere",
                    rightLabel: "Edges Only"
                );
            }
        }

        private void OnSelectNoiseTexturePressed()
        {
            var layer = CurrentLayer;
            if (layer == null) return;

            var dialog = new EditorFileDialog
            {
                FileMode = EditorFileDialog.FileModeEnum.OpenFile,
                Access = EditorFileDialog.AccessEnum.Resources,
                Title = "Select Noise Texture"
            };
            dialog.AddFilter("*.png", "PNG Images");
            dialog.AddFilter("*.jpg", "JPEG Images");
            dialog.AddFilter("*.webp", "WebP Images");

            dialog.FileSelected += (path) =>
            {
                var texture = GD.Load<Texture2D>(path);
                if (texture != null)
                {
                    layer.NoiseTexture = texture;
                    if (_noiseTexturePreview != null && IsInstanceValid(_noiseTexturePreview))
                    {
                        _noiseTexturePreview.Texture = texture;
                    }
                }
                dialog.QueueFree();
            };

            dialog.Canceled += () => dialog.QueueFree();

            EditorInterface.Singleton.GetBaseControl().AddChild(dialog);
            dialog.PopupCentered(new Vector2I(800, 600));
        }

        private void OnClearNoiseTexturePressed()
        {
            var layer = CurrentLayer;
            if (layer == null) return;

            layer.NoiseTexture = null;

            if (_noiseTexturePreview != null && IsInstanceValid(_noiseTexturePreview))
            {
                _noiseTexturePreview.Texture = null;
            }
        }

        #endregion

        #region Build Smoothing Section

        partial void BuildSmoothingSection()
        {
            var layer = CurrentLayer;
            if (layer == null) return;

            var headerRow = new HBoxContainer();
            headerRow.AddThemeConstantOverride("separation", 0);

            bool isExpanded = _sectionExpanded.TryGetValue("Blend Smoothing", out var expanded) && expanded;

            var headerButton = new Button
            {
                Text = (isExpanded ? "▼ " : "▶ ") + "Blend Smoothing",
                Flat = true,
                Alignment = HorizontalAlignment.Left,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
            };
            headerButton.AddThemeColorOverride("font_color", new Color(0.9f, 0.9f, 0.9f));
            headerButton.AddThemeFontSizeOverride("font_size", 14);
            headerRow.AddChild(headerButton);

            var helpBtn = EditorHelpTooltip.CreateHelpButtonStyled(
                "Blend Smoothing",
                EditorHelpTooltip.FormatHelpText(
                    "Post-processing pass that smooths blend values with neighboring pixels.\n\n" +
                    "Since zone-based blending eliminates texture pair flip-flopping, " +
                    "this pass focuses purely on blend value smoothing for softer gradients.",
                    new List<(string, string)>
                    {
                        ("Blend Smoothing", "General smoothing amount (averages with neighbors)"),
                        ("Boundary Smoothing", "Extra smoothing at zone boundaries"),
                        ("Falloff Edge Smoothing", "Extra smoothing at layer edges"),
                        ("Window Size", "3×3 (fast) or 5×5 (smoother)")
                    },
                    "Enable smoothing for softer transitions, especially when using noise."
                )
            );
            headerRow.AddChild(helpBtn);
            _mainContainer.AddChild(headerRow);

            var separator = new HSeparator();
            separator.AddThemeConstantOverride("separation", 2);
            _mainContainer.AddChild(separator);

            var section = new VBoxContainer();
            section.AddThemeConstantOverride("separation", 4);
            section.Visible = isExpanded;
            _sectionContents["Blend Smoothing"] = section;

            headerButton.Pressed += () =>
            {
                bool newState = !section.Visible;
                section.Visible = newState;
                headerButton.Text = (newState ? "▼ " : "▶ ") + "Blend Smoothing";
                _sectionExpanded["Blend Smoothing"] = newState;
            };

            _mainContainer.AddChild(section);

            // === ENABLE SMOOTHING ===
            _binder.AddCheckBox(
                section,
                "Enable Smoothing",
                () => layer.EnableSmoothing,
                v => layer.EnableSmoothing = v,
                "Apply post-processing pass to smooth blend values",
                OnSmoothingEnabledChanged
            );

            // === SMOOTHING SETTINGS CONTAINER ===
            _smoothingSettingsContainer = new VBoxContainer();
            _smoothingSettingsContainer.AddThemeConstantOverride("separation", 8);
            _smoothingSettingsContainer.Visible = layer.EnableSmoothing;
            section.AddChild(_smoothingSettingsContainer);

            if (layer.EnableSmoothing)
            {
                BuildSmoothingSettingsContent();
            }
        }

        private void BuildSmoothingSettingsContent()
        {
            var layer = CurrentLayer;
            if (layer == null || _smoothingSettingsContainer == null) return;

            _smoothingSettingsContainer.QueueFreeChildren();

            // === BLEND SMOOTHING ===
            _binder.AddSlider(
                _smoothingSettingsContainer,
                "Blend Smoothing",
                () => layer.BlendSmoothing,
                v => layer.BlendSmoothing = v,
                0f, 1f, 0.05f, "F2",
                tooltip: "How much to average blend values with neighbors",
                leftLabel: "None",
                rightLabel: "Strong"
            );

            // === BOUNDARY SMOOTHING ===
            _binder.AddSlider(
                _smoothingSettingsContainer,
                "Boundary Smoothing",
                () => layer.BoundarySmoothing,
                v => layer.BoundarySmoothing = v,
                0f, 1f, 0.05f, "F2",
                tooltip: "Extra smoothing at zone boundaries (where blend changes rapidly)",
                leftLabel: "None",
                rightLabel: "Strong"
            );

            // === FALLOFF EDGE SMOOTHING ===
            _binder.AddSlider(
                _smoothingSettingsContainer,
                "Falloff Edge Smoothing",
                () => layer.FalloffEdgeSmoothing,
                v => layer.FalloffEdgeSmoothing = v,
                0f, 1f, 0.05f, "F2",
                tooltip: "Extra smoothing at layer falloff edges",
                leftLabel: "None",
                rightLabel: "Strong"
            );

            _binder.AddSeparator(_smoothingSettingsContainer, 4);

            // === WINDOW SIZE ===
            _binder.AddDropdown(
                _smoothingSettingsContainer,
                "Window Size",
                new[] { "3×3 (faster)", "5×5 (smoother)" },
                () => layer.SmoothingWindowSize - 1,
                v => layer.SmoothingWindowSize = v + 1
            );
        }

        private void OnSmoothingEnabledChanged(bool enabled)
        {
            if (_smoothingSettingsContainer == null) return;

            _smoothingSettingsContainer.Visible = enabled;

            if (enabled && _smoothingSettingsContainer.GetChildCount() == 0)
            {
                BuildSmoothingSettingsContent();
            }
        }

        #endregion

        #region Refresh

        partial void RefreshBlendSettings()
        {
            var layer = CurrentLayer;
            if (layer == null) return;

            // Blend mode dropdown
            if (_blendModeDropdown != null && IsInstanceValid(_blendModeDropdown))
            {
                _blendModeDropdown.Selected = (int)layer.BlendMode;
            }

            // Noise texture preview
            if (_noiseTexturePreview != null && IsInstanceValid(_noiseTexturePreview))
            {
                _noiseTexturePreview.Texture = layer.NoiseTexture;
            }

            // Container visibility
            if (_noiseSettingsContainer != null && IsInstanceValid(_noiseSettingsContainer))
            {
                _noiseSettingsContainer.Visible = layer.EnableNoise;
            }

            if (_edgeAwareContainer != null && IsInstanceValid(_edgeAwareContainer))
            {
                _edgeAwareContainer.Visible = layer.EdgeAwareNoise;
            }

            if (_smoothingSettingsContainer != null && IsInstanceValid(_smoothingSettingsContainer))
            {
                _smoothingSettingsContainer.Visible = layer.EnableSmoothing;
            }
        }

        #endregion
    }
    
}