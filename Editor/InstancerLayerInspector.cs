// /Editor/InstancerLayerInspector.cs
using System;
using System.Collections.Generic;
using Godot;
using Terrain3DTools.Layers;
using Terrain3DTools.Layers.Instancer;
using Terrain3DTools.Editor.Utils;
using TokisanGames;

namespace Terrain3DTools.Editor
{
    /// <summary>
    /// Custom inspector for InstancerLayer nodes.
    /// Provides UI for mesh configuration, placement settings, and exclusion options.
    /// </summary>
    [Tool, GlobalClass]
    public partial class InstancerLayerInspector : EditorInspectorPlugin
    {
        #region Constants
        private const int THUMBNAIL_SIZE = 40;
        #endregion

        #region State
        private ulong _currentLayerInstanceId;
        private VBoxContainer _mainContainer;
        private Dictionary<string, bool> _sectionExpanded = new();
        private Dictionary<string, VBoxContainer> _sectionContents = new();
        private bool _layerSignalsConnected = false;
        #endregion

        #region Helper Properties
        private InstancerLayer CurrentLayer
        {
            get
            {
                if (_currentLayerInstanceId == 0) return null;
                var obj = GodotObject.InstanceFromId(_currentLayerInstanceId);
                return obj as InstancerLayer;
            }
        }

        private Terrain3DAssets CurrentTerrain3DAssets
            => TerrainMeshAssetSelector.GetAssets(CurrentLayer);
        #endregion

        #region EditorInspectorPlugin Overrides
        public override bool _CanHandle(GodotObject obj)
        {
            return obj is InstancerLayer;
        }

        public override bool _ParseProperty(GodotObject @object, Variant.Type type, string name,
            PropertyHint hintType, string hintString, PropertyUsageFlags usageFlags, bool wide)
        {
            var hiddenProperties = new HashSet<string>
            {
                "MeshEntries",
                "BaseDensity",
                "MinimumSpacing",
                "Seed",
                "ExclusionThreshold",
            };

            if (hiddenProperties.Contains(name))
            {
                return true;
            }

            return false;
        }

        public override void _ParseBegin(GodotObject obj)
        {
            if (obj is not InstancerLayer instancerLayer) return;

            ulong newInstanceId = instancerLayer.GetInstanceId();

            if (_currentLayerInstanceId != 0 && _currentLayerInstanceId != newInstanceId)
            {
                DisconnectSignals();
            }

            _currentLayerInstanceId = newInstanceId;

            if (_sectionExpanded.Count == 0)
            {
                _sectionExpanded = new Dictionary<string, bool>
                {
                    { "Mesh Configuration", true },
                    { "Placement Settings", true },
                    { "Exclusion Settings", false }
                };
            }
            _sectionContents.Clear();

            _mainContainer = new VBoxContainer();
            _mainContainer.AddThemeConstantOverride("separation", 8);

            if (CurrentTerrain3DAssets == null)
            {
                AddTerrainWarning();
                AddCustomControl(_mainContainer);
                return;
            }

            BuildMeshConfigurationSection();
            EditorUIUtils.AddSeparator(_mainContainer);

            BuildPlacementSettingsSection();
            EditorUIUtils.AddSeparator(_mainContainer);

            BuildExclusionSettingsSection();

            AddCustomControl(_mainContainer);
            ConnectSignals();
        }

        public override void _ParseEnd(GodotObject obj)
        {
            // Keep instance ID for potential refresh
        }
        #endregion

        #region Signal Management
        private void ConnectSignals()
        {
            var layer = CurrentLayer;
            if (layer == null || _layerSignalsConnected) return;
            _layerSignalsConnected = true;
        }

        private void DisconnectSignals()
        {
            if (!_layerSignalsConnected) return;
            _layerSignalsConnected = false;
        }
        #endregion

        #region Section Builders

        private VBoxContainer CreateSection(string title, bool defaultExpanded = true)
        {
            bool isExpanded = _sectionExpanded.TryGetValue(title, out var expanded) ? expanded : defaultExpanded;

            var result = EditorUIUtils.CreateCollapsibleSection(
                _mainContainer,
                title,
                isExpanded,
                _sectionExpanded
            );

            _sectionContents[title] = result.container;
            return result.container;
        }

