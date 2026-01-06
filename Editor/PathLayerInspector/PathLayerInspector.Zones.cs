// /Editor/PathLayerInspector.Zones.cs
using System;
using System.Collections.Generic;
using Godot;
using Terrain3DTools.Layers.Path;
using Terrain3DTools.Layers;
using Terrain3DTools.Editor.Utils;
using Terrain3DTools.Utils;
using TokisanGames;

namespace Terrain3DTools.Editor
{
    public partial class PathLayerInspector
    {
        #region Zone Fields
        private VBoxContainer _zoneEditorContainer;
        private HBoxContainer _zoneListContainer;
        private int _selectedZoneIndex = 0;
        #endregion

        #region Zone Editor Setup
        private void AddZoneEditor()
        {
            // Build help content for the zone editor
            string helpContent = EditorHelpTooltip.FormatHelpText(
                "Edit individual zone properties to customize your path's appearance and terrain modification.",
                new List<(string, string)>
                {
            ("Zone Type", "Determines default behavior (Center, Shoulder, Edge, Wall, etc.)"),
            ("Width", "How wide this zone extends from the previous zone boundary"),
            ("Height Offset", "Vertical displacement from the path's base height"),
            ("Strength", "How strongly this zone modifies the terrain (0 = no effect, 1 = full effect)"),
            ("Conform", "How much the zone follows existing terrain vs. flattening it (0 = flat, 1 = follow terrain)"),
            ("Blend Mode", "How height modifications combine with terrain (Replace, Add, Blend, etc.)"),
            ("Height Curve", "Shape of the height transition across this zone"),
            ("Texture", "Terrain texture to paint in this zone (-1 = no change)")
                },
                "Tip: Use higher Conform values for natural paths that follow terrain, lower values for engineered surfaces like roads."
            );

            CreateCollapsibleSection("Zone Editor", false, "Zone Editor Help", helpContent);
            _zoneEditorContainer = GetSectionContent("Zone Editor");
            RefreshZoneEditorContent();
        }

        private void RefreshZoneList()
        {
            if (!IsInstanceValid(_zoneListContainer)) return;
            RefreshZoneListContent();
        }

        private void RefreshZoneListContent()
        {
            if (!IsInstanceValid(_zoneListContainer)) return;

            foreach (Node c in _zoneListContainer.GetChildren())
                c.QueueFree();

            var layer = CurrentLayer;
            if (layer?.Profile?.Zones == null) return;

            for (int i = 0; i < layer.Profile.Zones.Count; i++)
            {
                var zone = layer.Profile.Zones[i];
                if (zone == null) continue;
                _zoneListContainer.AddChild(CreateZoneButton(zone, i));
            }

            var addBtn = new Button
            {
                Text = "+",
                TooltipText = "Add new zone",
                CustomMinimumSize = new Vector2(EditorConstants.ZONE_BUTTON_MIN_HEIGHT, EditorConstants.ZONE_BUTTON_MIN_HEIGHT)
            };
            addBtn.Pressed += AddNewZone;
            _zoneListContainer.AddChild(addBtn);
        }

        private Button CreateZoneButton(ProfileZone zone, int index)
        {
            var btn = new Button
            {
                Text = zone.Name,
                TooltipText = $"{zone.Type}\nWidth: {zone.Width:F1}\nHeight: {zone.HeightOffset:F1}",
                ToggleMode = true,
                ButtonPressed = index == _selectedZoneIndex,
                CustomMinimumSize = new Vector2(EditorConstants.ZONE_BUTTON_MIN_WIDTH, EditorConstants.ZONE_BUTTON_MIN_HEIGHT)
            };

            // Use centralized zone colors
            var color = zone.Enabled
                ? ZoneColors.GetColor(zone.Type, ZoneColors.ColorContext.Inspector)
                : ZoneColors.GetDisabledColor(zone.Type, ZoneColors.ColorContext.Inspector);

            var style = new StyleBoxFlat
            {
                BgColor = color,
                CornerRadiusBottomLeft = 4,
                CornerRadiusBottomRight = 4,
                CornerRadiusTopLeft = 4,
                CornerRadiusTopRight = 4
            };

            if (index == _selectedZoneIndex)
            {
                style.BorderWidthBottom = 2;
                style.BorderWidthTop = 2;
                style.BorderWidthLeft = 2;
                style.BorderWidthRight = 2;
                style.BorderColor = Colors.White;
            }

            btn.AddThemeStyleboxOverride("normal", style);
            btn.AddThemeStyleboxOverride("hover", style);
            btn.AddThemeStyleboxOverride("pressed", style);

            int idx = index;
            btn.Pressed += () =>
            {
                _selectedZoneIndex = idx;
                RefreshZoneList();
                RefreshZoneEditor();
                RefreshProfilePreview();
            };

            return btn;
        }
        #endregion

