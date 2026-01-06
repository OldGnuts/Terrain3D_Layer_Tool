// /Editor/TextureLayerInspector.Exclusions.cs
using System;
using System.Linq;
using Godot;
using Terrain3DTools.Layers;
using Terrain3DTools.Editor.Utils;
using TokisanGames;

namespace Terrain3DTools.Editor
{
    public partial class TextureLayerInspector
    {
        #region UI References - Exclusions
        private Label _exclusionHeaderLabel;
        private HFlowContainer _exclusionGrid;
        private Button _addExclusionButton;
        #endregion

        #region Constants - Exclusions
        private const int EXCLUSION_THUMBNAIL_SIZE = 48;
        #endregion

        partial void BuildExclusionsSection()
        {
            var layer = CurrentLayer;
            if (layer == null) return;
            
            // Hide exclusions in gradient mode (not currently supported)
            if (layer.GradientModeEnabled)
            {
                return;
            }
            // Create section with dynamic count in title
            int exclusionCount = layer.ExcludedTextureIds?.Count ?? 0;
            string sectionTitle = GetExclusionSectionTitle(exclusionCount);

            // We need to handle the section title specially since it changes
            bool isExpanded = _sectionExpanded.TryGetValue("Exclusions", out var expanded) ? expanded : false;

            var result = EditorUIUtils.CreateCollapsibleSection(
                _mainContainer,
                sectionTitle,
                isExpanded,
                _sectionExpanded
            );

            // Store reference for title updates
            _exclusionHeaderLabel = FindSectionHeaderButton(result.container);

            var section = result.container;
            _sectionContents["Exclusions"] = section;

            // === DESCRIPTION ===
            var descriptionLabel = new Label
            {
                Text = "Textures that won't be overwritten by this layer.\nClick a texture to remove it from the list.",
                Modulate = new Color(0.6f, 0.6f, 0.6f),
                AutowrapMode = TextServer.AutowrapMode.Word
            };
            descriptionLabel.AddThemeFontSizeOverride("font_size", 11);
            section.AddChild(descriptionLabel);

            EditorUIUtils.AddSeparator(section, 8);

            // === ADD BUTTON ===
            var buttonRow = new HBoxContainer();
            buttonRow.AddThemeConstantOverride("separation", 8);

            _addExclusionButton = new Button
            {
                Text = "+ Add Exclusion...",
                SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin
            };
            _addExclusionButton.Pressed += OnAddExclusionPressed;
            buttonRow.AddChild(_addExclusionButton);

            // Clear all button (only show if there are exclusions)
            if (exclusionCount > 0)
            {
                var clearAllButton = new Button
                {
                    Text = "Clear All",
                    SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin
                };
                clearAllButton.AddThemeColorOverride("font_color", new Color(1.0f, 0.6f, 0.6f));
                clearAllButton.Pressed += OnClearAllExclusionsPressed;
                buttonRow.AddChild(clearAllButton);
            }

            section.AddChild(buttonRow);

            EditorUIUtils.AddSeparator(section, 8);

            // === EXCLUSION GRID ===
            _exclusionGrid = new HFlowContainer
            {
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
            };
            _exclusionGrid.AddThemeConstantOverride("h_separation", 8);
            _exclusionGrid.AddThemeConstantOverride("v_separation", 8);
            section.AddChild(_exclusionGrid);

            // Populate the grid
            PopulateExclusionGrid();

            // Empty state message
            if (exclusionCount == 0)
            {
                var emptyLabel = new Label
                {
                    Text = "No textures excluded",
                    Modulate = new Color(0.5f, 0.5f, 0.5f),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
                };
                emptyLabel.AddThemeFontSizeOverride("font_size", 12);
                emptyLabel.Name = "EmptyStateLabel";
                section.AddChild(emptyLabel);
            }
        }

        partial void RefreshExclusions()
        {
            var layer = CurrentLayer;
            if (layer == null) return;

            // Update section title with count
            UpdateExclusionSectionTitle();

            // Repopulate the grid
            PopulateExclusionGrid();

            // Update empty state visibility
            UpdateEmptyState();
        }

