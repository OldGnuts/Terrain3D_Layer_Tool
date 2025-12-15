// /Editor/PathLayerInspector.Zones.cs
using System;
using Godot;
using Terrain3DTools.Layers.Path;
using Terrain3DTools.Editor.Utils;
using Terrain3DTools.Utils;
using TokisanGames;

namespace Terrain3DTools.Editor
{
    public partial class PathLayerInspector
    {
        private VBoxContainer _zoneEditorContainer;
        private HBoxContainer _zoneListContainer;
        private int _selectedZoneIndex = 0;

        private void AddZoneEditor()
        {
            CreateCollapsibleSection("Zone Editor", true);
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
            
            // Clear existing
            foreach (Node c in _zoneListContainer.GetChildren()) c.QueueFree();

            var layer = CurrentLayer;
            if (layer?.Profile?.Zones == null) return;

            for (int i = 0; i < layer.Profile.Zones.Count; i++)
            {
                var zone = layer.Profile.Zones[i];
                if (zone == null) continue;
                _zoneListContainer.AddChild(CreateZoneButton(zone, i));
            }

            var addBtn = new Button { 
                Text = "+", 
                TooltipText = "Add new zone",
                CustomMinimumSize = new Vector2(32, 32) 
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
                CustomMinimumSize = new Vector2(60, 32)
            };

            // --- RESTORED FEATURE: Zone Type Coloring ---
            var color = GetZoneTypeColor(zone.Type);
            if (!zone.Enabled) color = color with { A = 0.3f };

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
            // -------------------------------------------

            int idx = index;
            btn.Pressed += () => { 
                _selectedZoneIndex = idx; 
                RefreshZoneList(); // Refresh to update borders
                RefreshZoneEditor(); 
                RefreshProfilePreview(); 
            };
            return btn;
        }

        private Color GetZoneTypeColor(ZoneType type)
        {
            return type switch
            {
                ZoneType.Center => new Color(0.2f, 0.6f, 1.0f, 0.7f),
                ZoneType.Inner => new Color(0.3f, 0.5f, 0.9f, 0.7f),
                ZoneType.Shoulder => new Color(0.2f, 0.8f, 0.4f, 0.7f),
                ZoneType.Edge => new Color(0.5f, 0.7f, 0.3f, 0.7f),
                ZoneType.Wall => new Color(0.8f, 0.4f, 0.2f, 0.7f),
                ZoneType.Rim => new Color(0.8f, 0.6f, 0.2f, 0.7f),
                ZoneType.Slope => new Color(0.6f, 0.5f, 0.4f, 0.7f),
                ZoneType.Transition => new Color(0.5f, 0.5f, 0.5f, 0.5f),
                _ => new Color(0.4f, 0.4f, 0.4f, 0.7f)
            };
        }

        private void RefreshZoneEditor()
        {
            if (!IsInstanceValid(_zoneEditorContainer)) return;
            RefreshZoneEditorContent();
        }

