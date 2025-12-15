// /Editor/PathLayerInspector.Profile.cs
using System;
using Godot;
using Terrain3DTools.Layers.Path;
using Terrain3DTools.Editor.Utils;

namespace Terrain3DTools.Editor
{
    public partial class PathLayerInspector
    {
        private Label _pathTypeDescriptionLabel;
        private Label _profileNameLabel;
        private PathProfilePreviewDrawArea _profilePreviewDrawArea;

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
        }

        private void OnPathTypeSelected(long index)
        {
            var layer = CurrentLayer;
            if (layer == null) return;

            var newType = (PathType)(int)index;
            if (newType == layer.PathType) return;

            // Disconnect from old profile
            if (_profileSignalConnected && layer.Profile != null)
            {
                layer.Profile.Changed -= OnProfileChanged;
                _profileSignalConnected = false;
            }

            // Apply new type
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
            RefreshProfileUI();
        }

        private void AddProfileSection()
        {
            CreateCollapsibleSection("Cross-Section Profile", true);
            var content = GetSectionContent("Cross-Section Profile");
            var layer = CurrentLayer;
            
            if (content == null || layer == null) return;

            // Header Row
            var headerContainer = new HBoxContainer();

            _profileNameLabel = new Label
            {
                Text = layer.Profile?.Name ?? "No Profile",
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
            };
            headerContainer.AddChild(_profileNameLabel);

            var resetButton = new Button
            {
                Text = "Reset to Default",
                TooltipText = "Reset profile to current path type's default"
            };
            resetButton.Pressed += () =>
            {
                if (CurrentLayer == null) return;
                
                // Disconnect, Reset, Reconnect
                if (_profileSignalConnected && CurrentLayer.Profile != null)
                {
                    CurrentLayer.Profile.Changed -= OnProfileChanged;
                }

                CurrentLayer.ResetProfileToDefault = true;

                if (CurrentLayer.Profile != null)
                {
                    CurrentLayer.Profile.Changed += OnProfileChanged;
                    _profileSignalConnected = true;
                }

                _selectedZoneIndex = 0;
                RefreshProfileUI();
            };
            headerContainer.AddChild(resetButton);

            content.AddChild(headerContainer);

            // Preview Area
            var previewContainer = new PanelContainer
            {
                CustomMinimumSize = new Vector2(0, PROFILE_PREVIEW_HEIGHT)
            };
            var bg = new StyleBoxFlat { BgColor = new Color(0.15f, 0.15f, 0.15f) };
            previewContainer.AddThemeStyleboxOverride("panel", bg);

            _profilePreviewDrawArea = new PathProfilePreviewDrawArea(layer, () => _selectedZoneIndex);
            previewContainer.AddChild(_profilePreviewDrawArea);
            content.AddChild(previewContainer);

            // Zones List Header
            var zoneListLabel = new Label { Text = "Zones (center → edge):" };
            zoneListLabel.AddThemeColorOverride("font_color", new Color(0.8f, 0.8f, 0.8f));
            content.AddChild(zoneListLabel);

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

        private void UpdateProfileHeader()
        {
            var layer = CurrentLayer;
            if (layer == null) return;

            if (IsInstanceValid(_profileNameLabel))
            {
                _profileNameLabel.Text = layer.Profile?.Name ?? "No Profile";
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
    }
}