        #region Exclusion Grid Management
        /// <summary>
        /// Populates the exclusion grid with texture thumbnails.
        /// </summary>
        private void PopulateExclusionGrid()
        {
            if (_exclusionGrid == null) return;

            // Clear existing items
            foreach (var child in _exclusionGrid.GetChildren())
            {
                child.QueueFree();
            }

            var layer = CurrentLayer;
            var assets = CurrentTerrain3DAssets;

            if (layer == null || layer.ExcludedTextureIds == null || layer.ExcludedTextureIds.Count == 0)
                return;

            // Add a button for each excluded texture
            foreach (int textureId in layer.ExcludedTextureIds)
            {
                var button = CreateExclusionButton(textureId, assets);
                if (button != null)
                {
                    _exclusionGrid.AddChild(button);
                }
            }
        }

        /// <summary>
        /// Creates a clickable button for an excluded texture.
        /// </summary>
        private Button CreateExclusionButton(int textureId, Terrain3DAssets assets)
        {
            var button = new Button
            {
                CustomMinimumSize = new Vector2(EXCLUSION_THUMBNAIL_SIZE + 16, EXCLUSION_THUMBNAIL_SIZE + 28),
                TooltipText = $"ID: {textureId}\nClick to remove",
                IconAlignment = HorizontalAlignment.Center,
                VerticalIconAlignment = VerticalAlignment.Top,
                ExpandIcon = true,
                Text = textureId.ToString(),
                ClipText = true
            };

            // Try to get texture info and thumbnail
            if (assets != null)
            {
                var textureInfo = GetTextureInfo(assets, textureId);
                if (textureInfo.HasValue)
                {
                    button.TooltipText = $"[{textureId}] {textureInfo.Value.name}\nClick to remove";

                    // Create smaller thumbnail for grid
                    if (textureInfo.Value.thumbnail != null)
                    {
                        var img = textureInfo.Value.thumbnail.GetImage();
                        if (img != null)
                        {
                            img.Resize(EXCLUSION_THUMBNAIL_SIZE, EXCLUSION_THUMBNAIL_SIZE);
                            button.Icon = ImageTexture.CreateFromImage(img);
                        }
                    }
                }
            }

            // Apply exclusion style (reddish tint)
            ApplyExclusionButtonStyle(button);

            // Store texture ID for click handler
            int capturedId = textureId;
            button.Pressed += () => OnExclusionButtonPressed(capturedId);

            return button;
        }

        /// <summary>
        /// Applies the visual style for exclusion buttons.
        /// </summary>
        private void ApplyExclusionButtonStyle(Button button)
        {
            var style = new StyleBoxFlat
            {
                BgColor = new Color(0.5f, 0.2f, 0.2f, 0.6f),
                BorderWidthBottom = 2,
                BorderWidthTop = 2,
                BorderWidthLeft = 2,
                BorderWidthRight = 2,
                BorderColor = new Color(0.8f, 0.3f, 0.3f, 0.8f),
                CornerRadiusBottomLeft = 4,
                CornerRadiusBottomRight = 4,
                CornerRadiusTopLeft = 4,
                CornerRadiusTopRight = 4,
                ContentMarginLeft = 4,
                ContentMarginRight = 4,
                ContentMarginTop = 4,
                ContentMarginBottom = 4
            };

            var hoverStyle = new StyleBoxFlat
            {
                BgColor = new Color(0.6f, 0.25f, 0.25f, 0.8f),
                BorderWidthBottom = 2,
                BorderWidthTop = 2,
                BorderWidthLeft = 2,
                BorderWidthRight = 2,
                BorderColor = new Color(1.0f, 0.4f, 0.4f, 1.0f),
                CornerRadiusBottomLeft = 4,
                CornerRadiusBottomRight = 4,
                CornerRadiusTopLeft = 4,
                CornerRadiusTopRight = 4,
                ContentMarginLeft = 4,
                ContentMarginRight = 4,
                ContentMarginTop = 4,
                ContentMarginBottom = 4
            };

            button.AddThemeStyleboxOverride("normal", style);
            button.AddThemeStyleboxOverride("hover", hoverStyle);
            button.AddThemeStyleboxOverride("pressed", hoverStyle);
        }