        private void RefreshZoneEditorContent()
        {
            if (!IsInstanceValid(_zoneEditorContainer)) return;
            foreach (Node c in _zoneEditorContainer.GetChildren()) c.QueueFree();

            var layer = CurrentLayer;
            if (layer?.Profile?.Zones == null || _selectedZoneIndex >= layer.Profile.Zones.Count) 
            {
                _zoneEditorContainer.AddChild(new Label { Text = "No zone selected" });
                return;
            }

            var zone = layer.Profile.Zones[_selectedZoneIndex];
            if (zone == null) return;

            // Header (Name, Enable, Type)
            var header = new HBoxContainer();
            
            var enableCheck = new CheckBox { Text = "Enabled", ButtonPressed = zone.Enabled };
            enableCheck.Toggled += (v) => { zone.Enabled = v; RefreshProfilePreview(); RefreshZoneList(); };
            header.AddChild(enableCheck);
            
            var nameEdit = new LineEdit { Text = zone.Name, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, PlaceholderText = "Zone Name" };
            nameEdit.TextChanged += (t) => { zone.Name = t; RefreshZoneList(); };
            header.AddChild(nameEdit);

            var typeDropdown = new OptionButton();
            foreach (ZoneType t in Enum.GetValues(typeof(ZoneType))) typeDropdown.AddItem(t.ToString(), (int)t);
            typeDropdown.Selected = (int)zone.Type;
            typeDropdown.ItemSelected += (idx) => { zone.Type = (ZoneType)(int)idx; RefreshProfilePreview(); RefreshZoneList(); };
            header.AddChild(typeDropdown);

            if (layer.Profile.Zones.Count > 1) {
                var delBtn = new Button { Text = "Delete" };
                delBtn.Pressed += () => { 
                    layer.Profile.RemoveZone(_selectedZoneIndex); 
                    _selectedZoneIndex = Mathf.Max(0, _selectedZoneIndex - 1); 
                    RefreshZoneList();
                    RefreshZoneEditor(); 
                    RefreshProfilePreview(); 
                };
                header.AddChild(delBtn);
            }
            _zoneEditorContainer.AddChild(header);

            // Dimensions
            EditorUIUtils.AddSeparator(_zoneEditorContainer);
            _zoneEditorContainer.AddChild(new Label { Text = "Dimensions", Modulate = new Color(0.8f, 0.8f, 0.8f) });
            EditorUIUtils.AddSliderRow(_zoneEditorContainer, "Width", zone.Width, 0.1f, 50f, (v) => { zone.Width = (float)v; RefreshProfilePreview(); UpdateStatLabels(); });

            // Height
            EditorUIUtils.AddSeparator(_zoneEditorContainer);
            _zoneEditorContainer.AddChild(new Label { Text = "Height Modification", Modulate = new Color(0.8f, 0.8f, 0.8f) });
            EditorUIUtils.AddSliderRow(_zoneEditorContainer, "Offset", zone.HeightOffset, -20f, 20f, (v) => { zone.HeightOffset = (float)v; RefreshProfilePreview(); });
            EditorUIUtils.AddSliderRow(_zoneEditorContainer, "Strength", zone.HeightStrength, 0f, 1f, (v) => zone.HeightStrength = (float)v);
            EditorUIUtils.AddSliderRow(_zoneEditorContainer, "Conform", zone.TerrainConformance, 0f, 1f, (v) => zone.TerrainConformance = (float)v);
            
            var blendRow = new HBoxContainer();
            blendRow.AddChild(new Label { Text = "Blend Mode:", CustomMinimumSize = new Vector2(100, 0) });
            var blendOption = new OptionButton { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            foreach (HeightBlendMode m in Enum.GetValues(typeof(HeightBlendMode))) blendOption.AddItem(m.ToString(), (int)m);
            blendOption.Selected = (int)zone.HeightBlendMode;
            blendOption.ItemSelected += (idx) => zone.HeightBlendMode = (HeightBlendMode)(int)idx;
            blendRow.AddChild(blendOption);
            _zoneEditorContainer.AddChild(blendRow);

            AddCurveEditorRow(_zoneEditorContainer, "Height Curve", zone.HeightCurve, (c) => { zone.HeightCurve = c; RefreshProfilePreview(); });

            // Texture
            EditorUIUtils.AddSeparator(_zoneEditorContainer);
            _zoneEditorContainer.AddChild(new Label { Text = "Texture", Modulate = new Color(0.8f, 0.8f, 0.8f) });
            AddTextureSelector(_zoneEditorContainer, zone);
            EditorUIUtils.AddSliderRow(_zoneEditorContainer, "Strength", zone.TextureStrength, 0f, 1f, (v) => zone.TextureStrength = (float)v);
            
            var texBlendRow = new HBoxContainer();
            texBlendRow.AddChild(new Label { Text = "Blend Mode:", CustomMinimumSize = new Vector2(100, 0) });
            var texBlendOption = new OptionButton { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            foreach (TextureBlendMode m in Enum.GetValues(typeof(TextureBlendMode))) texBlendOption.AddItem(m.ToString(), (int)m);
            texBlendOption.Selected = (int)zone.TextureBlendMode;
            texBlendOption.ItemSelected += (idx) => zone.TextureBlendMode = (TextureBlendMode)(int)idx;
            texBlendRow.AddChild(texBlendOption);
            _zoneEditorContainer.AddChild(texBlendRow);

            // Noise
            EditorUIUtils.AddSeparator(_zoneEditorContainer);
            _zoneEditorContainer.AddChild(new Label { Text = "Height Noise", Modulate = new Color(0.8f, 0.8f, 0.8f) });
            AddNoiseEditor(_zoneEditorContainer, zone.HeightNoise, "Height");

            EditorUIUtils.AddSeparator(_zoneEditorContainer);
            _zoneEditorContainer.AddChild(new Label { Text = "Texture Noise", Modulate = new Color(0.8f, 0.8f, 0.8f) });
            AddNoiseEditor(_zoneEditorContainer, zone.TextureNoise, "Texture");
        }

        private void AddTextureSelector(VBoxContainer container, ProfileZone zone)
        {
            var row = new HBoxContainer();
            row.AddChild(new Label { Text = "Texture:", CustomMinimumSize = new Vector2(100, 0) });

            var assets = CurrentTerrain3DAssets;
            var currentThumb = new TextureRect
            {
                CustomMinimumSize = new Vector2(THUMBNAIL_SIZE, THUMBNAIL_SIZE),
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered
            };
            var currentLabel = new Label { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };

            // --- RESTORED FEATURE: Inline Texture Preview ---
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
                        if (thumb != null) {
                            var img = thumb.GetImage();
                            img?.Resize(THUMBNAIL_SIZE, THUMBNAIL_SIZE);
                            currentThumb.Texture = img != null ? ImageTexture.CreateFromImage(img) : null;
                        }
                    }
                }
            };
            updateDisplay();

