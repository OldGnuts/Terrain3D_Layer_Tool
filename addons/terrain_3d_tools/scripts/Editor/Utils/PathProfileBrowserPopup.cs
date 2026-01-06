// /Editor/Utils/PathProfileBrowserPopup.cs
using System;
using System.Collections.Generic;
using Godot;

using Terrain3DTools.Layers.Path;

namespace Terrain3DTools.Editor.Utils
{
    /// <summary>
    /// Popup window for browsing, selecting, saving, and managing path profiles.
    /// </summary>
    public partial class PathProfileBrowserPopup : Window
    {
        #region Signals
        [Signal]
        public delegate void ProfileSelectedEventHandler(PathProfile profile, bool isNewEmpty);

        [Signal]
        public delegate void ProfileSavedEventHandler(string profileName);
        #endregion

        #region Fields
        private PathProfile _currentProfile;
        private bool _saveMode;
        private ItemList _profileList;
        private LineEdit _saveNameEdit;
        private Button _actionButton;
        private Button _deleteButton;
        private Label _previewLabel;
        private List<SavedProfileInfo> _profiles;
        private int _selectedIndex = -1;
        #endregion

        #region Initialization

        /// <summary>
        /// Creates and shows the browser in Load mode.
        /// </summary>
        public static PathProfileBrowserPopup ShowLoadBrowser(Control parent, Action<PathProfile, bool> onSelected)
        {
            var popup = new PathProfileBrowserPopup();
            popup._saveMode = false;
            popup._currentProfile = null;

            parent.AddChild(popup);
            popup.BuildUI();

            popup.ProfileSelected += (profile, isNew) =>
            {
                onSelected?.Invoke(profile, isNew);
                popup.Hide();
                popup.QueueFree();
            };

            popup.CloseRequested += () =>
            {
                popup.Hide();
                popup.QueueFree();
            };

            popup.PopupCentered();
            return popup;
        }

        /// <summary>
        /// Creates and shows the browser in Save mode.
        /// </summary>
        public static PathProfileBrowserPopup ShowSaveBrowser(Control parent, PathProfile profileToSave, Action<string> onSaved)
        {
            var popup = new PathProfileBrowserPopup();
            popup._saveMode = true;
            popup._currentProfile = profileToSave;

            parent.AddChild(popup);
            popup.BuildUI();

            popup.ProfileSaved += (name) =>
            {
                onSaved?.Invoke(name);
                popup.Hide();
                popup.QueueFree();
            };

            popup.CloseRequested += () =>
            {
                popup.Hide();
                popup.QueueFree();
            };

            popup.PopupCentered();
            return popup;
        }

        private void BuildUI()
        {
            Title = _saveMode ? "Save Profile" : "Load Profile";
            Size = new Vector2I(450, 400);
            Exclusive = true;
            Transient = true;

            var margin = new MarginContainer();
            margin.SetAnchorsPreset(Godot.Control.LayoutPreset.FullRect);
            margin.AddThemeConstantOverride("margin_left", 12);
            margin.AddThemeConstantOverride("margin_right", 12);
            margin.AddThemeConstantOverride("margin_top", 12);
            margin.AddThemeConstantOverride("margin_bottom", 12);

            var vbox = new VBoxContainer();
            vbox.AddThemeConstantOverride("separation", 8);

            // Header
            var headerLabel = new Label
            {
                Text = _saveMode
                    ? "Save your profile for reuse. Enter a name or select an existing profile to overwrite."
                    : "Select a profile to load, or create a new empty profile.",
                AutowrapMode = TextServer.AutowrapMode.Word
            };
            headerLabel.AddThemeColorOverride("font_color", new Color(0.7f, 0.7f, 0.7f));
            headerLabel.AddThemeFontSizeOverride("font_size", 12);
            vbox.AddChild(headerLabel);

            // Save name input (only in save mode)
            if (_saveMode)
            {
                var nameRow = new HBoxContainer();
                nameRow.AddChild(new Label { Text = "Name:", CustomMinimumSize = new Vector2(50, 0) });

                _saveNameEdit = new LineEdit
                {
                    Text = _currentProfile?.Name ?? "My Profile",
                    SizeFlagsHorizontal = Godot.Control.SizeFlags.ExpandFill,
                    PlaceholderText = "Enter profile name"
                };
                _saveNameEdit.TextChanged += OnSaveNameChanged;
                nameRow.AddChild(_saveNameEdit);

                vbox.AddChild(nameRow);
            }

            // Profile list
            var listLabel = new Label { Text = _saveMode ? "Existing Profiles (select to overwrite):" : "Saved Profiles:" };
            listLabel.AddThemeColorOverride("font_color", new Color(0.8f, 0.8f, 0.8f));
            vbox.AddChild(listLabel);

            _profileList = new ItemList
            {
                CustomMinimumSize = new Vector2(0, 180),
                SizeFlagsVertical = Godot.Control.SizeFlags.ExpandFill,
                SelectMode = ItemList.SelectModeEnum.Single
            };
            _profileList.ItemSelected += OnProfileSelected;
            _profileList.ItemActivated += OnProfileActivated;
            vbox.AddChild(_profileList);

            // Preview info
            _previewLabel = new Label
            {
                Text = "",
                AutowrapMode = TextServer.AutowrapMode.Word
            };
            _previewLabel.AddThemeColorOverride("font_color", new Color(0.6f, 0.8f, 1.0f));
            _previewLabel.AddThemeFontSizeOverride("font_size", 11);
            vbox.AddChild(_previewLabel);

            // Buttons
            var buttonRow = new HBoxContainer();
            buttonRow.AddThemeConstantOverride("separation", 8);

            if (!_saveMode)
            {
                var newEmptyBtn = new Button
                {
                    Text = "New Empty Profile",
                    TooltipText = "Create a new profile from scratch"
                };
                newEmptyBtn.Pressed += OnNewEmptyPressed;
                buttonRow.AddChild(newEmptyBtn);
            }

            _deleteButton = new Button
            {
                Text = "Delete",
                TooltipText = "Delete the selected profile",
                Disabled = true
            };
            _deleteButton.Pressed += OnDeletePressed;
            buttonRow.AddChild(_deleteButton);

            buttonRow.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }); // Spacer