        #region Zone Editor Content
        private void RefreshZoneEditor()
        {
            if (!IsInstanceValid(_zoneEditorContainer)) return;
            RefreshZoneEditorContent();
        }

        private void RefreshZoneEditorContent()
        {
            if (!IsInstanceValid(_zoneEditorContainer)) return;

            foreach (Node c in _zoneEditorContainer.GetChildren())
                c.QueueFree();

            var layer = CurrentLayer;
            if (layer?.Profile?.Zones == null || _selectedZoneIndex >= layer.Profile.Zones.Count)
            {
                _zoneEditorContainer.AddChild(new Label { Text = "No zone selected" });
                return;
            }

            var zone = layer.Profile.Zones[_selectedZoneIndex];
            if (zone == null) return;

            AddZoneHeader(zone, layer);
            AddZoneDimensions(zone);
            AddZoneHeightSettings(zone);
            AddZoneTextureSettings(zone);
            AddZoneNoiseSettings(zone);
        }

        private void AddZoneHeader(ProfileZone zone, PathLayer layer)
        {
            var header = new HBoxContainer();

            var enableCheck = new CheckBox { Text = "Enabled", ButtonPressed = zone.Enabled };
            enableCheck.Toggled += (v) => zone.Enabled = v;
            header.AddChild(enableCheck);

            var nameEdit = new LineEdit
            {
                Text = zone.Name,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                PlaceholderText = "Zone Name"
            };
            nameEdit.TextChanged += (t) => zone.Name = t;
            header.AddChild(nameEdit);

            var typeDropdown = new OptionButton();
            foreach (ZoneType t in Enum.GetValues(typeof(ZoneType)))
                typeDropdown.AddItem(t.ToString(), (int)t);
            typeDropdown.Selected = (int)zone.Type;
            typeDropdown.ItemSelected += (idx) => zone.Type = (ZoneType)(int)idx;
            header.AddChild(typeDropdown);

            if (layer.Profile.Zones.Count > 1)
            {
                var delBtn = new Button { Text = "Delete" };
                delBtn.Pressed += () =>
                {
                    layer.Profile.RemoveZone(_selectedZoneIndex);
                    _selectedZoneIndex = Mathf.Max(0, _selectedZoneIndex - 1);
                };
                header.AddChild(delBtn);
            }

            _zoneEditorContainer.AddChild(header);
        }

        private void AddZoneDimensions(ProfileZone zone)
        {
            EditorUIUtils.AddSeparator(_zoneEditorContainer);
            _zoneEditorContainer.AddChild(EditorUIUtils.CreateSectionLabel("Dimensions"));
            EditorUIUtils.AddSliderRow(_zoneEditorContainer, "Width", zone.Width, 0.1f, 50f,
                (v) => zone.Width = (float)v);
        }

