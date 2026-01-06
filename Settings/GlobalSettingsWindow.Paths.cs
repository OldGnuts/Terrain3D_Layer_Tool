using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using static Godot.Control;
using Terrain3DTools.Core;
using Terrain3DTools.Editor.Utils;
using Terrain3DTools.Layers.Path;
using Terrain3DTools.Settings;
using TokisanGames;

namespace Terrain3DTools.Editor
{
    public partial class GlobalSettingsWindow
    {
        #region Path Tab Fields
        private VBoxContainer _zoneTextureDefaultsContainer;
        private VBoxContainer _profileEditorContainer;
        private OptionButton _profilePathTypeDropdown;
        private PathProfilePreviewControl _profilePreview;
        private HBoxContainer _profileZoneListContainer;
        private VBoxContainer _profileZoneEditorContainer;
        private int _selectedProfileZoneIndex = 0;
        private PathType _selectedProfilePathType = PathType.Road;

        // Working copy of the profile being edited
        private PathProfile _editingProfile;
        #endregion

        #region Paths Tab
        private void CreatePathsTab()
        {
            var content = CreateTab("🛤️ Paths");
            PopulatePathSettingsTab(content);
        }

        private void PopulatePathSettingsTab(VBoxContainer content)
        {
            // Zone Texture Defaults Section
            AddZoneTextureDefaultsSection(content);

            EditorUIUtils.AddSeparator(content, 16);

            // Profile Editor Section
            AddProfileEditorSection(content);
        }
        #endregion

        #region Zone Texture Defaults
        private void AddZoneTextureDefaultsSection(VBoxContainer parent)
        {
            string zoneHelp = EditorHelpTooltip.FormatHelpText(
                "Default textures applied to zones when creating new paths.",
                new System.Collections.Generic.List<(string, string)>
                {
            ("Zone Type", "Each zone type can have a default texture"),
            ("None (-1)", "Set to 'None' to keep the preset's default texture"),
            ("Select", "Choose a texture from your Terrain3D asset library"),
            ("Clear (✕)", "Remove the override and use preset default")
                },
                "Tip: Set up your texture defaults once, and all new paths will use them automatically."
            );

            EditorUIUtils.AddSectionHeader(parent, "🎨 Zone Texture Defaults", "Zone Texture Defaults Help", zoneHelp);

            _zoneTextureDefaultsContainer = ExpandHorizontal(new VBoxContainer());
            _zoneTextureDefaultsContainer.AddThemeConstantOverride("separation", 4);

            RefreshZoneTextureDefaults();

            parent.AddChild(_zoneTextureDefaultsContainer);
        }

        private void RefreshZoneTextureDefaults()
        {
            if (_zoneTextureDefaultsContainer == null) return;

            foreach (var child in _zoneTextureDefaultsContainer.GetChildren())
                child.QueueFree();

            var pathSettings = PathToolsSettingsManager.Current;
            var assets = GetTerrain3DAssets();

            // Create a row for each zone type
            foreach (ZoneType zoneType in Enum.GetValues(typeof(ZoneType)))
            {
                int currentTexture = pathSettings.GetDefaultTextureForZone(zoneType);
                var row = CreateZoneTextureRow(zoneType, currentTexture, assets);
                _zoneTextureDefaultsContainer.AddChild(row);
            }
        }