            var cancelBtn = new Button { Text = "Cancel" };
            cancelBtn.Pressed += () =>
            {
                Hide();
                QueueFree();
            };
            buttonRow.AddChild(cancelBtn);

            _actionButton = new Button
            {
                Text = _saveMode ? "Save" : "Load",
                Disabled = !_saveMode // In load mode, need selection first
            };
            _actionButton.Pressed += OnActionPressed;
            buttonRow.AddChild(_actionButton);

            vbox.AddChild(buttonRow);

            margin.AddChild(vbox);
            AddChild(margin);

            // Populate list
            RefreshProfileList();
        }

        #endregion

        #region List Management

        private void RefreshProfileList()
        {
            _profileList.Clear();
            _profiles = PathProfileManager.GetSavedProfiles();

            foreach (var info in _profiles)
            {
                _profileList.AddItem(info.GetDisplayText());
            }

            _selectedIndex = -1;
            _deleteButton.Disabled = true;

            if (!_saveMode)
            {
                _actionButton.Disabled = true;
            }

            UpdatePreview();
        }

        private void OnProfileSelected(long index)
        {
            _selectedIndex = (int)index;
            _deleteButton.Disabled = false;

            if (!_saveMode)
            {
                _actionButton.Disabled = false;
            }
            else if (_selectedIndex >= 0 && _selectedIndex < _profiles.Count)
            {
                // In save mode, populate name field with selected profile name
                _saveNameEdit.Text = _profiles[_selectedIndex].Name;
            }

            UpdatePreview();
        }

        private void OnProfileActivated(long index)
        {
            // Double-click to load/save
            _selectedIndex = (int)index;
            OnActionPressed();
        }

        private void UpdatePreview()
        {
            if (_selectedIndex < 0 || _selectedIndex >= _profiles.Count)
            {
                _previewLabel.Text = "";
                return;
            }

            var info = _profiles[_selectedIndex];
            _previewLabel.Text = $"📁 {info.Name}\n" +
                                 $"   Zones: {info.ZoneCount}\n" +
                                 $"   Total Width: {info.TotalWidth:F1}m";
        }

        #endregion

        #region Event Handlers

        private void OnSaveNameChanged(string newText)
        {
            _actionButton.Disabled = string.IsNullOrWhiteSpace(newText);
        }

        private void OnNewEmptyPressed()
        {
            // Create a minimal empty profile
            var emptyProfile = PathPresets.CreateMinimalCustom();
            emptyProfile.Name = "Custom Profile";

            EmitSignal(SignalName.ProfileSelected, emptyProfile, true);
        }

        private void OnDeletePressed()
        {
            if (_selectedIndex < 0 || _selectedIndex >= _profiles.Count) return;

            var info = _profiles[_selectedIndex];

            // Show confirmation dialog
            var dialog = new ConfirmationDialog
            {
                DialogText = $"Are you sure you want to delete '{info.Name}'?\nThis cannot be undone.",
                Title = "Delete Profile"
            };

            dialog.Confirmed += () =>
            {
                PathProfileManager.DeleteProfile(info.FullPath);
                RefreshProfileList();
                dialog.QueueFree();
            };

            dialog.Canceled += () => dialog.QueueFree();

            AddChild(dialog);
            dialog.PopupCentered();
        }

        private void OnActionPressed()
        {
            if (_saveMode)
            {
                // Save mode
                string name = _saveNameEdit.Text.Trim();
                if (string.IsNullOrWhiteSpace(name)) return;

                // Check for overwrite
                if (PathProfileManager.ProfileExists(name))
                {
                    var dialog = new ConfirmationDialog
                    {
                        DialogText = $"A profile named '{name}' already exists.\nDo you want to overwrite it?",
                        Title = "Overwrite Profile"
                    };

                    dialog.Confirmed += () =>
                    {
                        if (PathProfileManager.SaveProfile(_currentProfile, name))
                        {
                            EmitSignal(SignalName.ProfileSaved, name);
                        }
                        dialog.QueueFree();
                    };

                    dialog.Canceled += () => dialog.QueueFree();

                    AddChild(dialog);
                    dialog.PopupCentered();
                }
                else
                {
                    if (PathProfileManager.SaveProfile(_currentProfile, name))
                    {
                        EmitSignal(SignalName.ProfileSaved, name);
                    }
                }
            }
            else
            {
                // Load mode
                if (_selectedIndex < 0 || _selectedIndex >= _profiles.Count) return;

                var info = _profiles[_selectedIndex];
                var profile = PathProfileManager.LoadProfile(info.FullPath);

                if (profile != null)
                {
                    EmitSignal(SignalName.ProfileSelected, profile, false);
                }
            }
        }

        #endregion
    }
}