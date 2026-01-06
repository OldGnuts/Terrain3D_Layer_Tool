// /Editor/GlobalSettingsWindow_VisualizationTab.cs
using Godot;
using Terrain3DTools.Editor.Utils;
using Terrain3DTools.Settings;

namespace Terrain3DTools.Editor
{
    public partial class GlobalSettingsWindow
    {
        #region Visualization Tab Fields
        private VBoxContainer _heightVizContainer;
        private VBoxContainer _textureVizContainer;
        private VBoxContainer _featureVizContainer;
        #endregion

        #region Visualization Tab
        private void CreateVisualizationTab()
        {
            var content = CreateTab("👁️ Visualization");
            PopulateVisualizationTab(content);
        }

        private void PopulateVisualizationTab(VBoxContainer content)
        {
            var settings = Settings;
            if (settings == null) return;

            // Layer Visualization Overview
            AddLayerVisualizationSection(content, settings);

            EditorUIUtils.AddSeparator(content, 12);

            // Height Layer Settings
            AddHeightVisualizationSection(content, settings);

            EditorUIUtils.AddSeparator(content, 12);

            // Texture Layer Settings
            AddTextureVisualizationSection(content, settings);

            EditorUIUtils.AddSeparator(content, 12);

            // Feature Layer Settings
            AddFeatureVisualizationSection(content, settings);

            EditorUIUtils.AddSeparator(content, 12);

            // Region Preview Settings
            AddRegionPreviewSection(content, settings);
        }

        private void AddLayerVisualizationSection(VBoxContainer parent, GlobalToolSettings settings)
        {
            string vizHelp = EditorHelpTooltip.FormatHelpText(
                "Enable or disable visualization for each layer type.",
                new System.Collections.Generic.List<(string, string)>
                {
                    ("Height Layers", "Shows terrain displacement with green (raise) and red (lower) colors"),
                    ("Texture Layers", "Shows texture painting influence in violet"),
                    ("Feature Layers", "Shows path/feature influence with contour lines")
                },
                "Visualization only appears when a layer is selected in the scene tree."
            );

            EditorUIUtils.AddSectionHeader(parent, "🎛️ Layer Visualization Toggles", "Layer Visualization Help", vizHelp);

            var toggleContainer = ExpandHorizontal(new HBoxContainer());
            toggleContainer.AddThemeConstantOverride("separation", 16);

            // Height toggle
            var heightToggle = CreateVisualizationToggle("📐 Height", settings.EnableHeightLayerVisualization,
                new Color(0.2f, 0.8f, 0.2f), (v) =>
                {
                    settings.EnableHeightLayerVisualization = v;
                    UpdateVisualizationSectionVisibility();
                    MarkSettingsChanged();
                });
            toggleContainer.AddChild(heightToggle);

            // Texture toggle
            var textureToggle = CreateVisualizationToggle("🎨 Texture", settings.EnableTextureLayerVisualization,
                new Color(0.6f, 0.2f, 1.0f), (v) =>
                {
                    settings.EnableTextureLayerVisualization = v;
                    UpdateVisualizationSectionVisibility();
                    MarkSettingsChanged();
                });
            toggleContainer.AddChild(textureToggle);

            // Feature toggle
            var featureToggle = CreateVisualizationToggle("🛤️ Feature", settings.EnableFeatureLayerVisualization,
                new Color(0.4f, 0.9f, 1.0f), (v) =>
                {
                    settings.EnableFeatureLayerVisualization = v;
                    UpdateVisualizationSectionVisibility();
                    MarkSettingsChanged();
                });
            toggleContainer.AddChild(featureToggle);

            parent.AddChild(toggleContainer);
        }