        private Control CreateZoneTextureRow(ZoneType zoneType, int currentTextureId, Terrain3DAssets assets)
        {
            var row = ExpandHorizontal(new HBoxContainer());
            row.AddThemeConstantOverride("separation", 8);

            // Zone color indicator
            var colorRect = new ColorRect
            {
                CustomMinimumSize = new Vector2(16, 16),
                Color = ZoneColors.GetColor(zoneType, ZoneColors.ColorContext.Inspector)
            };
            row.AddChild(colorRect);

            // Zone name
            var nameLabel = new Label
            {
                Text = zoneType.ToString(),
                CustomMinimumSize = new Vector2(80, 0)
            };
            row.AddChild(nameLabel);

            // Texture thumbnail
            var thumbnail = new TextureRect
            {
                CustomMinimumSize = new Vector2(EditorConstants.THUMBNAIL_SIZE_SMALL, EditorConstants.THUMBNAIL_SIZE_SMALL),
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize
            };
            row.AddChild(thumbnail);

            // Texture name label
            var textureLabel = ExpandHorizontal(new Label());
            row.AddChild(textureLabel);

            // Update display action
            Action updateDisplay = () =>
            {
                int texId = PathToolsSettingsManager.Current.GetDefaultTextureForZone(zoneType);
                UpdateTextureDisplay(thumbnail, textureLabel, texId, assets);
            };
            updateDisplay();

            // Select button
            var selectBtn = new Button { Text = "Select" };
            selectBtn.Pressed += () =>
            {
                if (assets != null)
                {
                    int currentTexture = PathToolsSettingsManager.Current.GetDefaultTextureForZone(zoneType);
                    TerrainAssetSelector.ShowTexturePopup(this, assets, currentTexture, (id) =>
                    {
                        PathToolsSettingsManager.Current.SetDefaultTextureForZone(zoneType, id);
                        PathToolsSettingsManager.Save();
                        updateDisplay();
                        MarkSettingsChanged();
                    });
                }
                else
                {
                    ShowNoTerrainAssetsWarning();
                }
            };
            row.AddChild(selectBtn);

            // Clear button
            var clearBtn = new Button
            {
                Text = "✕",
                TooltipText = "Clear (use preset default)",
                CustomMinimumSize = new Vector2(24, 24)
            };
            clearBtn.Pressed += () =>
            {
                PathToolsSettingsManager.Current.SetDefaultTextureForZone(zoneType, -1);
                PathToolsSettingsManager.Save();
                updateDisplay();
                MarkSettingsChanged();
            };
            row.AddChild(clearBtn);

            return row;
        }

        private void UpdateTextureDisplay(TextureRect thumbnail, Label label, int textureId, Terrain3DAssets assets)
        {
            if (textureId < 0)
            {
                label.Text = "None (use preset)";
                thumbnail.Texture = null;
                return;
            }

            if (assets != null && textureId < assets.TextureList.Count)
            {
                var resource = assets.TextureList[textureId].As<Resource>();
                var asset = resource != null ? Terrain3DTextureAsset.Bind(resource) : null;

                if (asset != null)
                {
                    label.Text = $"[{textureId}] {asset.Name}";
                    var tex = asset.GetThumbnail();
                    if (tex != null)
                    {
                        var img = tex.GetImage();
                        img?.Resize(EditorConstants.THUMBNAIL_SIZE_SMALL, EditorConstants.THUMBNAIL_SIZE_SMALL);
                        thumbnail.Texture = img != null ? ImageTexture.CreateFromImage(img) : null;
                    }
                    return;
                }
            }

            label.Text = $"[{textureId}] (not found)";
            thumbnail.Texture = null;
        }
        #endregion