        /// <summary>
        /// Updates the empty state label visibility.
        /// </summary>
        private void UpdateEmptyState()
        {
            var section = GetSectionContent("Exclusions");
            if (section == null) return;

            var layer = CurrentLayer;
            int count = layer?.ExcludedTextureIds?.Count ?? 0;

            // Find existing empty state label
            Label emptyLabel = null;
            foreach (var child in section.GetChildren())
            {
                if (child is Label label && label.Name == "EmptyStateLabel")
                {
                    emptyLabel = label;
                    break;
                }
            }

            if (count == 0)
            {
                // Show empty state
                if (emptyLabel == null)
                {
                    emptyLabel = new Label
                    {
                        Text = "No textures excluded",
                        Modulate = new Color(0.5f, 0.5f, 0.5f),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
                    };
                    emptyLabel.AddThemeFontSizeOverride("font_size", 12);
                    emptyLabel.Name = "EmptyStateLabel";
                    section.AddChild(emptyLabel);
                }
                else
                {
                    emptyLabel.Visible = true;
                }
            }
            else
            {
                // Hide empty state
                if (emptyLabel != null)
                {
                    emptyLabel.Visible = false;
                }
            }
        }
        #endregion

        #region Section Title Management
        /// <summary>
        /// Gets the section title with exclusion count.
        /// </summary>
        private string GetExclusionSectionTitle(int count)
        {
            if (count == 0)
                return "Exclusions";
            return $"Exclusions ({count})";
        }