        private void AddTerrainWarning()
        {
            var warningContainer = new VBoxContainer();
            warningContainer.AddThemeConstantOverride("separation", 8);

            var warningLabel = new Label
            {
                Text = "⚠️ Terrain3D Connection Required",
                HorizontalAlignment = HorizontalAlignment.Center
            };
            warningLabel.AddThemeColorOverride("font_color", new Color(1.0f, 0.8f, 0.2f));
            warningLabel.AddThemeFontSizeOverride("font_size", 14);
            warningContainer.AddChild(warningLabel);

            var detailLabel = new Label
            {
                Text = "TerrainLayerManager not found or Terrain3D not connected.\n" +
                       "Mesh selection is unavailable until connected.",
                AutowrapMode = TextServer.AutowrapMode.Word,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            detailLabel.AddThemeColorOverride("font_color", new Color(0.7f, 0.7f, 0.7f));
            warningContainer.AddChild(detailLabel);

            _mainContainer.AddChild(warningContainer);
        }

        private void BuildMeshConfigurationSection()
        {
            var section = CreateSection("Mesh Configuration", true);

            var layer = CurrentLayer;
            if (layer == null) return;

            // Mesh entries list
            var meshEntriesContainer = new VBoxContainer();
            meshEntriesContainer.AddThemeConstantOverride("separation", 6);

            RefreshMeshEntriesList(meshEntriesContainer);

            section.AddChild(meshEntriesContainer);

            // Add mesh button
            var addButton = new Button
            {
                Text = "+ Add Mesh Entry",
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
            };
            addButton.Pressed += () =>
            {
                var newEntry = new InstancerMeshEntry();
                layer.MeshEntries.Add(newEntry);
                layer.NotifyPropertyListChanged();
            };
            section.AddChild(addButton);
        }

        private void RefreshMeshEntriesList(VBoxContainer container)
        {
            // Clear existing
            foreach (var child in container.GetChildren())
            {
                child.QueueFree();
            }

            var layer = CurrentLayer;
            if (layer == null) return;

            var assets = CurrentTerrain3DAssets;

            for (int i = 0; i < layer.MeshEntries.Count; i++)
            {
                var entry = layer.MeshEntries[i];
                if (entry == null) continue;

                var entryUI = CreateMeshEntryUI(entry, i, assets, container);
                container.AddChild(entryUI);
            }

            if (layer.MeshEntries.Count == 0)
            {
                var emptyLabel = new Label
                {
                    Text = "No mesh entries. Add one below.",
                    HorizontalAlignment = HorizontalAlignment.Center
                };
                emptyLabel.AddThemeColorOverride("font_color", new Color(0.6f, 0.6f, 0.6f));
                container.AddChild(emptyLabel);
            }
        }

        private Control CreateMeshEntryUI(InstancerMeshEntry entry, int index, Terrain3DAssets assets, VBoxContainer parentContainer)
        {
            var entryContainer = new PanelContainer();
            var entryStyle = new StyleBoxFlat
            {
                BgColor = new Color(0.18f, 0.18f, 0.2f),
                ContentMarginLeft = 8,
                ContentMarginRight = 8,
                ContentMarginTop = 6,
                ContentMarginBottom = 6,
                CornerRadiusBottomLeft = 4,
                CornerRadiusBottomRight = 4,
                CornerRadiusTopLeft = 4,
                CornerRadiusTopRight = 4
            };
            entryContainer.AddThemeStyleboxOverride("panel", entryStyle);

            var vbox = new VBoxContainer();
            vbox.AddThemeConstantOverride("separation", 4);

            // Header row with mesh selection and remove button
            var headerRow = new HBoxContainer();
            headerRow.AddThemeConstantOverride("separation", 8);

            // Thumbnail
            var thumbnailRect = new TextureRect
            {
                CustomMinimumSize = new Vector2(THUMBNAIL_SIZE, THUMBNAIL_SIZE),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered
            };

            if (entry.MeshAssetId >= 0 && assets != null)
            {
                var thumb = TerrainMeshAssetSelector.GetMeshThumbnail(assets, entry.MeshAssetId);
                if (thumb != null)
                {
                    var img = thumb.GetImage();
                    img?.Resize(THUMBNAIL_SIZE, THUMBNAIL_SIZE);
                    thumbnailRect.Texture = img != null ? ImageTexture.CreateFromImage(img) : null;
                }
            }

            if (thumbnailRect.Texture == null)
            {
                var placeholder = Image.CreateEmpty(THUMBNAIL_SIZE, THUMBNAIL_SIZE, false, Image.Format.Rgba8);
                placeholder.Fill(new Color(0.25f, 0.25f, 0.3f));
                thumbnailRect.Texture = ImageTexture.CreateFromImage(placeholder);
            }

            headerRow.AddChild(thumbnailRect);

            // Mesh selection button
            var meshName = entry.MeshAssetId >= 0
                ? TerrainMeshAssetSelector.GetMeshName(assets, entry.MeshAssetId) ?? $"Mesh {entry.MeshAssetId}"
                : "Select Mesh...";

            var selectButton = new Button
            {
                Text = meshName,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                ClipText = true
            };

            var capturedEntry = entry;
            var capturedIndex = index;

            selectButton.Pressed += () =>
            {
                TerrainMeshAssetSelector.ShowMeshPopup(
                    selectButton,
                    assets,
                    capturedEntry.MeshAssetId,
                    (newId) =>
                    {
                        capturedEntry.MeshAssetId = newId;
                        CurrentLayer?.NotifyPropertyListChanged();
                    });
            };

            headerRow.AddChild(selectButton);

            // Remove button
            var removeButton = new Button
            {
                Text = "✕",
                CustomMinimumSize = new Vector2(28, 28),
                TooltipText = "Remove this mesh entry"
            };

            removeButton.Pressed += () =>
            {
                var layer = CurrentLayer;
                if (layer != null && capturedIndex < layer.MeshEntries.Count)
                {
                    layer.MeshEntries.RemoveAt(capturedIndex);
                    layer.NotifyPropertyListChanged();
                }
            };

            headerRow.AddChild(removeButton);
            vbox.AddChild(headerRow);

            // Properties (collapsible)
            var propsContent = EditorUIUtils.CreateInlineCollapsible(
                vbox, "Properties", false, $"meshentry_{index}_props", _sectionExpanded);

            // Probability weight
            EditorUIUtils.AddSliderRow(propsContent, "Weight", entry.ProbabilityWeight, 0.01f, 10f,
                (v) => capturedEntry.ProbabilityWeight = (float)v, "F2");

            // Scale range
            var scaleRow = new HBoxContainer();
            scaleRow.AddChild(new Label { Text = "Scale:", CustomMinimumSize = new Vector2(80, 0) });

            var minScaleSpin = new SpinBox
            {
                MinValue = 0.01,
                MaxValue = 10,
                Step = 0.05,
                Value = entry.ScaleRange.X,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
            };
            minScaleSpin.ValueChanged += (v) =>
            {
                capturedEntry.ScaleRange = new Vector2((float)v, capturedEntry.ScaleRange.Y);
            };

            var maxScaleSpin = new SpinBox
            {
                MinValue = 0.01,
                MaxValue = 10,
                Step = 0.05,
                Value = entry.ScaleRange.Y,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
            };
            maxScaleSpin.ValueChanged += (v) =>
            {
                capturedEntry.ScaleRange = new Vector2(capturedEntry.ScaleRange.X, (float)v);
            };

            scaleRow.AddChild(minScaleSpin);
            scaleRow.AddChild(new Label { Text = "-" });
            scaleRow.AddChild(maxScaleSpin);
            propsContent.AddChild(scaleRow);

            // Y Rotation
            EditorUIUtils.AddSliderRow(propsContent, "Y Rotation", entry.YRotationRange, 0f, 360f,
                (v) => capturedEntry.YRotationRange = (float)v, "F0");

            // Align to normal
            EditorUIUtils.AddCheckBoxRow(propsContent, "Align to Normal", entry.AlignToNormal,
                (v) => capturedEntry.AlignToNormal = v);

            // Normal alignment strength
            EditorUIUtils.AddSliderRow(propsContent, "Normal Strength", entry.NormalAlignmentStrength, 0f, 1f,
                (v) => capturedEntry.NormalAlignmentStrength = (float)v, "F2");

            // Height offset
            EditorUIUtils.AddSliderRow(propsContent, "Height Offset", entry.HeightOffset, -10f, 10f,
                (v) => capturedEntry.HeightOffset = (float)v, "F2");

            entryContainer.AddChild(vbox);
            return entryContainer;
        }

        private void BuildPlacementSettingsSection()
        {
            var section = CreateSection("Placement Settings", true);

            var layer = CurrentLayer;
            if (layer == null) return;

            // Base Density
            EditorUIUtils.AddSliderRow(section, "Base Density", layer.BaseDensity, 0.01f, 2f,
                (v) =>
                {
                    if (CurrentLayer != null) CurrentLayer.BaseDensity = (float)v;
                }, "F2");

            var densityHint = new Label
            {
                Text = "Instances per square meter at full mask intensity",
                AutowrapMode = TextServer.AutowrapMode.Word
            };
            densityHint.AddThemeColorOverride("font_color", new Color(0.6f, 0.6f, 0.6f));
            densityHint.AddThemeFontSizeOverride("font_size", 11);
            section.AddChild(densityHint);

            EditorUIUtils.AddSeparator(section, 4);

            // Minimum Spacing
            EditorUIUtils.AddSliderRow(section, "Min Spacing", layer.MinimumSpacing, 0.1f, 50f,
                (v) =>
                {
                    if (CurrentLayer != null) CurrentLayer.MinimumSpacing = (float)v;
                }, "F1");

            var spacingHint = new Label
            {
                Text = "Minimum distance between instances (cell size)",
                AutowrapMode = TextServer.AutowrapMode.Word
            };
            spacingHint.AddThemeColorOverride("font_color", new Color(0.6f, 0.6f, 0.6f));
            spacingHint.AddThemeFontSizeOverride("font_size", 11);
            section.AddChild(spacingHint);

            EditorUIUtils.AddSeparator(section, 4);

            // Seed
            var seedRow = new HBoxContainer();
            seedRow.AddChild(new Label
            {
                Text = "Seed:",
                CustomMinimumSize = new Vector2(100, 0)
            });

            var seedSpin = new SpinBox
            {
                MinValue = int.MinValue,
                MaxValue = int.MaxValue,
                Value = layer.Seed,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
            };
            seedSpin.ValueChanged += (v) =>
            {
                if (CurrentLayer != null) CurrentLayer.Seed = (int)v;
            };
            seedRow.AddChild(seedSpin);

            var randomizeButton = new Button
            {
                Text = "🎲",
                TooltipText = "Randomize seed"
            };
            randomizeButton.Pressed += () =>
            {
                var newSeed = (int)(GD.Randi() % 1000000);
                seedSpin.Value = newSeed;
                if (CurrentLayer != null) CurrentLayer.Seed = newSeed;
            };
            seedRow.AddChild(randomizeButton);

            section.AddChild(seedRow);
        }

        private void BuildExclusionSettingsSection()
        {
            var section = CreateSection("Exclusion Settings", false);

            var layer = CurrentLayer;
            if (layer == null) return;

            // Exclusion threshold
            EditorUIUtils.AddSliderRow(section, "Exclusion Threshold", layer.ExclusionThreshold, 0f, 1f,
                (v) =>
                {
                    if (CurrentLayer != null) CurrentLayer.ExclusionThreshold = (float)v;
                }, "F2");

            var thresholdHint = new Label
            {
                Text = "Exclusion map value above which instances are blocked.\n" +
                       "Lower = more strict, Higher = more permissive.",
                AutowrapMode = TextServer.AutowrapMode.Word
            };
            thresholdHint.AddThemeColorOverride("font_color", new Color(0.6f, 0.6f, 0.6f));
            thresholdHint.AddThemeFontSizeOverride("font_size", 11);
            section.AddChild(thresholdHint);
        }

        #endregion
    }
}