        #region Profile Editor
        private void AddProfileEditorSection(VBoxContainer parent)
        {
            string profileHelp = EditorHelpTooltip.FormatHelpText(
                "Edit the default profiles for each path type.",
                new System.Collections.Generic.List<(string, string)>
                {
            ("Path Type", "Select which path type's profile to edit"),
            ("Zone List", "Click zones to select and edit them"),
            ("Reset to Default", "Restore the built-in preset for this path type")
                },
                "Changes here affect all NEW paths created with that type. Existing paths keep their current profiles."
            );

            EditorUIUtils.AddSectionHeader(parent, "📐 Default Profile Editor", "Profile Editor Help", profileHelp);

            // Info label
            var infoLabel = new Label
            {
                Text = "Select a path type to edit its default profile.",
                AutowrapMode = TextServer.AutowrapMode.Word
            };
            infoLabel.AddThemeColorOverride("font_color", new Color(0.6f, 0.6f, 0.6f));
            infoLabel.AddThemeFontSizeOverride("font_size", 11);
            parent.AddChild(infoLabel);

            EditorUIUtils.AddSeparator(parent, 4);

            // Path type selector row
            var typeRow = ExpandHorizontal(new HBoxContainer());
            typeRow.AddThemeConstantOverride("separation", 8);

            typeRow.AddChild(new Label { Text = "Path Type:", CustomMinimumSize = new Vector2(80, 0) });

            _profilePathTypeDropdown = ExpandHorizontal(new OptionButton());
            foreach (PathType type in Enum.GetValues(typeof(PathType)))
            {
                _profilePathTypeDropdown.AddItem(PathPresets.GetDisplayName(type), (int)type);
            }
            _profilePathTypeDropdown.Selected = (int)_selectedProfilePathType;
            _profilePathTypeDropdown.ItemSelected += OnProfilePathTypeChanged;
            typeRow.AddChild(_profilePathTypeDropdown);

            var resetBtn = new Button
            {
                Text = "Reset to Default",
                TooltipText = "Reset this path type's profile to built-in defaults"
            };
            resetBtn.Pressed += OnResetProfileToDefault;
            typeRow.AddChild(resetBtn);

            parent.AddChild(typeRow);

            EditorUIUtils.AddSeparator(parent, 8);

            // Profile editor container
            _profileEditorContainer = ExpandFill(new VBoxContainer());
            _profileEditorContainer.AddThemeConstantOverride("separation", 8);

            LoadProfileForEditing(_selectedProfilePathType);
            BuildProfileEditor();

            parent.AddChild(_profileEditorContainer);
        }

        private void OnProfilePathTypeChanged(long index)
        {
            _selectedProfilePathType = (PathType)(int)index;
            _selectedProfileZoneIndex = 0;
            LoadProfileForEditing(_selectedProfilePathType);
            BuildProfileEditor();
        }

        private void LoadProfileForEditing(PathType pathType)
        {
            _editingProfile = PathPresets.GetPresetWithUserDefaults(pathType);
        }

        private void OnResetProfileToDefault()
        {
            var dialog = new ConfirmationDialog
            {
                Title = "Reset Profile",
                DialogText = $"Reset {PathPresets.GetDisplayName(_selectedProfilePathType)} profile to built-in defaults?\n\n" +
                            "This will clear any customizations for this path type.",
                OkButtonText = "Reset",
                CancelButtonText = "Cancel"
            };

            dialog.Confirmed += () =>
            {
                var pathSettings = PathToolsSettingsManager.Current;
                foreach (ZoneType zoneType in Enum.GetValues(typeof(ZoneType)))
                {
                    pathSettings.ClearTextureOverride(_selectedProfilePathType, zoneType);
                }
                PathToolsSettingsManager.Save();

                _selectedProfileZoneIndex = 0;
                LoadProfileForEditing(_selectedProfilePathType);
                BuildProfileEditor();
                MarkSettingsChanged();

                dialog.QueueFree();
            };

            dialog.Canceled += () => dialog.QueueFree();

            AddChild(dialog);
            dialog.PopupCentered();
        }

        private void BuildProfileEditor()
        {
            if (_profileEditorContainer == null) return;

            foreach (var child in _profileEditorContainer.GetChildren())
                child.QueueFree();

            if (_editingProfile == null)
            {
                _profileEditorContainer.AddChild(new Label { Text = "No profile loaded" });
                return;
            }

            // Profile preview
            var previewContainer = ExpandHorizontal(new PanelContainer
            {
                CustomMinimumSize = new Vector2(0, EditorConstants.PROFILE_PREVIEW_HEIGHT)
            });
            var bg = new StyleBoxFlat { BgColor = new Color(0.12f, 0.12f, 0.12f) };
            previewContainer.AddThemeStyleboxOverride("panel", bg);

            _profilePreview = ExpandFill(new PathProfilePreviewControl(_editingProfile, () => _selectedProfileZoneIndex));
            previewContainer.AddChild(_profilePreview);
            _profileEditorContainer.AddChild(previewContainer);

            // Zone list (horizontal scroll)
            _profileZoneListContainer = ExpandHorizontal(new HBoxContainer());
            _profileZoneListContainer.AddThemeConstantOverride("separation", 4);
            RefreshProfileZoneList();

            var zoneScroll = ExpandHorizontal(new ScrollContainer
            {
                CustomMinimumSize = new Vector2(0, 44),
                HorizontalScrollMode = ScrollContainer.ScrollMode.Auto,
                VerticalScrollMode = ScrollContainer.ScrollMode.Disabled
            });
            zoneScroll.AddChild(_profileZoneListContainer);
            _profileEditorContainer.AddChild(zoneScroll);

            // Zone editor - wrap in scroll container with fixed minimum height
            var zoneEditorScroll = ExpandFill(new ScrollContainer
            {
                CustomMinimumSize = new Vector2(0, 280),
                HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
                VerticalScrollMode = ScrollContainer.ScrollMode.Auto
            });

            _profileZoneEditorContainer = ExpandHorizontal(new VBoxContainer());
            _profileZoneEditorContainer.AddThemeConstantOverride("separation", 4);

            RefreshProfileZoneEditor();

            zoneEditorScroll.AddChild(_profileZoneEditorContainer);
            _profileEditorContainer.AddChild(zoneEditorScroll);
        }