        /// <summary>
        /// Updates the exclusion section title with current count.
        /// </summary>
        private void UpdateExclusionSectionTitle()
        {
            var layer = CurrentLayer;
            if (layer == null) return;

            int count = layer.ExcludedTextureIds?.Count ?? 0;
            string newTitle = GetExclusionSectionTitle(count);

            // Find the header button in the parent container
            var section = GetSectionContent("Exclusions");
            if (section?.GetParent() is VBoxContainer outerContainer)
            {
                foreach (var child in outerContainer.GetChildren())
                {
                    if (child is Button headerButton && headerButton.Text.Contains("Exclusions"))
                    {
                        // Preserve the expand/collapse indicator
                        bool isExpanded = section.Visible;
                        headerButton.Text = (isExpanded ? "▼ " : "▶ ") + newTitle;
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// Finds the header button for a collapsible section.
        /// </summary>
        private Label FindSectionHeaderButton(VBoxContainer sectionContent)
        {
            // The header button is a sibling in the parent container
            if (sectionContent?.GetParent() is VBoxContainer outerContainer)
            {
                foreach (var child in outerContainer.GetChildren())
                {
                    if (child is Button)
                    {
                        // Found the header button, but we return null since we don't need to store it
                        return null;
                    }
                }
            }
            return null;
        }
        #endregion

        #region Event Handlers - Exclusions
        /// <summary>
        /// Opens the texture picker to add an exclusion.
        /// </summary>
        private void OnAddExclusionPressed()
        {
            var layer = CurrentLayer;
            var assets = CurrentTerrain3DAssets;
            if (layer == null || assets == null) return;

            ShowExclusionPickerPopup(assets, layer.ExcludedTextureIds, (selectedId) =>
            {
                AddExclusion(selectedId);
            });
        }

        /// <summary>
        /// Removes an exclusion when its button is clicked.
        /// </summary>
        private void OnExclusionButtonPressed(int textureId)
        {
            RemoveExclusion(textureId);
        }

        /// <summary>
        /// Clears all exclusions.
        /// </summary>
        private void OnClearAllExclusionsPressed()
        {
            var layer = CurrentLayer;
            if (layer == null) return;

            layer.ExcludedTextureIds = new Godot.Collections.Array<int>();

            // Rebuild the entire section to update Clear All button visibility
            RebuildExclusionsSection();
        }
        #endregion

        #region Exclusion Management
        /// <summary>
        /// Adds a texture ID to the exclusion list.
        /// </summary>
        private void AddExclusion(int textureId)
        {
            var layer = CurrentLayer;
            if (layer == null) return;

            // Don't add duplicates
            if (layer.ExcludedTextureIds.Contains(textureId))
                return;

            // Create new array with added ID
            var newList = new Godot.Collections.Array<int>(layer.ExcludedTextureIds);
            newList.Add(textureId);
            layer.ExcludedTextureIds = newList;

            // Rebuild section to show Clear All button if this is the first exclusion
            if (newList.Count == 1)
            {
                RebuildExclusionsSection();
            }
            else
            {
                // Just update the grid and title
                PopulateExclusionGrid();
                UpdateExclusionSectionTitle();
                UpdateEmptyState();
            }
        }

        /// <summary>
        /// Removes a texture ID from the exclusion list.
        /// </summary>
        private void RemoveExclusion(int textureId)
        {
            var layer = CurrentLayer;
            if (layer == null) return;

            if (!layer.ExcludedTextureIds.Contains(textureId))
                return;

            // Create new array without the removed ID
            var newList = new Godot.Collections.Array<int>(
                layer.ExcludedTextureIds.Where(id => id != textureId)
            );
            layer.ExcludedTextureIds = newList;

            // Rebuild section to hide Clear All button if no more exclusions
            if (newList.Count == 0)
            {
                RebuildExclusionsSection();
            }
            else
            {
                // Just update the grid and title
                PopulateExclusionGrid();
                UpdateExclusionSectionTitle();
                UpdateEmptyState();
            }
        }

        /// <summary>
        /// Rebuilds the entire exclusions section.
        /// Used when we need to add/remove the Clear All button.
        /// </summary>
        private void RebuildExclusionsSection()
        {
            var section = GetSectionContent("Exclusions");
            if (section == null) return;

            // Find and remove the outer container
            var outerContainer = section.GetParent();
            if (outerContainer != null)
            {
                var parent = outerContainer.GetParent();
                int index = outerContainer.GetIndex();

                outerContainer.QueueFree();
                _sectionContents.Remove("Exclusions");

                // Rebuild in the next frame to allow QueueFree to complete
                Callable.From(() =>
                {
                    if (_mainContainer != null && IsInstanceValid(_mainContainer))
                    {
                        // Create a temporary container at the right position
                        var tempContainer = new VBoxContainer();
                        _mainContainer.AddChild(tempContainer);
                        _mainContainer.MoveChild(tempContainer, index);

                        // Build the new section
                        BuildExclusionsSectionAt(tempContainer, index);

                        tempContainer.QueueFree();
                    }
                }).CallDeferred();
            }
        }

        /// <summary>
        /// Builds the exclusions section at a specific position.
        /// </summary>
        private void BuildExclusionsSectionAt(Control placeholder, int index)
        {
            var layer = CurrentLayer;
            if (layer == null || _mainContainer == null) return;

            int exclusionCount = layer.ExcludedTextureIds?.Count ?? 0;
            string sectionTitle = GetExclusionSectionTitle(exclusionCount);
            bool isExpanded = _sectionExpanded.TryGetValue("Exclusions", out var expanded) ? expanded : false;

            // Create the section directly in main container
            var result = EditorUIUtils.CreateCollapsibleSection(
                _mainContainer,
                sectionTitle,
                isExpanded,
                _sectionExpanded
            );

            var section = result.container;
            _sectionContents["Exclusions"] = section;

            // Move to correct position
            var outerContainer = section.GetParent();
            if (outerContainer != null)
            {
                _mainContainer.MoveChild(outerContainer, index);
            }

            // Build content (same as BuildExclusionsSection but without creating the section)
            var descriptionLabel = new Label
            {
                Text = "Textures that won't be overwritten by this layer.\nClick a texture to remove it from the list.",
                Modulate = new Color(0.6f, 0.6f, 0.6f),
                AutowrapMode = TextServer.AutowrapMode.Word
            };
            descriptionLabel.AddThemeFontSizeOverride("font_size", 11);
            section.AddChild(descriptionLabel);

            EditorUIUtils.AddSeparator(section, 8);

            var buttonRow = new HBoxContainer();
            buttonRow.AddThemeConstantOverride("separation", 8);

            _addExclusionButton = new Button
            {
                Text = "+ Add Exclusion...",
                SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin
            };
            _addExclusionButton.Pressed += OnAddExclusionPressed;
            buttonRow.AddChild(_addExclusionButton);

            if (exclusionCount > 0)
            {
                var clearAllButton = new Button
                {
                    Text = "Clear All",
                    SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin
                };
                clearAllButton.AddThemeColorOverride("font_color", new Color(1.0f, 0.6f, 0.6f));
                clearAllButton.Pressed += OnClearAllExclusionsPressed;
                buttonRow.AddChild(clearAllButton);
            }

            section.AddChild(buttonRow);

            EditorUIUtils.AddSeparator(section, 8);

            _exclusionGrid = new HFlowContainer
            {
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
            };
            _exclusionGrid.AddThemeConstantOverride("h_separation", 8);
            _exclusionGrid.AddThemeConstantOverride("v_separation", 8);
            section.AddChild(_exclusionGrid);

            PopulateExclusionGrid();

            if (exclusionCount == 0)
            {
                var emptyLabel = new Label
                {
                    Text = "No textures excluded",
                    Modulate = new Color(0.5f, 0.5f, 0.5f),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
                };
                emptyLabel.AddThemeFontSizeOverride("font_size", 12);
                emptyLabel.Name = "EmptyStateLabel";
                section.AddChild(emptyLabel);
            }
        }
        #endregion

        #region Exclusion Picker Popup
        /// <summary>
        /// Shows a popup to select a texture for exclusion.
        /// Filters out already-excluded textures and the primary/secondary textures.
        /// </summary>
        private void ShowExclusionPickerPopup(
            Terrain3DAssets assets,
            Godot.Collections.Array<int> currentExclusions,
            Action<int> onSelected)
        {
            var layer = CurrentLayer;
            if (layer == null || assets == null) return;

            var popup = new PopupPanel();
            popup.Size = new Vector2I(420, 320);

            var vbox = new VBoxContainer();
            vbox.AddThemeConstantOverride("separation", 8);

            var titleLabel = new Label
            {
                Text = "Select Texture to Exclude",
                HorizontalAlignment = HorizontalAlignment.Center
            };
            titleLabel.AddThemeFontSizeOverride("font_size", 14);
            vbox.AddChild(titleLabel);

            var hintLabel = new Label
            {
                Text = "Grayed out textures are already excluded or in use",
                Modulate = new Color(0.6f, 0.6f, 0.6f),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            hintLabel.AddThemeFontSizeOverride("font_size", 11);
            vbox.AddChild(hintLabel);

            var scroll = new ScrollContainer
            {
                CustomMinimumSize = new Vector2(400, 240),
                SizeFlagsVertical = Control.SizeFlags.ExpandFill
            };

            var grid = new GridContainer { Columns = 6 };
            grid.AddThemeConstantOverride("h_separation", 4);
            grid.AddThemeConstantOverride("v_separation", 4);

            for (int i = 0; i < assets.TextureList.Count; i++)
            {
                var resource = assets.TextureList[i].As<Resource>();
                if (resource == null) continue;

                var asset = Terrain3DTextureAsset.Bind(resource);
                if (asset == null) continue;

                int textureId = (int)asset.Id;

                // Check if this texture should be disabled
                bool isExcluded = currentExclusions?.Contains(textureId) ?? false;
                bool isPrimary = textureId == layer.TextureIndex;
                bool isSecondary = layer.GradientModeEnabled && textureId == layer.SecondaryTextureIndex;
                bool isDisabled = isExcluded || isPrimary || isSecondary;

                var button = new Button
                {
                    CustomMinimumSize = new Vector2(EXCLUSION_THUMBNAIL_SIZE + 8, EXCLUSION_THUMBNAIL_SIZE + 24),
                    Text = textureId.ToString(),
                    IconAlignment = HorizontalAlignment.Center,
                    VerticalIconAlignment = VerticalAlignment.Top,
                    Disabled = isDisabled
                };

                // Build tooltip
                string tooltip = $"[{textureId}] {asset.Name}";
                if (isExcluded) tooltip += "\n(Already excluded)";
                if (isPrimary) tooltip += "\n(Primary texture)";
                if (isSecondary) tooltip += "\n(Secondary texture)";
                button.TooltipText = tooltip;

                // Set thumbnail
                var thumb = asset.GetThumbnail();
                if (thumb != null)
                {
                    var img = thumb.GetImage();
                    img?.Resize(EXCLUSION_THUMBNAIL_SIZE, EXCLUSION_THUMBNAIL_SIZE);
                    button.Icon = img != null ? ImageTexture.CreateFromImage(img) : null;
                }

                // Style disabled buttons
                if (isDisabled)
                {
                    button.Modulate = new Color(0.5f, 0.5f, 0.5f, 0.7f);
                }

                int capturedId = textureId;
                button.Pressed += () =>
                {
                    onSelected?.Invoke(capturedId);
                    popup.Hide();
                    popup.QueueFree();
                };

                grid.AddChild(button);
            }

            scroll.AddChild(grid);
            vbox.AddChild(scroll);

            // Cancel button
            var cancelButton = new Button
            {
                Text = "Cancel",
                SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter
            };
            cancelButton.Pressed += () =>
            {
                popup.Hide();
                popup.QueueFree();
            };
            vbox.AddChild(cancelButton);

            popup.AddChild(vbox);

            var editorBase = EditorInterface.Singleton.GetBaseControl();
            editorBase.AddChild(popup);
            popup.PopupCentered();
        }
        #endregion
    }
}