// /Editor/TextureLayerInspector.TextureSelection.cs
using System;
using System.Collections.Generic;
using Godot;
using Terrain3DTools.Layers;
using Terrain3DTools.Editor.Utils;
using TokisanGames;

namespace Terrain3DTools.Editor
{
    public partial class TextureLayerInspector
    {
        #region UI References - Texture Selection

        private TextureRect _primaryTexturePreview;
        private Label _primaryTextureLabel;
        private CheckBox _gradientModeCheckbox;
        private VBoxContainer _gradientModeContainer;

        // Secondary texture
        private TextureRect _secondaryTexturePreview;
        private Label _secondaryTextureLabel;

        // Tertiary texture
        private TextureRect _tertiaryTexturePreview;
        private Label _tertiaryTextureLabel;

        // Zone visualization
        private Control _zoneVisualization;
        private VBoxContainer _zoneContainer;

        #endregion

        #region Zone Colors

        private static readonly Color ZoneColorOriginal = new Color(0.6f, 0.6f, 0.6f);
        private static readonly Color ZoneColorTertiary = new Color(0.3f, 0.8f, 0.4f);
        private static readonly Color ZoneColorSecondary = new Color(0.9f, 0.6f, 0.2f);
        private static readonly Color ZoneColorPrimary = new Color(0.3f, 0.5f, 0.9f);

        #endregion

        partial void BuildTextureSelectionSection()
        {
            var layer = CurrentLayer;
            if (layer == null) return;

            var assets = CurrentTerrain3DAssets;
            if (assets == null) return;

            // === TEXTURE SELECTION SECTION ===
            string helpContent = EditorHelpTooltip.FormatHelpText(
                "Select textures to paint on the terrain based on mask values.",
                new List<(string, string)>
                {
                    ("Primary Texture", "Dominates at high mask values (steep slopes, peaks)"),
                    ("Secondary Texture", "Appears at mid mask values (moderate slopes)"),
                    ("Tertiary Texture", "Appears at low mask values (gentle slopes, flats)"),
                    ("Original Base", "Underlying terrain texture, visible at lowest mask values"),
                    ("Zone Thresholds", "Define where each texture starts dominating")
                },
                "Tip: Thresholds define fixed zones - textures never flip-flop, ensuring smooth blending."
            );

            var (section, _) = EditorUIUtils.CreateCollapsibleSectionWithHelp(
                _mainContainer,
                "Texture Selection",
                _sectionExpanded.TryGetValue("Texture Selection", out var expanded) ? expanded : true,
                _sectionExpanded,
                "Texture Selection Help",
                helpContent
            );
            _sectionContents["Texture Selection"] = section;

            // === PRIMARY TEXTURE (always shown) ===
            BuildPrimaryTextureUI(section, layer, assets);

            _binder.AddSeparator(section, 12);

            // === GRADIENT MODE TOGGLE with help ===
            var gradientRow = new HBoxContainer();
            gradientRow.AddThemeConstantOverride("separation", 8);

            _gradientModeCheckbox = new CheckBox
            {
                Text = "Enable Gradient Mode",
                ButtonPressed = layer.GradientModeEnabled,
                TooltipText = "Zone-based blending: Original → Tertiary → Secondary → Primary"
            };
            _gradientModeCheckbox.Toggled += (v) =>
            {
                layer.GradientModeEnabled = v;
                OnGradientModeToggled(v);
            };
            gradientRow.AddChild(_gradientModeCheckbox);

            var gradientHelpBtn = EditorHelpTooltip.CreateHelpButtonStyled(
                "Gradient Mode",
                EditorHelpTooltip.FormatHelpText(
                    "Zone-based gradient blending eliminates striping artifacts.\n\n" +
                    "Each zone transitions between exactly 2 textures. Zones are determined by mask value thresholds.",
                    new List<(string, string)>
                    {
                        ("Zone 0", "Original → Tertiary (mask < Tertiary Threshold)"),
                        ("Zone 1", "Tertiary → Secondary (mask < Secondary Threshold)"),
                        ("Zone 2", "Secondary → Primary (mask < Primary Threshold)"),
                        ("Zone 3", "Primary dominates (mask ≥ Primary Threshold)")
                    },
                    "Within each zone, blend values transition smoothly. Noise affects blend amount, not zone selection."
                )
            );
            gradientRow.AddChild(gradientHelpBtn);
            section.AddChild(gradientRow);

            // === GRADIENT MODE CONTAINER ===
            _gradientModeContainer = new VBoxContainer();
            _gradientModeContainer.AddThemeConstantOverride("separation", 8);
            _gradientModeContainer.Visible = layer.GradientModeEnabled;
            section.AddChild(_gradientModeContainer);

            if (layer.GradientModeEnabled)
            {
                BuildGradientModeContent(layer, assets);
            }
        }