        private Control CreateVisualizationToggle(string label, bool initialValue, Color accentColor, System.Action<bool> onToggled)
        {
            var container = new VBoxContainer();
            container.AddThemeConstantOverride("separation", 4);

            // Color indicator bar
            var colorBar = new ColorRect
            {
                CustomMinimumSize = new Vector2(80, 4),
                Color = initialValue ? accentColor : accentColor with { A = 0.3f }
            };

            var checkBox = new CheckBox
            {
                Text = label,
                ButtonPressed = initialValue
            };

            checkBox.Toggled += (v) =>
            {
                colorBar.Color = v ? accentColor : accentColor with { A = 0.3f };
                onToggled?.Invoke(v);
            };

            container.AddChild(colorBar);
            container.AddChild(checkBox);

            return container;
        }

        private void AddHeightVisualizationSection(VBoxContainer parent, GlobalToolSettings settings)
        {
            string heightHelp = EditorHelpTooltip.FormatHelpText(
                "Customize height layer visualization appearance.",
                new System.Collections.Generic.List<(string, string)>
                {
                    ("Positive Color", "Color for terrain being raised (addition)"),
                    ("Negative Color", "Color for terrain being lowered (subtraction)"),
                    ("Opacity", "Overall transparency of the visualization overlay")
                },
                null
            );

            EditorUIUtils.AddSectionHeader(parent, "📐 Height Layer Visualization", "Height Visualization Help", heightHelp);

            _heightVizContainer = ExpandHorizontal(new VBoxContainer());
            _heightVizContainer.AddThemeConstantOverride("separation", 8);

            // Positive color (Add/Raise)
            AddColorPickerRow(_heightVizContainer, "Positive (Raise)", settings.HeightPositiveColor,
                (c) => { settings.HeightPositiveColor = c; MarkSettingsChanged(); },
                "Color for terrain being raised");

            // Negative color (Subtract/Lower)
            AddColorPickerRow(_heightVizContainer, "Negative (Lower)", settings.HeightNegativeColor,
                (c) => { settings.HeightNegativeColor = c; MarkSettingsChanged(); },
                "Color for terrain being lowered");

            // Opacity
            EditorUIUtils.AddSliderRow(_heightVizContainer, "Opacity", settings.HeightVisualizationOpacity, 0.0f, 1.0f,
                (v) => { settings.HeightVisualizationOpacity = (float)v; MarkSettingsChanged(); }, "F2");

            _heightVizContainer.Visible = settings.EnableHeightLayerVisualization;
            parent.AddChild(_heightVizContainer);
        }

        private void AddTextureVisualizationSection(VBoxContainer parent, GlobalToolSettings settings)
        {
            string textureHelp = EditorHelpTooltip.FormatHelpText(
                "Customize texture layer visualization appearance.",
                new System.Collections.Generic.List<(string, string)>
                {
                    ("Base Color", "Primary color for texture influence areas"),
                    ("Highlight Color", "Color for high-influence areas (>80%)"),
                    ("Opacity", "Overall transparency of the visualization overlay")
                },
                null
            );

            EditorUIUtils.AddSectionHeader(parent, "🎨 Texture Layer Visualization", "Texture Visualization Help", textureHelp);

            _textureVizContainer = ExpandHorizontal(new VBoxContainer());
            _textureVizContainer.AddThemeConstantOverride("separation", 8);

            // Base color
            AddColorPickerRow(_textureVizContainer, "Base Color", settings.TextureBaseColor,
                (c) => { settings.TextureBaseColor = c; MarkSettingsChanged(); },
                "Primary visualization color");

            // Highlight color
            AddColorPickerRow(_textureVizContainer, "Highlight Color", settings.TextureHighlightColor,
                (c) => { settings.TextureHighlightColor = c; MarkSettingsChanged(); },
                "Color for strong influence areas");

            // Opacity
            EditorUIUtils.AddSliderRow(_textureVizContainer, "Opacity", settings.TextureVisualizationOpacity, 0.0f, 1.0f,
                (v) => { settings.TextureVisualizationOpacity = (float)v; MarkSettingsChanged(); }, "F2");

            _textureVizContainer.Visible = settings.EnableTextureLayerVisualization;
            parent.AddChild(_textureVizContainer);
        }