            row.AddChild(currentThumb);
            row.AddChild(currentLabel);
            // ------------------------------------------------

            var selectBtn = new Button { Text = "Select..." };
            selectBtn.Pressed += () => TerrainAssetSelector.ShowTexturePopup(CurrentLayer, assets, zone.TextureId, (id) => { 
                zone.TextureId = id; 
                updateDisplay(); 
            });
            row.AddChild(selectBtn);

            var noneBtn = new Button { Text = "None" };
            noneBtn.Pressed += () => { 
                zone.TextureId = -1; 
                updateDisplay(); 
            };
            row.AddChild(noneBtn);

            container.AddChild(row);
        }

        private void AddNoiseEditor(VBoxContainer container, NoiseConfig noise, string label)
        {
             if (noise == null) return;
             
             var cb = new CheckBox { Text = $"Enable {label} Noise", ButtonPressed = noise.Enabled };
             cb.Toggled += (v) => noise.Enabled = v;
             container.AddChild(cb);

             EditorUIUtils.AddSliderRow(container, "Amplitude", noise.Amplitude, 0f, 10f, (v) => noise.Amplitude = (float)v);
             EditorUIUtils.AddSliderRow(container, "Frequency", noise.Frequency, 0.001f, 1f, (v) => noise.Frequency = (float)v);
             
             var octRow = new HBoxContainer();
             octRow.AddChild(new Label { Text = "Octaves:", CustomMinimumSize = new Vector2(100, 0) });
             var octSpin = new SpinBox { MinValue = 1, MaxValue = 8, Value = noise.Octaves, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
             octSpin.ValueChanged += (v) => noise.Octaves = (int)v;
             octRow.AddChild(octSpin);
             container.AddChild(octRow);

             var seedRow = new HBoxContainer();
             seedRow.AddChild(new Label { Text = "Seed:", CustomMinimumSize = new Vector2(100, 0) });
             var seedSpin = new SpinBox { MinValue = int.MinValue, MaxValue = int.MaxValue, Value = noise.Seed, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
             seedSpin.ValueChanged += (v) => noise.Seed = (int)v;
             seedRow.AddChild(seedSpin);
             var randBtn = new Button { Text = "🎲" };
             randBtn.Pressed += () => { noise.Seed = (int)(GD.Randi() % 100000); seedSpin.Value = noise.Seed; };
             seedRow.AddChild(randBtn);
             container.AddChild(seedRow);

             var worldCheck = new CheckBox { Text = "Use World Coords", ButtonPressed = noise.UseWorldCoords };
             worldCheck.Toggled += (v) => noise.UseWorldCoords = v;
             container.AddChild(worldCheck);
        }

        private void AddCurveEditorRow(VBoxContainer container, string label, Curve curve, Action<Curve> onChanged)
        {
            var row = new HBoxContainer();
            row.AddChild(new Label { Text = $"{label}:", CustomMinimumSize = new Vector2(100, 0) });

            var preview = new PathCurveMiniPreview(curve) { CustomMinimumSize = new Vector2(100, 40), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            row.AddChild(preview);

            var editBtn = new Button { Text = "Edit..." };
            editBtn.Pressed += () => ShowCurveEditorWindow(label, curve, (c) => { onChanged?.Invoke(c); preview.SetCurve(c); });
            row.AddChild(editBtn);

            container.AddChild(row);
        }

        private void ShowCurveEditorWindow(string title, Curve curve, Action<Curve> onAccept)
        {
            var editCurve = curve != null ? (Curve)curve.Duplicate() : CurveUtils.CreateLinearCurve();
            var window = new Window { Title = $"Edit Curve: {title}", Size = new Vector2I(500, 400), Exclusive = true, Transient = true };
            
            var vbox = new VBoxContainer();
            vbox.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            
            var editor = new InspectorCurveEditor(editCurve) { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
            vbox.AddChild(editor);

            var presets = new HBoxContainer();
            foreach(var p in new[]{"Linear", "EaseOut", "Bell", "Flat"}) {
                var b = new Button { Text = p };
                b.Pressed += () => editor.SetCurve(CurveUtils.GetPresetByName(p));
                presets.AddChild(b);
            }
            vbox.AddChild(presets);

            var btns = new HBoxContainer();
            btns.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
            var cancel = new Button { Text = "Cancel" };
            cancel.Pressed += () => { window.Hide(); window.QueueFree(); };
            btns.AddChild(cancel);
            var ok = new Button { Text = "OK" };
            ok.Pressed += () => { onAccept?.Invoke(editor.GetCurve()); window.Hide(); window.QueueFree(); };
            btns.AddChild(ok);
            vbox.AddChild(btns);

            window.AddChild(vbox);
            EditorInterface.Singleton.GetBaseControl().AddChild(window);
            window.PopupCentered();
        }

        private void AddNewZone()
        {
            CurrentLayer?.Profile?.AddZone(ProfileZone.CreateEdge(2.0f));
            RefreshProfileUI();
        }
    }
}