        private void BuildPrimaryTextureUI(VBoxContainer section, TextureLayer layer, Terrain3DAssets assets)
        {
            var headerLabel = new Label
            {
                Text = "Primary Texture (High Mask Values)",
                Modulate = ZoneColorPrimary
            };
            headerLabel.AddThemeFontSizeOverride("font_size", 12);
            section.AddChild(headerLabel);

            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 12);

            // Thumbnail
            _primaryTexturePreview = new TextureRect
            {
                CustomMinimumSize = new Vector2(LARGE_THUMBNAIL_SIZE, LARGE_THUMBNAIL_SIZE),
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize
            };
            ApplyTexturePreviewStyle(_primaryTexturePreview);
            row.AddChild(_primaryTexturePreview);

            // Info column
            var infoCol = new VBoxContainer();
            infoCol.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            infoCol.AddThemeConstantOverride("separation", 4);

            _primaryTextureLabel = new Label
            {
                Text = "None selected",
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                AutowrapMode = TextServer.AutowrapMode.Word
            };
            infoCol.AddChild(_primaryTextureLabel);

            var selectBtn = new Button
            {
                Text = "Select Texture...",
                SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin
            };
            selectBtn.Pressed += OnPrimarySelectPressed;
            infoCol.AddChild(selectBtn);

            row.AddChild(infoCol);
            section.AddChild(row);

            UpdatePrimaryTextureDisplay();
        }

        private void BuildGradientModeContent(TextureLayer layer, Terrain3DAssets assets)
        {
            _gradientModeContainer.QueueFreeChildren();

            // === ZONE VISUALIZATION (at top) ===
            BuildZoneVisualization(_gradientModeContainer, layer);

            _binder.AddSeparator(_gradientModeContainer, 12);

            // === SECONDARY TEXTURE ===
            var secondarySection = EditorUIUtils.CreateInlineCollapsibleWithHelp(
                _gradientModeContainer,
                "Secondary Texture (Mid Mask Values)",
                true,
                "Secondary Texture",
                _sectionExpanded,
                "Secondary Texture",
                "Appears between Tertiary and Primary thresholds.\n\n" +
                "• Threshold: Mask value where Secondary starts dominating\n" +
                "• Transition: How gradual the blend is (0.1 = sharp, 1.0 = gradual)\n" +
                "• Set to -1 to disable and skip this texture in the gradient"
            );
            BuildSecondaryTextureUI(secondarySection, layer, assets);

            // === TERTIARY TEXTURE ===
            _binder.AddSeparator(_gradientModeContainer, 8);
            var tertiarySection = EditorUIUtils.CreateInlineCollapsibleWithHelp(
                _gradientModeContainer,
                "Tertiary Texture (Low Mask Values)",
                true,
                "Tertiary Texture",
                _sectionExpanded,
                "Tertiary Texture",
                "Appears between Original and Secondary thresholds.\n\n" +
                "• Threshold: Mask value where Tertiary starts dominating\n" +
                "• This texture blends with the original base at lowest mask values\n" +
                "• Set to -1 to disable"
            );
            BuildTertiaryTextureUI(tertiarySection, layer, assets);

            // === ZONE THRESHOLDS (all in one place) ===
            _binder.AddSeparator(_gradientModeContainer, 12);
            BuildZoneThresholdsSection(_gradientModeContainer, layer);
        }