        private void RefreshProfileZoneList()
        {
            if (_profileZoneListContainer == null || _editingProfile == null) return;

            foreach (var child in _profileZoneListContainer.GetChildren())
                child.QueueFree();

            for (int i = 0; i < _editingProfile.Zones.Count; i++)
            {
                var zone = _editingProfile.Zones[i];
                if (zone == null) continue;

                var btn = CreateProfileZoneButton(zone, i);
                _profileZoneListContainer.AddChild(btn);
            }

            // Add zone button
            var addBtn = new Button
            {
                Text = "+",
                TooltipText = "Add new zone",
                CustomMinimumSize = new Vector2(EditorConstants.ZONE_BUTTON_MIN_HEIGHT, EditorConstants.ZONE_BUTTON_MIN_HEIGHT)
            };
            addBtn.Pressed += () =>
            {
                _editingProfile.AddZone(ProfileZone.CreateEdge(2.0f));
                _selectedProfileZoneIndex = _editingProfile.Zones.Count - 1;
                OnProfileEdited();
            };
            _profileZoneListContainer.AddChild(addBtn);
        }

        private Button CreateProfileZoneButton(ProfileZone zone, int index)
        {
            var btn = new Button
            {
                Text = zone.Name,
                TooltipText = $"{zone.Type}\nWidth: {zone.Width:F1}m\nHeight: {zone.HeightOffset:F1}m",
                ToggleMode = true,
                ButtonPressed = index == _selectedProfileZoneIndex,
                CustomMinimumSize = new Vector2(EditorConstants.ZONE_BUTTON_MIN_WIDTH, EditorConstants.ZONE_BUTTON_MIN_HEIGHT)
            };

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

            if (index == _selectedProfileZoneIndex)
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
                _selectedProfileZoneIndex = idx;
                RefreshProfileZoneList();
                RefreshProfileZoneEditor();
                _profilePreview?.QueueRedraw();
            };

            return btn;
        }