        private void AddFeatureVisualizationSection(VBoxContainer parent, GlobalToolSettings settings)
        {
            string featureHelp = EditorHelpTooltip.FormatHelpText(
                "Customize feature layer (paths, roads) visualization.",
                new System.Collections.Generic.List<(string, string)>
                {
                    ("Positive Color", "Color for raised areas (path surface)"),
                    ("Negative Color", "Color for carved areas (embankments, cuts)"),
                    ("Opacity", "Overall transparency of the visualization overlay"),
                    ("Show Contours", "Display contour lines for depth perception"),
                    ("Contour Spacing", "Distance between contour lines (in influence units)")
                },
                null
            );

            EditorUIUtils.AddSectionHeader(parent, "🛤️ Feature Layer Visualization", "Feature Visualization Help", featureHelp);

            _featureVizContainer = ExpandHorizontal(new VBoxContainer());
            _featureVizContainer.AddThemeConstantOverride("separation", 8);

            // Positive color
            AddColorPickerRow(_featureVizContainer, "Positive (Surface)", settings.FeaturePositiveColor,
                (c) => { settings.FeaturePositiveColor = c; MarkSettingsChanged(); },
                "Color for raised path surfaces");

            // Negative color
            AddColorPickerRow(_featureVizContainer, "Negative (Carved)", settings.FeatureNegativeColor,
                (c) => { settings.FeatureNegativeColor = c; MarkSettingsChanged(); },
                "Color for carved/cut areas");

            // Opacity
            EditorUIUtils.AddSliderRow(_featureVizContainer, "Opacity", settings.FeatureVisualizationOpacity, 0.0f, 1.0f,
                (v) => { settings.FeatureVisualizationOpacity = (float)v; MarkSettingsChanged(); }, "F2");

            EditorUIUtils.AddSeparator(_featureVizContainer, 8);

            // Contour settings
            EditorUIUtils.AddSubsectionHeader(_featureVizContainer, "Contour Lines");

            EditorUIUtils.AddCheckBoxRow(_featureVizContainer, "Show Contours", settings.FeatureShowContours,
                (v) => { settings.FeatureShowContours = v; MarkSettingsChanged(); },
                "Display contour lines for better depth perception");

            EditorUIUtils.AddSliderRow(_featureVizContainer, "Contour Spacing", settings.FeatureContourSpacing, 0.01f, 0.5f,
                (v) => { settings.FeatureContourSpacing = (float)v; MarkSettingsChanged(); }, "F2");

            _featureVizContainer.Visible = settings.EnableFeatureLayerVisualization;
            parent.AddChild(_featureVizContainer);
        }

        private void AddRegionPreviewSection(VBoxContainer parent, GlobalToolSettings settings)
        {
            string regionHelp = EditorHelpTooltip.FormatHelpText(
                "Configure region preview visualization.",
                new System.Collections.Generic.List<(string, string)>
                {
                    ("Enable Region Previews", "Show preview meshes for terrain regions being processed"),
                },
                "Region previews can help debug terrain updates but may impact performance."
            );

            EditorUIUtils.AddSectionHeader(parent, "🗺️ Region Preview", "Region Preview Help", regionHelp);

            var regionContainer = ExpandHorizontal(new VBoxContainer());
            regionContainer.AddThemeConstantOverride("separation", 8);

            EditorUIUtils.AddCheckBoxRow(regionContainer, "Enable Region Previews", settings.EnableRegionPreviews,
                (v) => { settings.EnableRegionPreviews = v; MarkSettingsChanged(); },
                "Show preview meshes for regions being processed (may impact performance)");

            // Info label
            var infoLabel = new Label
            {
                Text = "⚠️ Region previews are disabled by default as they can impact editor performance.",
                AutowrapMode = TextServer.AutowrapMode.Word
            };
            infoLabel.AddThemeColorOverride("font_color", new Color(0.7f, 0.6f, 0.4f));
            infoLabel.AddThemeFontSizeOverride("font_size", 11);
            regionContainer.AddChild(infoLabel);

            parent.AddChild(regionContainer);
        }

