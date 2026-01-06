// /Editor/PathLayerInspector.Profile.cs
using System;
using System.Collections.Generic;
using Godot;
using Terrain3DTools.Layers.Path;
using Terrain3DTools.Editor.Utils;

namespace Terrain3DTools.Editor
{
    public partial class PathLayerInspector
    {
        #region Profile Fields
        private Label _pathTypeDescriptionLabel;
        private Label _profileNameLabel;
        private PathProfilePreviewDrawArea _profilePreviewDrawArea;
        private bool _profileModified = false;
        private PathProfile _lastSavedProfileSnapshot;
        #endregion

        #region Path Type Selector
        private void AddPathTypeSelector()
        {
            CreateCollapsibleSection("Path Type", true);
            var content = GetSectionContent("Path Type");
            var layer = CurrentLayer;

            if (content == null || layer == null) return;

            var container = new HBoxContainer();
            container.AddThemeConstantOverride("separation", 8);

            var typeLabel = new Label { Text = "Type:", CustomMinimumSize = new Vector2(80, 0) };
            container.AddChild(typeLabel);

            var dropdown = new OptionButton { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            foreach (PathType type in Enum.GetValues(typeof(PathType)))
            {
                dropdown.AddItem(PathPresets.GetDisplayName(type), (int)type);
            }

            dropdown.Selected = (int)layer.PathType;
            dropdown.Connect("item_selected", Callable.From<long>(OnPathTypeSelected));
            container.AddChild(dropdown);

            content.AddChild(container);

            _pathTypeDescriptionLabel = new Label
            {
                Text = PathPresets.GetDescription(layer.PathType),
                AutowrapMode = TextServer.AutowrapMode.Word,
                CustomMinimumSize = new Vector2(0, 40)
            };
            _pathTypeDescriptionLabel.AddThemeColorOverride("font_color", new Color(0.7f, 0.7f, 0.7f));
            _pathTypeDescriptionLabel.AddThemeFontSizeOverride("font_size", 12);
            content.AddChild(_pathTypeDescriptionLabel);

            // Take initial snapshot for change detection
            TakeProfileSnapshot();
        }

        private void OnPathTypeSelected(long index)
        {
            var layer = CurrentLayer;
            if (layer == null) return;

            var newType = (PathType)(int)index;
            if (newType == layer.PathType) return;

            // Check if Custom was selected - open profile browser
            if (newType == PathType.Custom)
            {
                ShowLoadProfileBrowser();
                return;
            }

            // Check for unsaved changes
            if (HasProfileBeenModified())
            {
                ShowUnsavedChangesDialog(() =>
                {
                    // User chose to proceed without saving
                    ApplyPathTypeChange(layer, newType);
                });
            }
            else
            {
                ApplyPathTypeChange(layer, newType);
            }
        }

        private void ApplyPathTypeChange(Layers.PathLayer layer, PathType newType)
        {
            // Disconnect from old profile
            if (_profileSignalConnected && layer.Profile != null)
            {
                layer.Profile.Changed -= OnProfileChanged;
                _profileSignalConnected = false;
            }

            // Apply new type (this creates a new profile)
            layer.PathType = newType;

            // Connect to new profile
            if (layer.Profile != null)
            {
                layer.Profile.Changed += OnProfileChanged;
                _profileSignalConnected = true;
            }

            // Update UI
            if (IsInstanceValid(_pathTypeDescriptionLabel))
            {
                _pathTypeDescriptionLabel.Text = PathPresets.GetDescription(newType);
            }

            _selectedZoneIndex = 0;
            _profileModified = false;
            TakeProfileSnapshot();

            // Force full refresh
            RefreshProfileUI();
            ForceLayerUpdate(layer);
        }

        private void ShowLoadProfileBrowser()
        {
            var layer = CurrentLayer;
            if (layer == null) return;

            var editorBase = EditorInterface.Singleton?.GetBaseControl();
            if (editorBase == null) return;

            PathProfileBrowserPopup.ShowLoadBrowser(editorBase, (profile, isNewEmpty) =>
            {
                if (profile != null)
                {
                    ApplyLoadedProfile(layer, profile);
                }
            });
        }

        private void ApplyLoadedProfile(Layers.PathLayer layer, PathProfile profile)
        {
            // Disconnect from old profile
            if (_profileSignalConnected && layer.Profile != null)
            {
                layer.Profile.Changed -= OnProfileChanged;
                _profileSignalConnected = false;
            }

            // Apply the loaded profile
            layer.Profile = PathProfileManager.DuplicateProfile(profile);

            // Connect to new profile
            if (layer.Profile != null)
            {
                layer.Profile.Changed += OnProfileChanged;
                _profileSignalConnected = true;
            }

            _selectedZoneIndex = 0;
            _profileModified = false;
            TakeProfileSnapshot();

            // Force full refresh
            RefreshProfileUI();
            ForceLayerUpdate(layer);
        }

        /// <summary>
        /// Forces the PathLayer to fully update after a profile change.
        /// </summary>
        private void ForceLayerUpdate(Layers.PathLayer layer)
        {
            if (layer == null) return;

            // Invalidate caches to force recalculation
            layer.InvalidateCaches();

            // Mark as dirty to trigger regeneration
            layer.ForceDirty();
            layer.MarkPositionDirty();

            // Update visualization
            if (Engine.IsEditorHint())
            {
                // The visualization will update on the next frame via _Process
            }
        }
        #endregion

        #region Profile Section
        private void AddProfileSection()
        {
            // Build help content for the profile section
            string helpContent = EditorHelpTooltip.FormatHelpText(
                "The cross-section profile defines the shape of your path from center to edge. " +
                "Each zone represents a band of the path with its own height, texture, and blending settings.",
                new List<(string, string)>
                {
                    ("Zones", "Ordered bands from path center outward (e.g., Surface → Shoulder → Edge)"),
                    ("Profile Preview", "Visual representation of the path cross-section"),
                    ("Save/Load", "Save custom profiles for reuse across multiple paths"),
                    ("Reset", "Restore the default profile for the current path type")
                },
                "Tip: Start with a preset profile and adjust zone widths and heights to match your terrain scale."
            );

            CreateCollapsibleSection("Cross-Section Profile", false, "Cross-Section Profile Help", helpContent);
            var content = GetSectionContent("Cross-Section Profile");
            var layer = CurrentLayer;

            if (content == null || layer == null) return;

            // Header Row
            var headerContainer = new HBoxContainer();
            headerContainer.AddThemeConstantOverride("separation", 8);

            _profileNameLabel = new Label
            {
                Text = layer.Profile?.Name ?? "No Profile",
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
            };
            _profileNameLabel.AddThemeFontSizeOverride("font_size", 14);
            headerContainer.AddChild(_profileNameLabel);

            // Save Profile Button
            var saveButton = new Button
            {
                Text = "💾 Save",
                TooltipText = "Save this profile for reuse in other paths"
            };
            saveButton.Pressed += OnSaveProfilePressed;
            headerContainer.AddChild(saveButton);

            // Load Profile Button
            var loadButton = new Button
            {
                Text = "📂 Load",
                TooltipText = "Load a saved profile"
            };
            loadButton.Pressed += OnLoadProfilePressed;
            headerContainer.AddChild(loadButton);

            // Reset to Default Button
            var resetButton = new Button
            {
                Text = "Reset",
                TooltipText = "Reset profile to current path type's default settings"
            };
            resetButton.Pressed += OnResetProfilePressed;
            headerContainer.AddChild(resetButton);

            content.AddChild(headerContainer);

            // Preview Area
            var previewContainer = new PanelContainer
            {
                CustomMinimumSize = new Vector2(0, EditorConstants.PROFILE_PREVIEW_HEIGHT)
            };
            var bg = new StyleBoxFlat { BgColor = new Color(0.15f, 0.15f, 0.15f) };
            previewContainer.AddThemeStyleboxOverride("panel", bg);

            _profilePreviewDrawArea = new PathProfilePreviewDrawArea(layer, () => _selectedZoneIndex);
            previewContainer.AddChild(_profilePreviewDrawArea);
            content.AddChild(previewContainer);

            // Info text
            var infoLabel = new Label
            {
                Text = "Click a zone below to edit its properties. Zones are ordered from center → edge.",
                AutowrapMode = TextServer.AutowrapMode.Word
            };
            infoLabel.AddThemeColorOverride("font_color", new Color(0.6f, 0.6f, 0.6f));
            infoLabel.AddThemeFontSizeOverride("font_size", 11);
            content.AddChild(infoLabel);

            // Zone List
            _zoneListContainer = new HBoxContainer();
            _zoneListContainer.AddThemeConstantOverride("separation", 4);
            RefreshZoneListContent();

            var zoneScroll = new ScrollContainer
            {
                CustomMinimumSize = new Vector2(0, 40),
                HorizontalScrollMode = ScrollContainer.ScrollMode.Auto,
                VerticalScrollMode = ScrollContainer.ScrollMode.Disabled
            };
            zoneScroll.AddChild(_zoneListContainer);
            content.AddChild(zoneScroll);
        }

        private void OnSaveProfilePressed()
        {
            var layer = CurrentLayer;
            if (layer?.Profile == null) return;

            var editorBase = EditorInterface.Singleton?.GetBaseControl();
            if (editorBase == null) return;

            PathProfileBrowserPopup.ShowSaveBrowser(editorBase, layer.Profile, (savedName) =>
            {
                GD.Print($"Profile saved as: {savedName}");
                _profileModified = false;
                TakeProfileSnapshot();

                // Update the profile name display
                if (IsInstanceValid(_profileNameLabel))
                {
                    _profileNameLabel.Text = savedName;
                }
            });
        }

        private void OnLoadProfilePressed()
        {
            var layer = CurrentLayer;
            if (layer == null) return;

            // Check for unsaved changes
            if (HasProfileBeenModified())
            {
                ShowUnsavedChangesDialog(() =>
                {
                    ShowLoadProfileBrowser();
                });
            }
            else
            {
                ShowLoadProfileBrowser();
            }
        }

        private void OnResetProfilePressed()
        {
            var layer = CurrentLayer;
            if (layer == null) return;

            // Check for unsaved changes
            if (HasProfileBeenModified())
            {
                ShowUnsavedChangesDialog(() =>
                {
                    PerformProfileReset(layer);
                });
            }
            else
            {
                PerformProfileReset(layer);
            }
        }

        private void PerformProfileReset(Layers.PathLayer layer)
        {
            // Disconnect from old profile
            if (_profileSignalConnected && layer.Profile != null)
            {
                layer.Profile.Changed -= OnProfileChanged;
            }

            // Reset (this creates a new profile)
            layer.ResetProfileToDefault = true;

            // Connect to new profile
            if (layer.Profile != null)
            {
                layer.Profile.Changed += OnProfileChanged;
                _profileSignalConnected = true;
            }

            _selectedZoneIndex = 0;
            _profileModified = false;
            TakeProfileSnapshot();

            RefreshProfileUI();
            ForceLayerUpdate(layer);
        }

        private void UpdateProfileHeader()
        {
            var layer = CurrentLayer;
            if (layer == null) return;

            if (IsInstanceValid(_profileNameLabel))
            {
                string name = layer.Profile?.Name ?? "No Profile";
                if (_profileModified)
                {
                    name += " *"; // Indicate unsaved changes
                }
                _profileNameLabel.Text = name;
            }

            if (IsInstanceValid(_widthLabel))
            {
                _widthLabel.Text = $"{layer.ProfileTotalWidth:F1} units";
            }
        }

        private void RefreshProfilePreview()
        {
            if (IsInstanceValid(_profilePreviewDrawArea))
            {
                _profilePreviewDrawArea.QueueRedraw();
            }
        }
        #endregion

        #region Profile Change Detection

        private void TakeProfileSnapshot()
        {
            var layer = CurrentLayer;
            if (layer?.Profile == null)
            {
                _lastSavedProfileSnapshot = null;
                return;
            }

            _lastSavedProfileSnapshot = PathProfileManager.DuplicateProfile(layer.Profile);
            _profileModified = false;
        }

        private bool HasProfileBeenModified()
        {
            // If profile changed signal has been received, consider it modified
            // This is a simple approach - for more accuracy, we'd compare zone by zone
            return _profileModified;
        }

        /// <summary>
        /// Called when the profile emits Changed signal.
        /// </summary>
        private void MarkProfileModified()
        {
            _profileModified = true;
            UpdateProfileHeader(); // Update to show "*" indicator
        }

        private void ShowUnsavedChangesDialog(Action onProceed)
        {
            var layer = CurrentLayer;
            if (layer == null)
            {
                onProceed?.Invoke();
                return;
            }

            var dialog = new ConfirmationDialog
            {
                Title = "Unsaved Profile Changes",
                DialogText = "You have unsaved changes to the current profile.\n\n" +
                            "Would you like to save before continuing?",
                OkButtonText = "Don't Save",
                CancelButtonText = "Cancel"
            };

            // Add a Save button
            dialog.AddButton("Save", true, "save");

            dialog.CustomAction += (action) =>
            {
                if (action == "save")
                {
                    // Show save dialog, then proceed
                    var editorBase = EditorInterface.Singleton?.GetBaseControl();
                    if (editorBase != null && layer.Profile != null)
                    {
                        PathProfileBrowserPopup.ShowSaveBrowser(editorBase, layer.Profile, (savedName) =>
                        {
                            _profileModified = false;
                            TakeProfileSnapshot();
                            onProceed?.Invoke();
                        });
                    }
                    dialog.Hide();
                    dialog.QueueFree();
                }
            };

            dialog.Confirmed += () =>
            {
                // "Don't Save" was clicked
                onProceed?.Invoke();
                dialog.QueueFree();
            };

            dialog.Canceled += () =>
            {
                dialog.QueueFree();
            };

            var editorBase2 = EditorInterface.Singleton?.GetBaseControl();
            editorBase2?.AddChild(dialog);
            dialog.PopupCentered();
        }
        #endregion
    }
}