        private void RefreshProfileZoneEditor()
        {
            if (_profileZoneEditorContainer == null || _editingProfile == null) return;

            foreach (var child in _profileZoneEditorContainer.GetChildren())
                child.QueueFree();

            if (_selectedProfileZoneIndex >= _editingProfile.Zones.Count)
            {
                _profileZoneEditorContainer.AddChild(new Label { Text = "Select a zone to edit" });
                return;
            }

            var zone = _editingProfile.Zones[_selectedProfileZoneIndex];
            if (zone == null) return;

            // Zone header
            var headerRow = ExpandHorizontal(new HBoxContainer());
            headerRow.AddThemeConstantOverride("separation", 8);

            var enableCheck = new CheckBox { Text = "Enabled", ButtonPressed = zone.Enabled };
            enableCheck.Toggled += (v) => { zone.Enabled = v; OnProfileEdited(); };
            headerRow.AddChild(enableCheck);

            var nameEdit = ExpandHorizontal(new LineEdit
            {
                Text = zone.Name,
                PlaceholderText = "Zone Name"
            });
            nameEdit.TextChanged += (t) => { zone.Name = t; OnProfileEdited(); };
            headerRow.AddChild(nameEdit);

            var typeDropdown = new OptionButton();
            foreach (ZoneType t in Enum.GetValues(typeof(ZoneType)))
                typeDropdown.AddItem(t.ToString(), (int)t);
            typeDropdown.Selected = (int)zone.Type;
            typeDropdown.ItemSelected += (idx) => { zone.Type = (ZoneType)(int)idx; OnProfileEdited(); };
            headerRow.AddChild(typeDropdown);

            if (_editingProfile.Zones.Count > 1)
            {
                var delBtn = new Button { Text = "Delete" };
                delBtn.Pressed += () =>
                {
                    _editingProfile.RemoveZone(_selectedProfileZoneIndex);
                    _selectedProfileZoneIndex = Mathf.Max(0, _selectedProfileZoneIndex - 1);
                    OnProfileEdited();
                };
                headerRow.AddChild(delBtn);
            }

            _profileZoneEditorContainer.AddChild(headerRow);

            // Dimensions
            EditorUIUtils.AddSeparator(_profileZoneEditorContainer, 4);
            EditorUIUtils.AddSubsectionHeader(_profileZoneEditorContainer, "📏 Dimensions");
            EditorUIUtils.AddSliderRow(_profileZoneEditorContainer, "Width", zone.Width, 0.1f, 50f,
                (v) => { zone.Width = (float)v; OnProfileEdited(); });

            // Height
            EditorUIUtils.AddSeparator(_profileZoneEditorContainer, 4);
            EditorUIUtils.AddSubsectionHeader(_profileZoneEditorContainer, "📐 Height");
            EditorUIUtils.AddSliderRow(_profileZoneEditorContainer, "Offset", zone.HeightOffset, -20f, 20f,
                (v) => { zone.HeightOffset = (float)v; OnProfileEdited(); });
            EditorUIUtils.AddSliderRow(_profileZoneEditorContainer, "Strength", zone.HeightStrength, 0f, 1f,
                (v) => { zone.HeightStrength = (float)v; OnProfileEdited(); });

            EditorUIUtils.AddEnumDropdown(_profileZoneEditorContainer, "Blend Mode", zone.HeightBlendMode,
                (mode) => { zone.HeightBlendMode = mode; OnProfileEdited(); });

            // Height Curve
            EditorUIUtils.AddCurveEditorRow(_profileZoneEditorContainer, "Height Curve", zone.HeightCurve,
                (c) => { zone.HeightCurve = c; OnProfileEdited(); }, _windowTracker);

            // Texture
            EditorUIUtils.AddSeparator(_profileZoneEditorContainer, 4);
            EditorUIUtils.AddSubsectionHeader(_profileZoneEditorContainer, "🎨 Texture");
            AddProfileZoneTextureSelector(zone);

            EditorUIUtils.AddSliderRow(_profileZoneEditorContainer, "Strength", zone.TextureStrength, 0f, 1f,
                (v) => { zone.TextureStrength = (float)v; OnProfileEdited(); });
        }

        private void AddProfileZoneTextureSelector(ProfileZone zone)
        {
            var row = ExpandHorizontal(new HBoxContainer());
            row.AddThemeConstantOverride("separation", 8);
            row.AddChild(new Label { Text = "Texture:", CustomMinimumSize = new Vector2(EditorConstants.LABEL_MIN_WIDTH, 0) });

            var assets = GetTerrain3DAssets();
            var thumbnail = new TextureRect
            {
                CustomMinimumSize = new Vector2(EditorConstants.THUMBNAIL_SIZE_SMALL, EditorConstants.THUMBNAIL_SIZE_SMALL),
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered
            };
            row.AddChild(thumbnail);

            var textureLabel = ExpandHorizontal(new Label());
            row.AddChild(textureLabel);

            Action updateDisplay = () => UpdateTextureDisplay(thumbnail, textureLabel, zone.TextureId, assets);
            updateDisplay();

            var selectBtn = new Button { Text = "Select" };
            selectBtn.Pressed += () =>
            {
                if (assets != null)
                {
                    TerrainAssetSelector.ShowTexturePopup(this, assets, zone.TextureId, (id) =>
                    {
                        zone.TextureId = id;
                        updateDisplay();
                        OnProfileEdited();
                    });
                }
                else
                {
                    ShowNoTerrainAssetsWarning();
                }
            };
            row.AddChild(selectBtn);

            var noneBtn = new Button { Text = "None" };
            noneBtn.Pressed += () =>
            {
                zone.TextureId = -1;
                updateDisplay();
                OnProfileEdited();
            };
            row.AddChild(noneBtn);

            _profileZoneEditorContainer.AddChild(row);
        }