        private void AddColorPickerRow(VBoxContainer parent, string label, Color initialColor,
            System.Action<Color> onChanged, string tooltip = null)
        {
            var row = ExpandHorizontal(new HBoxContainer());
            row.AddThemeConstantOverride("separation", 8);

            var nameLabel = new Label
            {
                Text = label,
                CustomMinimumSize = new Vector2(EditorConstants.LABEL_MIN_WIDTH + 20, 0)
            };
            if (!string.IsNullOrEmpty(tooltip))
            {
                nameLabel.TooltipText = tooltip;
            }
            row.AddChild(nameLabel);

            // Color preview rect
            var colorPreview = new ColorRect
            {
                CustomMinimumSize = new Vector2(32, 24),
                Color = initialColor
            };
            var colorPreviewPanel = new PanelContainer();
            var previewStyle = new StyleBoxFlat
            {
                BgColor = Colors.Black,
                ContentMarginLeft = 2,
                ContentMarginRight = 2,
                ContentMarginTop = 2,
                ContentMarginBottom = 2,
                CornerRadiusBottomLeft = 3,
                CornerRadiusBottomRight = 3,
                CornerRadiusTopLeft = 3,
                CornerRadiusTopRight = 3
            };
            colorPreviewPanel.AddThemeStyleboxOverride("panel", previewStyle);
            colorPreviewPanel.AddChild(colorPreview);
            row.AddChild(colorPreviewPanel);

            // Color picker button
            var colorButton = new ColorPickerButton
            {
                Color = initialColor,
                CustomMinimumSize = new Vector2(100, 24),
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                EditAlpha = false
            };
            colorButton.ColorChanged += (c) =>
            {
                colorPreview.Color = c;
                onChanged?.Invoke(c);
            };
            row.AddChild(colorButton);

            // Reset button
            var resetBtn = new Button
            {
                Text = "↺",
                TooltipText = "Reset to default",
                CustomMinimumSize = new Vector2(28, 24)
            };
            Color defaultColor = initialColor; // Capture for reset
            resetBtn.Pressed += () =>
            {
                // Get fresh default from a new settings instance
                var defaults = GlobalToolSettings.CreateDefault();
                Color resetColor = GetDefaultColorForLabel(label, defaults);
                colorButton.Color = resetColor;
                colorPreview.Color = resetColor;
                onChanged?.Invoke(resetColor);
            };
            row.AddChild(resetBtn);

            parent.AddChild(row);
        }

        private Color GetDefaultColorForLabel(string label, GlobalToolSettings defaults)
        {
            return label switch
            {
                "Positive (Raise)" => defaults.HeightPositiveColor,
                "Negative (Lower)" => defaults.HeightNegativeColor,
                "Base Color" => defaults.TextureBaseColor,
                "Highlight Color" => defaults.TextureHighlightColor,
                "Positive (Surface)" => defaults.FeaturePositiveColor,
                "Negative (Carved)" => defaults.FeatureNegativeColor,
                _ => Colors.White
            };
        }

        private void UpdateVisualizationSectionVisibility()
        {
            var settings = Settings;
            if (settings == null) return;

            if (_heightVizContainer != null)
                _heightVizContainer.Visible = settings.EnableHeightLayerVisualization;

            if (_textureVizContainer != null)
                _textureVizContainer.Visible = settings.EnableTextureLayerVisualization;

            if (_featureVizContainer != null)
                _featureVizContainer.Visible = settings.EnableFeatureLayerVisualization;
        }
        #endregion
    }
}