        private void AddZoneHeightSettings(ProfileZone zone)
        {
            EditorUIUtils.AddSeparator(_zoneEditorContainer);
            _zoneEditorContainer.AddChild(EditorUIUtils.CreateSectionLabel("Height Modification"));

            EditorUIUtils.AddSliderRow(_zoneEditorContainer, "Offset", zone.HeightOffset, -20f, 20f,
                (v) => zone.HeightOffset = (float)v);
            EditorUIUtils.AddSliderRow(_zoneEditorContainer, "Strength", zone.HeightStrength, 0f, 1f,
                (v) => zone.HeightStrength = (float)v);
            EditorUIUtils.AddSliderRow(_zoneEditorContainer, "Conform", zone.TerrainConformance, 0f, 1f,
                (v) => zone.TerrainConformance = (float)v);

            EditorUIUtils.AddEnumDropdown(_zoneEditorContainer, "Blend Mode", zone.HeightBlendMode,
                (mode) => zone.HeightBlendMode = mode);

            EditorUIUtils.AddCurveEditorRow(_zoneEditorContainer, "Height Curve", zone.HeightCurve,
                (c) => zone.HeightCurve = c, _windowTracker);
        }

        private void AddZoneTextureSettings(ProfileZone zone)
        {
            EditorUIUtils.AddSeparator(_zoneEditorContainer);
            _zoneEditorContainer.AddChild(EditorUIUtils.CreateSectionLabel("Texture"));

            AddTextureSelector(_zoneEditorContainer, zone);

            EditorUIUtils.AddSliderRow(_zoneEditorContainer, "Strength", zone.TextureStrength, 0f, 1f,
                (v) => zone.TextureStrength = (float)v);

            EditorUIUtils.AddEnumDropdown(_zoneEditorContainer, "Blend Mode", zone.TextureBlendMode,
                (mode) => zone.TextureBlendMode = mode);
        }

        private void AddZoneNoiseSettings(ProfileZone zone)
        {
            EditorUIUtils.AddSeparator(_zoneEditorContainer);

            // Use collapsible noise editor - state is tracked via _sectionExpanded dictionary
            EditorUIUtils.AddCollapsibleNoiseEditor(
                _zoneEditorContainer,
                zone.HeightNoise,
                zone.TextureNoise,
                "Noise Settings",
                _sectionExpanded
            );
        }
        #endregion

        #region Texture Selector
        private void AddTextureSelector(VBoxContainer container, ProfileZone zone)
        {
            var row = new HBoxContainer();
            row.AddChild(new Label { Text = "Texture:", CustomMinimumSize = new Vector2(EditorConstants.LABEL_MIN_WIDTH, 0) });

            var assets = CurrentTerrain3DAssets;
            var currentThumb = new TextureRect
            {
                CustomMinimumSize = new Vector2(EditorConstants.THUMBNAIL_SIZE, EditorConstants.THUMBNAIL_SIZE),
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered
            };
            var currentLabel = new Label { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };

            Action updateDisplay = () =>
            {
                if (zone.TextureId < 0)
                {
                    currentLabel.Text = "None";
                    currentThumb.Texture = null;
                }
                else if (assets != null && zone.TextureId < assets.TextureList.Count)
                {
                    var res = assets.TextureList[zone.TextureId].As<Resource>();
                    var asset = res != null ? Terrain3DTextureAsset.Bind(res) : null;
                    if (asset != null)
                    {
                        currentLabel.Text = $"[{zone.TextureId}] {asset.Name}";
                        var thumb = asset.GetThumbnail();
                        if (thumb != null)
                        {
                            var img = thumb.GetImage();
                            img?.Resize(EditorConstants.THUMBNAIL_SIZE, EditorConstants.THUMBNAIL_SIZE);
                            currentThumb.Texture = img != null ? ImageTexture.CreateFromImage(img) : null;
                        }
                    }
                }
            };
            updateDisplay();

            row.AddChild(currentThumb);
            row.AddChild(currentLabel);

            var selectBtn = new Button { Text = "Select..." };
            selectBtn.Pressed += () => TerrainAssetSelector.ShowTexturePopup(CurrentLayer, assets, zone.TextureId,
                (id) => { zone.TextureId = id; updateDisplay(); });
            row.AddChild(selectBtn);

            var noneBtn = new Button { Text = "None" };
            noneBtn.Pressed += () => { zone.TextureId = -1; updateDisplay(); };
            row.AddChild(noneBtn);

            container.AddChild(row);
        }
        #endregion

        #region Zone Management
        private void AddNewZone()
        {
            CurrentLayer?.Profile?.AddZone(ProfileZone.CreateEdge(2.0f));
        }
        #endregion
    }
}