        private void OnProfileEdited()
        {
            SaveProfileToSettings();

            RefreshProfileZoneList();
            RefreshProfileZoneEditor();
            _profilePreview?.QueueRedraw();
            MarkSettingsChanged();
        }

        private void SaveProfileToSettings()
        {
            if (_editingProfile == null) return;

            var pathSettings = PathToolsSettingsManager.Current;

            foreach (var zone in _editingProfile.Zones)
            {
                if (zone == null) continue;

                int defaultTexture = pathSettings.GetDefaultTextureForZone(zone.Type);
                if (zone.TextureId != defaultTexture)
                {
                    pathSettings.SetTextureForPathAndZone(_selectedProfilePathType, zone.Type, zone.TextureId);
                }
            }

            PathToolsSettingsManager.Save();
        }
        #endregion

        #region Helpers
        private Terrain3DAssets GetTerrain3DAssets()
        {
            var manager = TerrainLayerManager;
            if (manager?.Terrain3DNode != null)
            {
                var terrain3D = Terrain3D.Bind(manager.Terrain3DNode);
                return terrain3D?.Assets;
            }

            var tree = GetTree();
            if (tree != null)
            {
                var managerNode = tree.GetFirstNodeInGroup("terrain_layer_manager") as TerrainLayerManager;
                if (managerNode?.Terrain3DNode != null)
                {
                    var terrain3D = Terrain3D.Bind(managerNode.Terrain3DNode);
                    return terrain3D?.Assets;
                }
            }

            return null;
        }