        private void BuildSecondaryTextureUI(VBoxContainer section, TextureLayer layer, Terrain3DAssets assets)
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 12);

            _secondaryTexturePreview = new TextureRect
            {
                CustomMinimumSize = new Vector2(THUMBNAIL_SIZE, THUMBNAIL_SIZE),
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize
            };
            ApplyTexturePreviewStyle(_secondaryTexturePreview);
            row.AddChild(_secondaryTexturePreview);

            var infoCol = new VBoxContainer();
            infoCol.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            infoCol.AddThemeConstantOverride("separation", 4);

            _secondaryTextureLabel = new Label
            {
                Text = layer.SecondaryTextureIndex < 0 ? "Disabled" : "Loading...",
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
            };
            infoCol.AddChild(_secondaryTextureLabel);

            var btnRow = new HBoxContainer();
            btnRow.AddThemeConstantOverride("separation", 4);

            var selectBtn = new Button { Text = "Select..." };
            selectBtn.Pressed += OnSecondarySelectPressed;
            btnRow.AddChild(selectBtn);

            var clearBtn = new Button { Text = "Disable" };
            clearBtn.Pressed += OnSecondaryClearPressed;
            btnRow.AddChild(clearBtn);

            infoCol.AddChild(btnRow);
            row.AddChild(infoCol);
            section.AddChild(row);

            // Transition width slider (only if enabled)
            if (layer.SecondaryTextureIndex >= 0)
            {
                _binder.AddSeparator(section, 8);
                _binder.AddSlider(
                    section,
                    "Transition Width",
                    () => layer.SecondaryTransition,
                    v => { layer.SecondaryTransition = v; UpdateZoneVisualization(); },
                    0.1f, 2.0f, 0.05f, "F2",
                    tooltip: "How gradual the blend is in this zone",
                    leftLabel: "Sharp",
                    rightLabel: "Gradual"
                );
            }

            UpdateSecondaryTextureDisplay();
        }

        private void BuildTertiaryTextureUI(VBoxContainer section, TextureLayer layer, Terrain3DAssets assets)
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 12);

            _tertiaryTexturePreview = new TextureRect
            {
                CustomMinimumSize = new Vector2(THUMBNAIL_SIZE, THUMBNAIL_SIZE),
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize
            };
            ApplyTexturePreviewStyle(_tertiaryTexturePreview);
            row.AddChild(_tertiaryTexturePreview);

            var infoCol = new VBoxContainer();
            infoCol.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            infoCol.AddThemeConstantOverride("separation", 4);

            _tertiaryTextureLabel = new Label
            {
                Text = layer.TertiaryTextureIndex < 0 ? "Disabled" : "Loading...",
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
            };
            infoCol.AddChild(_tertiaryTextureLabel);

            var btnRow = new HBoxContainer();
            btnRow.AddThemeConstantOverride("separation", 4);

            var selectBtn = new Button { Text = "Select..." };
            selectBtn.Pressed += OnTertiarySelectPressed;
            btnRow.AddChild(selectBtn);

            var clearBtn = new Button { Text = "Disable" };
            clearBtn.Pressed += OnTertiaryClearPressed;
            btnRow.AddChild(clearBtn);

            infoCol.AddChild(btnRow);
            row.AddChild(infoCol);
            section.AddChild(row);

            // Transition width slider (only if enabled)
            if (layer.TertiaryTextureIndex >= 0)
            {
                _binder.AddSeparator(section, 8);
                _binder.AddSlider(
                    section,
                    "Transition Width",
                    () => layer.TertiaryTransition,
                    v => { layer.TertiaryTransition = v; UpdateZoneVisualization(); },
                    0.1f, 2.0f, 0.05f, "F2",
                    tooltip: "How gradual the blend is in this zone",
                    leftLabel: "Sharp",
                    rightLabel: "Gradual"
                );
            }

            UpdateTertiaryTextureDisplay();
        }

        private void BuildZoneThresholdsSection(VBoxContainer parent, TextureLayer layer)
        {
            var headerRow = new HBoxContainer();
            headerRow.AddThemeConstantOverride("separation", 8);

            var headerLabel = new Label
            {
                Text = "Zone Thresholds",
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
            };
            headerLabel.AddThemeFontSizeOverride("font_size", 13);
            headerRow.AddChild(headerLabel);

            var helpBtn = EditorHelpTooltip.CreateHelpButtonStyled(
                "Zone Thresholds",
                EditorHelpTooltip.FormatHelpText(
                    "Thresholds define where each texture starts dominating.\n\n" +
                    "These create fixed zones - texture pairs never flip-flop within a zone.",
                    new List<(string, string)>
                    {
                        ("Tertiary Threshold", "Mask value where Tertiary starts (ends Original zone)"),
                        ("Secondary Threshold", "Mask value where Secondary starts (ends Tertiary zone)"),
                        ("Primary Threshold", "Mask value where Primary dominates fully")
                    },
                    "Keep thresholds in ascending order. The visualization shows the resulting zones."
                )
            );
            headerRow.AddChild(helpBtn);
            parent.AddChild(headerRow);

            var thresholdContainer = new VBoxContainer();
            thresholdContainer.AddThemeConstantOverride("separation", 6);

            // Tertiary threshold (only if tertiary enabled)
            if (layer.TertiaryTextureIndex >= 0)
            {
                var tertRow = new HBoxContainer();
                tertRow.AddThemeConstantOverride("separation", 8);

                var tertColor = new ColorRect
                {
                    CustomMinimumSize = new Vector2(16, 16),
                    Color = ZoneColorTertiary
                };
                tertRow.AddChild(tertColor);

                _binder.AddSliderInline(
                    tertRow,
                    "Tertiary starts at:",
                    () => layer.TertiaryThreshold,
                    v => { layer.TertiaryThreshold = v; ValidateAndUpdateThresholds(); },
                    0.01f, 0.98f, 0.01f, "F2"
                );
                thresholdContainer.AddChild(tertRow);
            }

            // Secondary threshold (only if secondary enabled)
            if (layer.SecondaryTextureIndex >= 0)
            {
                var secRow = new HBoxContainer();
                secRow.AddThemeConstantOverride("separation", 8);

                var secColor = new ColorRect
                {
                    CustomMinimumSize = new Vector2(16, 16),
                    Color = ZoneColorSecondary
                };
                secRow.AddChild(secColor);

                _binder.AddSliderInline(
                    secRow,
                    "Secondary starts at:",
                    () => layer.SecondaryThreshold,
                    v => { layer.SecondaryThreshold = v; ValidateAndUpdateThresholds(); },
                    0.02f, 0.99f, 0.01f, "F2"
                );
                thresholdContainer.AddChild(secRow);
            }

            // Primary threshold (always shown)
            var primRow = new HBoxContainer();
            primRow.AddThemeConstantOverride("separation", 8);

            var primColor = new ColorRect
            {
                CustomMinimumSize = new Vector2(16, 16),
                Color = ZoneColorPrimary
            };
            primRow.AddChild(primColor);

            _binder.AddSliderInline(
                primRow,
                "Primary dominates at:",
                () => layer.PrimaryThreshold,
                v => { layer.PrimaryThreshold = v; ValidateAndUpdateThresholds(); },
                0.03f, 1.0f, 0.01f, "F2"
            );
            thresholdContainer.AddChild(primRow);

            // Primary transition width
            _binder.AddSeparator(thresholdContainer, 4);
            _binder.AddSlider(
                thresholdContainer,
                "Primary Transition",
                () => layer.PrimaryTransition,
                v => { layer.PrimaryTransition = v; UpdateZoneVisualization(); },
                0.1f, 2.0f, 0.05f, "F2",
                tooltip: "How gradual the final transition to Primary is",
                leftLabel: "Sharp",
                rightLabel: "Gradual"
            );

            parent.AddChild(thresholdContainer);
        }

        /// <summary>
        /// Ensures thresholds stay in ascending order.
        /// </summary>
        private void ValidateAndUpdateThresholds()
        {
            var layer = CurrentLayer;
            if (layer == null) return;

            // Enforce ordering: tertiary < secondary < primary
            if (layer.TertiaryTextureIndex >= 0 && layer.SecondaryTextureIndex >= 0)
            {
                if (layer.TertiaryThreshold >= layer.SecondaryThreshold)
                {
                    layer.TertiaryThreshold = layer.SecondaryThreshold - 0.01f;
                }
                if (layer.SecondaryThreshold >= layer.PrimaryThreshold)
                {
                    layer.SecondaryThreshold = layer.PrimaryThreshold - 0.01f;
                }
            }
            else if (layer.TertiaryTextureIndex >= 0)
            {
                if (layer.TertiaryThreshold >= layer.PrimaryThreshold)
                {
                    layer.TertiaryThreshold = layer.PrimaryThreshold - 0.01f;
                }
            }
            else if (layer.SecondaryTextureIndex >= 0)
            {
                if (layer.SecondaryThreshold >= layer.PrimaryThreshold)
                {
                    layer.SecondaryThreshold = layer.PrimaryThreshold - 0.01f;
                }
            }

            UpdateZoneVisualization();
        }

        #region Zone Visualization

        private void BuildZoneVisualization(VBoxContainer parent, TextureLayer layer)
        {
            // Header with help
            var headerRow = new HBoxContainer();
            headerRow.AddThemeConstantOverride("separation", 8);

            var headerLabel = new Label
            {
                Text = "Zone Preview",
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
            };
            headerLabel.AddThemeFontSizeOverride("font_size", 12);
            headerLabel.AddThemeColorOverride("font_color", new Color(0.9f, 0.9f, 0.9f));
            headerRow.AddChild(headerLabel);

            var helpBtn = EditorHelpTooltip.CreateHelpButtonStyled(
                "Zone Preview",
                "Shows texture zones across mask values (0.0 to 1.0).\n\n" +
                "Each zone blends exactly 2 textures:\n" +
                "• Gray = Original base texture\n" +
                "• Green = Tertiary texture\n" +
                "• Orange = Secondary texture\n" +
                "• Blue = Primary texture\n\n" +
                "Vertical lines show zone boundaries (thresholds).\n" +
                "Gradient width shows transition sharpness."
            );
            headerRow.AddChild(helpBtn);
            parent.AddChild(headerRow);

            // Visualization container
            _zoneContainer = new VBoxContainer();
            _zoneContainer.AddThemeConstantOverride("separation", 4);

            _zoneVisualization = CreateZoneDisplay(layer);
            _zoneContainer.AddChild(_zoneVisualization);

            // Legend
            var legend = new HBoxContainer();
            legend.AddThemeConstantOverride("separation", 16);

            AddLegendItem(legend, "Original", ZoneColorOriginal);
            if (layer.TertiaryTextureIndex >= 0)
                AddLegendItem(legend, "Tertiary", ZoneColorTertiary);
            if (layer.SecondaryTextureIndex >= 0)
                AddLegendItem(legend, "Secondary", ZoneColorSecondary);
            AddLegendItem(legend, "Primary", ZoneColorPrimary);

            _zoneContainer.AddChild(legend);
            parent.AddChild(_zoneContainer);
        }

        private void AddLegendItem(HBoxContainer parent, string label, Color color)
        {
            var item = new HBoxContainer();
            item.AddThemeConstantOverride("separation", 4);

            item.AddChild(new ColorRect
            {
                CustomMinimumSize = new Vector2(12, 12),
                Color = color
            });

            item.AddChild(new Label
            {
                Text = label,
                Modulate = new Color(0.7f, 0.7f, 0.7f)
            });

            parent.AddChild(item);
        }

        private Control CreateZoneDisplay(TextureLayer layer)
        {
            var container = new VBoxContainer();
            container.CustomMinimumSize = new Vector2(0, 70);
            container.AddThemeConstantOverride("separation", 0);

            // Main bar showing zones
            var barContainer = new HBoxContainer();
            barContainer.CustomMinimumSize = new Vector2(0, 50);
            barContainer.AddThemeConstantOverride("separation", 0);

            bool hasSecondary = layer.SecondaryTextureIndex >= 0;
            bool hasTertiary = layer.TertiaryTextureIndex >= 0;

            const int samples = 100;

            for (int i = 0; i < samples; i++)
            {
                float maskValue = (float)i / (samples - 1);
                Color sampleColor = CalculateZoneColor(layer, maskValue, hasSecondary, hasTertiary);

                var column = new ColorRect
                {
                    Color = sampleColor,
                    SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
                };
                barContainer.AddChild(column);
            }

            container.AddChild(barContainer);

            // Threshold markers and labels
            var markerContainer = new Control();
            markerContainer.CustomMinimumSize = new Vector2(0, 20);

            // We'll add markers as child controls positioned absolutely
            // This is a simplified approach - in production you might use _Draw()

            container.AddChild(markerContainer);

            // Axis labels
            var axis = new HBoxContainer();
            axis.AddChild(new Label { Text = "0.0", Modulate = new Color(0.5f, 0.5f, 0.5f) });
            axis.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
            axis.AddChild(new Label { Text = "Mask Value", Modulate = new Color(0.5f, 0.5f, 0.5f) });
            axis.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
            axis.AddChild(new Label { Text = "1.0", Modulate = new Color(0.5f, 0.5f, 0.5f) });
            container.AddChild(axis);

            return container;
        }

        private Color CalculateZoneColor(TextureLayer layer, float maskValue, bool hasSecondary, bool hasTertiary)
        {
            // Get zone info
            var (zoneIndex, baseTex, overlayTex, zoneT) = layer.CalculateZoneForMaskValue(maskValue, 0);

            // Apply transition curve
            float transitionWidth = layer.GetTransitionWidthForZone(zoneIndex);
            float curvedT = layer.ApplyTransitionCurve(zoneT, transitionWidth);

            // Get colors for base and overlay
            Color baseColor = GetTextureZoneColor(layer, baseTex, hasTertiary, hasSecondary);
            Color overlayColor = GetTextureZoneColor(layer, overlayTex, hasTertiary, hasSecondary);

            // Blend colors
            return baseColor.Lerp(overlayColor, curvedT);
        }

        private Color GetTextureZoneColor(TextureLayer layer, uint texId, bool hasTertiary, bool hasSecondary)
        {
            if (texId == (uint)layer.TextureIndex)
                return ZoneColorPrimary;
            if (hasSecondary && texId == (uint)layer.SecondaryTextureIndex)
                return ZoneColorSecondary;
            if (hasTertiary && texId == (uint)layer.TertiaryTextureIndex)
                return ZoneColorTertiary;
            // Original base or unknown
            return ZoneColorOriginal;
        }

        private void UpdateZoneVisualization()
        {
            var layer = CurrentLayer;
            if (layer == null || _zoneVisualization == null || _zoneContainer == null) return;

            var parent = _zoneVisualization.GetParent();
            if (parent == null) return;

            int index = _zoneVisualization.GetIndex();
            _zoneVisualization.QueueFree();

            _zoneVisualization = CreateZoneDisplay(layer);
            parent.AddChild(_zoneVisualization);
            parent.MoveChild(_zoneVisualization, index);
        }

        partial void RefreshInfluenceVisualization()
        {
            UpdateZoneVisualization();
        }

        #endregion

        #region Texture Display Updates

        partial void RefreshTextureDisplays()
        {
            UpdatePrimaryTextureDisplay();
            UpdateSecondaryTextureDisplay();
            UpdateTertiaryTextureDisplay();
        }

        private void UpdatePrimaryTextureDisplay()
        {
            var layer = CurrentLayer;
            var assets = CurrentTerrain3DAssets;

            if (layer == null || assets == null || _primaryTexturePreview == null) return;

            var info = GetTextureInfo(assets, layer.TextureIndex);
            if (info.HasValue)
            {
                _primaryTextureLabel.Text = $"[{info.Value.id}] {info.Value.name}";
                _primaryTexturePreview.Texture = info.Value.thumbnail;
            }
            else
            {
                _primaryTextureLabel.Text = $"[{layer.TextureIndex}] Unknown";
                _primaryTexturePreview.Texture = null;
            }
        }

        private void UpdateSecondaryTextureDisplay()
        {
            var layer = CurrentLayer;
            var assets = CurrentTerrain3DAssets;

            if (layer == null || _secondaryTexturePreview == null) return;

            if (layer.SecondaryTextureIndex < 0)
            {
                _secondaryTextureLabel.Text = "Disabled";
                _secondaryTexturePreview.Texture = null;
                return;
            }

            if (assets == null)
            {
                _secondaryTextureLabel.Text = "Assets unavailable";
                return;
            }

            var info = GetTextureInfo(assets, layer.SecondaryTextureIndex);
            if (info.HasValue)
            {
                _secondaryTextureLabel.Text = $"[{info.Value.id}] {info.Value.name}";
                _secondaryTexturePreview.Texture = info.Value.thumbnail;
            }
            else
            {
                _secondaryTextureLabel.Text = $"[{layer.SecondaryTextureIndex}] Unknown";
                _secondaryTexturePreview.Texture = null;
            }
        }

        private void UpdateTertiaryTextureDisplay()
        {
            var layer = CurrentLayer;
            var assets = CurrentTerrain3DAssets;

            if (layer == null || _tertiaryTexturePreview == null) return;

            if (layer.TertiaryTextureIndex < 0)
            {
                _tertiaryTextureLabel.Text = "Disabled";
                _tertiaryTexturePreview.Texture = null;
                return;
            }

            if (assets == null)
            {
                _tertiaryTextureLabel.Text = "Assets unavailable";
                return;
            }

            var info = GetTextureInfo(assets, layer.TertiaryTextureIndex);
            if (info.HasValue)
            {
                _tertiaryTextureLabel.Text = $"[{info.Value.id}] {info.Value.name}";
                _tertiaryTexturePreview.Texture = info.Value.thumbnail;
            }
            else
            {
                _tertiaryTextureLabel.Text = $"[{layer.TertiaryTextureIndex}] Unknown";
                _tertiaryTexturePreview.Texture = null;
            }
        }

        #endregion

        #region Event Handlers

        private void OnGradientModeToggled(bool enabled)
        {
            var layer = CurrentLayer;
            if (layer == null) return;

            if (_gradientModeContainer != null)
            {
                _gradientModeContainer.Visible = enabled;

                if (enabled && _gradientModeContainer.GetChildCount() == 0)
                {
                    BuildGradientModeContent(layer, CurrentTerrain3DAssets);
                }
            }
        }

        private void OnPrimarySelectPressed()
        {
            var layer = CurrentLayer;
            var assets = CurrentTerrain3DAssets;
            if (layer == null || assets == null) return;

            TerrainAssetSelector.ShowTexturePopup(layer, assets, layer.TextureIndex, (id) =>
            {
                layer.TextureIndex = id;
                UpdatePrimaryTextureDisplay();
                UpdateZoneVisualization();
            });
        }

        private void OnSecondarySelectPressed()
        {
            var layer = CurrentLayer;
            var assets = CurrentTerrain3DAssets;
            if (layer == null || assets == null) return;

            int currentId = layer.SecondaryTextureIndex >= 0 ? layer.SecondaryTextureIndex : -1;

            TerrainAssetSelector.ShowTexturePopup(layer, assets, currentId, (id) =>
            {
                layer.SecondaryTextureIndex = id;
                UpdateSecondaryTextureDisplay();
                RebuildGradientModeContent();
            });
        }

        private void OnSecondaryClearPressed()
        {
            var layer = CurrentLayer;
            if (layer == null) return;

            layer.SecondaryTextureIndex = -1;
            UpdateSecondaryTextureDisplay();
            RebuildGradientModeContent();
        }

        private void OnTertiarySelectPressed()
        {
            var layer = CurrentLayer;
            var assets = CurrentTerrain3DAssets;
            if (layer == null || assets == null) return;

            int currentId = layer.TertiaryTextureIndex >= 0 ? layer.TertiaryTextureIndex : -1;

            TerrainAssetSelector.ShowTexturePopup(layer, assets, currentId, (id) =>
            {
                layer.TertiaryTextureIndex = id;
                UpdateTertiaryTextureDisplay();
                RebuildGradientModeContent();
            });
        }

        private void OnTertiaryClearPressed()
        {
            var layer = CurrentLayer;
            if (layer == null) return;

            layer.TertiaryTextureIndex = -1;
            UpdateTertiaryTextureDisplay();
            RebuildGradientModeContent();
        }

        private void RebuildGradientModeContent()
        {
            var layer = CurrentLayer;
            var assets = CurrentTerrain3DAssets;
            if (layer == null || assets == null || _gradientModeContainer == null) return;

            BuildGradientModeContent(layer, assets);
            UpdateZoneVisualization();
        }

        #endregion
    }
    /// <summary>
    /// Extension to free all children of a container.
    /// </summary>
    internal static class ContainerExtensions
    {
        public static void QueueFreeChildren(this Control container)
        {
            if (container == null) return;

            foreach (var child in container.GetChildren())
            {
                if (child is Node node && GodotObject.IsInstanceValid(node))
                {
                    node.QueueFree();
                }
            }
        }
    }
    
}