        private void ShowNoTerrainAssetsWarning()
        {
            var dialog = new AcceptDialog
            {
                Title = "No Terrain Assets",
                DialogText = "Cannot access Terrain3D texture assets.\n\n" +
                            "Make sure you have a TerrainLayerManager in your scene\n" +
                            "with a valid Terrain3D node assigned."
            };
            dialog.Confirmed += () => dialog.QueueFree();
            AddChild(dialog);
            dialog.PopupCentered();
        }
        #endregion
    }

    #region Profile Preview Control
    /// <summary>
    /// Simplified profile preview control for the global settings window.
    /// </summary>
    [Tool]
    public partial class PathProfilePreviewControl : Control
    {
        private PathProfile _profile;
        private Func<int> _getSelectedZone;

        public PathProfilePreviewControl()
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill;
            SizeFlagsVertical = SizeFlags.ExpandFill;
        }

        public PathProfilePreviewControl(PathProfile profile, Func<int> getSelectedZone) : this()
        {
            _profile = profile;
            _getSelectedZone = getSelectedZone;
        }

        public void SetProfile(PathProfile profile)
        {
            _profile = profile;
            QueueRedraw();
        }

        public override void _Draw()
        {
            if (_profile == null) return;

            var rect = GetRect();
            float width = rect.Size.X;
            float height = rect.Size.Y;
            float centerX = width / 2f;
            float baselineY = height - 20f;

            // Background
            DrawRect(new Rect2(0, 0, width, height), new Color(0.1f, 0.1f, 0.1f));

            // Calculate scale
            float profileWidth = _profile.HalfWidth;
            if (profileWidth < 0.1f) profileWidth = 10f;

            float maxHeight = 2f;
            float minHeight = 0f;
            foreach (var zone in _profile.Zones)
            {
                if (zone != null && zone.Enabled)
                {
                    maxHeight = Mathf.Max(maxHeight, zone.HeightOffset + 1f);
                    minHeight = Mathf.Min(minHeight, zone.HeightOffset - 1f);
                }
            }
            float heightRange = maxHeight - minHeight;
            if (heightRange < 0.1f) heightRange = 5f;

            float scaleX = (width * 0.45f) / profileWidth;
            float scaleY = (height * 0.5f) / heightRange;

            int selectedZone = _getSelectedZone?.Invoke() ?? -1;

            // Draw zones
            float currentDist = 0f;
            for (int i = 0; i < _profile.Zones.Count; i++)
            {
                var zone = _profile.Zones[i];
                if (zone == null || !zone.Enabled) continue;

                Color zoneColor = ZoneColors.GetColor(zone.Type, ZoneColors.ColorContext.Preview);
                if (i == selectedZone)
                {
                    zoneColor = ZoneColors.GetHighlightedColor(zone.Type, ZoneColors.ColorContext.Preview);
                }

                DrawZoneFill(centerX, baselineY, scaleX, scaleY, minHeight, zone, currentDist, zoneColor);
                currentDist += zone.Width;
            }

            // Draw baseline
            DrawLine(new Vector2(0, baselineY), new Vector2(width, baselineY), new Color(0.4f, 0.4f, 0.4f), 1f);

            // Draw centerline
            DrawLine(new Vector2(centerX, 5), new Vector2(centerX, height - 5), new Color(1f, 1f, 1f, 0.3f), 1f);

            // Labels
            var font = ThemeDB.FallbackFont;
            DrawString(font, new Vector2(centerX - 15, height - 3), "Center",
                HorizontalAlignment.Center, -1, 9, new Color(0.6f, 0.6f, 0.6f));
            DrawString(font, new Vector2(5, height - 3), $"-{profileWidth:F1}m",
                HorizontalAlignment.Left, -1, 9, new Color(0.6f, 0.6f, 0.6f));
            DrawString(font, new Vector2(width - 45, height - 3), $"+{profileWidth:F1}m",
                HorizontalAlignment.Left, -1, 9, new Color(0.6f, 0.6f, 0.6f));
        }

        private void DrawZoneFill(float centerX, float baselineY, float scaleX, float scaleY,
            float minHeight, ProfileZone zone, float zoneStartDist, Color zoneColor)
        {
            int samples = 12;
            var zoneCurve = zone.HeightCurve;

            // Right side
            var rightPoints = new System.Collections.Generic.List<Vector2>();
            rightPoints.Add(new Vector2(centerX + zoneStartDist * scaleX, baselineY));

            for (int s = 0; s <= samples; s++)
            {
                float t = (float)s / samples;
                float dist = zoneStartDist + t * zone.Width;

                float curveValue = 1f;
                if (zoneCurve != null && zoneCurve.PointCount >= 2)
                {
                    zoneCurve.Bake();
                    curveValue = zoneCurve.SampleBaked(t);
                }

                float h = zone.HeightOffset * curveValue;
                float screenX = centerX + dist * scaleX;
                float screenY = baselineY - (h - minHeight) * scaleY;

                rightPoints.Add(new Vector2(screenX, screenY));
            }

            rightPoints.Add(new Vector2(centerX + (zoneStartDist + zone.Width) * scaleX, baselineY));

            if (rightPoints.Count >= 3)
            {
                DrawColoredPolygon(rightPoints.ToArray(), zoneColor with { A = 0.5f });
            }

            // Left side (mirrored)
            var leftPoints = new System.Collections.Generic.List<Vector2>();
            foreach (var pt in rightPoints)
            {
                leftPoints.Add(new Vector2(2 * centerX - pt.X, pt.Y));
            }

            if (leftPoints.Count >= 3)
            {
                DrawColoredPolygon(leftPoints.ToArray(), zoneColor with { A = 0.5f });
            }

            // Outline
            for (int i = 1; i < rightPoints.Count - 1; i++)
            {
                DrawLine(rightPoints[i], rightPoints[i + 1], zoneColor.Lightened(0.3f), 1.5f);
                DrawLine(leftPoints[i], leftPoints[i + 1], zoneColor.Lightened(0.3f), 1.5f);
            }
        }
    }
    #endregion
}