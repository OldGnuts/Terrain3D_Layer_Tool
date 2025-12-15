// /Editor/TerrainLayerInspector.cs
using System;
using System.Collections.Generic;
using Godot;
using Terrain3DTools.Core;
using Terrain3DTools.Layers;
using Terrain3DTools.Layers.Path; 
using TokisanGames;

namespace Terrain3DTools.Editor
{
    [Tool, GlobalClass]
    public partial class TerrainLayerInspector : EditorInspectorPlugin
    {
        private const int THUMBNAIL_SIZE = 64;
        private const int GRID_COLUMNS = 4;

        public override bool _CanHandle(GodotObject obj)
        {
            // PathLayer has its own dedicated inspector
            if (obj is PathLayer) return false;

            return obj is TerrainLayerBase;
        }

        public override bool _ParseProperty(GodotObject @object, Variant.Type type, string name, PropertyHint hintType, string hintString, PropertyUsageFlags usageFlags, bool wide)
        {
            // Let the custom UI handle these properties for TextureLayer
            if (@object is TextureLayer)
            {
                if (name == "TextureIndex" || name == "ExcludedTextureIds")
                {
                    // Return false to let default property show (hybrid mode)
                    // We add our custom UI in _ParseBegin
                    return true;
                }
            }
            return false;
        }

        public override void _ParseBegin(GodotObject obj)
        {
            if (obj is not TerrainLayerBase layer) return;

            // Add mask preview UI for all layer types
            AddMaskPreviewUI(layer);

            // Add texture selector UI only for TextureLayer
            if (obj is TextureLayer textureLayer)
            {
                AddTextureLayerUI(textureLayer);
            }
        }

        private void AddMaskPreviewUI(TerrainLayerBase layer)
        {
            var vbox = new VBoxContainer();

            var previewTex = new TextureRect
            {
                StretchMode = TextureRect.StretchModeEnum.Scale,
                CustomMinimumSize = new Vector2(0, 256f),
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
            };

            var previewButton = new Button { Text = "Generate Mask Preview" };
            previewButton.Pressed += () => GenerateMaskPreview(layer, previewButton, previewTex);

            vbox.AddChild(previewButton);
            vbox.AddChild(previewTex);

            AddCustomControl(vbox);
        }

        private void AddTextureLayerUI(TextureLayer textureLayer)
        {
            var terrain3DAssets = GetTerrain3DAssets(textureLayer);
            if (terrain3DAssets == null)
            {
                var warningLabel = new Label
                {
                    Text = "⚠️ TerrainLayerManager not found or Terrain3D not connected.\nTexture selection unavailable.",
                    AutowrapMode = TextServer.AutowrapMode.Word
                };
                warningLabel.AddThemeColorOverride("font_color", Colors.Yellow);
                AddCustomControl(warningLabel);
                return;
            }

            var textureList = terrain3DAssets.TextureList;
            if (textureList == null || textureList.Count == 0)
            {
                var warningLabel = new Label
                {
                    Text = "⚠️ No textures found in Terrain3DAssets.",
                    AutowrapMode = TextServer.AutowrapMode.Word
                };
                warningLabel.AddThemeColorOverride("font_color", Colors.Yellow);
                AddCustomControl(warningLabel);
                return;
            }

            // === TEXTURE INDEX SELECTOR ===
            AddSeparatorWithLabel("Texture Selection");

            var indexLabel = new Label { Text = $"Current Texture Index: {textureLayer.TextureIndex}" };
            AddCustomControl(indexLabel);

            var textureGrid = CreateTextureGrid(textureList, textureLayer.TextureIndex, (selectedIndex) =>
            {
                if (IsInstanceValid(indexLabel))
                {
                    indexLabel.Text = $"Current Texture Index: {selectedIndex}";
                }
            }, textureLayer);

            var textureScrollContainer = new ScrollContainer
            {
                CustomMinimumSize = new Vector2(0, THUMBNAIL_SIZE + 48),
                HorizontalScrollMode = ScrollContainer.ScrollMode.Auto,
                VerticalScrollMode = ScrollContainer.ScrollMode.Disabled,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
            };
            textureScrollContainer.AddChild(textureGrid);
            AddCustomControl(textureScrollContainer);

            // === EXCLUDED TEXTURES SELECTOR ===
            AddSeparatorWithLabel("Excluded Textures (won't be overwritten)");

            var excludedLabel = new Label
            {
                Text = GetExcludedTexturesLabel(textureLayer.ExcludedTextureIds),
                AutowrapMode = TextServer.AutowrapMode.Word
            };
            AddCustomControl(excludedLabel);

            var excludedGrid = CreateExclusionGrid(textureList, textureLayer.ExcludedTextureIds, (updatedList) =>
            {
                textureLayer.ExcludedTextureIds = updatedList;
                excludedLabel.Text = GetExcludedTexturesLabel(updatedList);
            });

            var excludedScrollContainer = new ScrollContainer
            {
                CustomMinimumSize = new Vector2(0, THUMBNAIL_SIZE + 48),
                HorizontalScrollMode = ScrollContainer.ScrollMode.Auto,
                VerticalScrollMode = ScrollContainer.ScrollMode.Disabled,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
            };
            excludedScrollContainer.AddChild(excludedGrid);
            AddCustomControl(excludedScrollContainer);
        }

        private void AddSeparatorWithLabel(string text)
        {
            var separator = new HSeparator();
            separator.CustomMinimumSize = new Vector2(0, 10);
            AddCustomControl(separator);

            var label = new Label
            {
                Text = text,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            label.AddThemeColorOverride("font_color", Colors.LightGray);
            AddCustomControl(label);
        }

        private string GetExcludedTexturesLabel(Godot.Collections.Array<int> excludedIds)
        {
            if (excludedIds == null || excludedIds.Count == 0)
                return "No textures excluded";

            return $"Excluded IDs: [{string.Join(", ", excludedIds)}]";
        }

        private Terrain3DAssets GetTerrain3DAssets(TextureLayer layer)
        {
            if (!layer.IsInsideTree()) return null;

            var manager = layer.GetTree().GetFirstNodeInGroup("terrain_layer_manager") as TerrainLayerManager;
            if (manager == null) return null;

            var terrain3DNode = manager.Terrain3DNode;
            if (terrain3DNode == null) return null;

            // Access Terrain3D.Assets
            var terrain3D = Terrain3D.Bind(terrain3DNode);
            if (terrain3D == null) return null;

            return terrain3D.Assets;
        }

        private Control CreateTextureGrid(Godot.Collections.Array textureList, int currentIndex, Action<int> onSelected, TextureLayer textureLayer)
        {
            var grid = new GridContainer
            {
                Columns = GRID_COLUMNS,
                CustomMinimumSize = new Vector2(0, 0)
            };

            // Store instance ID - we'll re-fetch the object at click time
            ulong layerInstanceId = textureLayer.GetInstanceId();

            for (int i = 0; i < textureList.Count; i++)
            {
                var resource = textureList[i].As<Resource>();
                if (resource == null) continue;

                var asset = Terrain3DTextureAsset.Bind(resource);
                if (asset == null) continue;

                int index = (int)asset.Id;

                var button = CreateTextureButton(asset, index == currentIndex, false);

                // Store ALL needed data as metadata - no closure captures
                button.SetMeta("texture_index", index);
                button.SetMeta("layer_instance_id", (long)layerInstanceId);

                // Capture only primitives and the callback
                button.Pressed += () =>
                {
                    // Re-fetch everything fresh at click time
                    if (!IsInstanceValid(button)) return;

                    int clickedIndex = button.GetMeta("texture_index").AsInt32();
                    long instanceId = button.GetMeta("layer_instance_id").AsInt64();

                    // Get fresh reference to the layer
                    var layer = GodotObject.InstanceFromId((ulong)instanceId) as TextureLayer;
                    if (layer == null)
                    {
                        GD.PrintErr($"[TextureGrid] Layer instance {instanceId} no longer valid");
                        return;
                    }

                    // Set property directly - single source of truth
                    layer.TextureIndex = clickedIndex;

                    // Update visuals if grid still valid
                    if (IsInstanceValid(grid))
                    {
                        UpdateTextureGridSelection(grid, clickedIndex);
                    }

                    // Notify callback
                    onSelected?.Invoke(clickedIndex);
                };

                grid.AddChild(button);
            }

            return grid;
        }

        private Control CreateExclusionGrid(Godot.Collections.Array textureList, Godot.Collections.Array<int> excludedIds, Action<Godot.Collections.Array<int>> onChanged)
        {
            var grid = new GridContainer
            {
                Columns = GRID_COLUMNS,
                CustomMinimumSize = new Vector2(0, 0)
            };

            var workingExcludedIds = new Godot.Collections.Array<int>(excludedIds ?? new Godot.Collections.Array<int>());

            for (int i = 0; i < textureList.Count; i++)
            {
                var resource = textureList[i].As<Resource>();
                if (resource == null) continue;

                var asset = Terrain3DTextureAsset.Bind(resource);
                if (asset == null) continue;

                int index = (int)asset.Id;
                bool isExcluded = workingExcludedIds.Contains(index);

                var button = CreateTextureButton(asset, false, isExcluded);
                button.ToggleMode = true;
                button.ButtonPressed = isExcluded;

                int capturedIndex = index;
                button.Toggled += (pressed) =>
                {
                    if (pressed && !workingExcludedIds.Contains(capturedIndex))
                    {
                        workingExcludedIds.Add(capturedIndex);
                    }
                    else if (!pressed && workingExcludedIds.Contains(capturedIndex))
                    {
                        workingExcludedIds.Remove(capturedIndex);
                    }

                    UpdateExclusionButtonStyle(button, pressed);
                    onChanged?.Invoke(new Godot.Collections.Array<int>(workingExcludedIds));
                };

                grid.AddChild(button);
            }

            return grid;
        }

        private Button CreateTextureButton(Terrain3DTextureAsset asset, bool isSelected, bool isExcluded)
        {
            var button = new Button
            {
                CustomMinimumSize = new Vector2(THUMBNAIL_SIZE + 16, THUMBNAIL_SIZE + 32),
                TooltipText = $"ID: {asset.Id}\n{asset.Name}",
                IconAlignment = HorizontalAlignment.Center,
                VerticalIconAlignment = VerticalAlignment.Top,
                ExpandIcon = true,
                Text = asset.Id.ToString(),
                Flat = false
            };

            // Set thumbnail as icon
            var thumbnail = asset.GetThumbnail();
            if (thumbnail != null)
            {
                // Resize thumbnail to fit
                var image = thumbnail.GetImage();
                if (image != null)
                {
                    image.Resize(THUMBNAIL_SIZE, THUMBNAIL_SIZE);
                    button.Icon = ImageTexture.CreateFromImage(image);
                }
            }

            // Style based on state
            if (isSelected)
            {
                ApplySelectedStyle(button);
            }
            else if (isExcluded)
            {
                ApplyExcludedStyle(button);
            }

            return button;
        }

        private void ApplySelectedStyle(Button button)
        {
            var styleBox = new StyleBoxFlat
            {
                BgColor = new Color(0.2f, 0.6f, 1.0f, 0.5f),
                BorderWidthBottom = 3,
                BorderWidthTop = 3,
                BorderWidthLeft = 3,
                BorderWidthRight = 3,
                BorderColor = Colors.White,
                ContentMarginLeft = 4,
                ContentMarginRight = 4,
                ContentMarginTop = 4,
                ContentMarginBottom = 4
            };
            button.AddThemeStyleboxOverride("normal", styleBox);
            button.AddThemeStyleboxOverride("hover", styleBox);
            button.AddThemeStyleboxOverride("pressed", styleBox);
        }

        private void ApplyExcludedStyle(Button button)
        {
            var styleBox = new StyleBoxFlat
            {
                BgColor = new Color(1.0f, 0.3f, 0.3f, 0.4f),
                BorderWidthBottom = 3,
                BorderWidthTop = 3,
                BorderWidthLeft = 3,
                BorderWidthRight = 3,
                BorderColor = new Color(1.0f, 0.5f, 0.5f),
                ContentMarginLeft = 4,
                ContentMarginRight = 4,
                ContentMarginTop = 4,
                ContentMarginBottom = 4
            };
            button.AddThemeStyleboxOverride("normal", styleBox);
            button.AddThemeStyleboxOverride("hover", styleBox);
            button.AddThemeStyleboxOverride("pressed", styleBox);
        }

        private void ClearButtonStyle(Button button)
        {
            button.RemoveThemeStyleboxOverride("normal");
            button.RemoveThemeStyleboxOverride("hover");
            button.RemoveThemeStyleboxOverride("pressed");
        }

        private void UpdateTextureGridSelection(GridContainer grid, int selectedIndex)
        {
            foreach (var child in grid.GetChildren())
            {
                if (child is Button btn)
                {
                    int btnIndex = btn.GetMeta("texture_index").AsInt32();
                    bool isSelected = btnIndex == selectedIndex;

                    if (isSelected)
                    {
                        ApplySelectedStyle(btn);
                    }
                    else
                    {
                        ClearButtonStyle(btn);
                    }
                }
            }
        }

        private void UpdateExclusionButtonStyle(Button button, bool isExcluded)
        {
            if (isExcluded)
            {
                ApplyExcludedStyle(button);
            }
            else
            {
                ClearButtonStyle(button);
            }
        }

        private void GenerateMaskPreview(TerrainLayerBase layer, Button previewButton, TextureRect previewTex)
        {
            // Safety Checks
            if (!IsInstanceValid(layer) || !layer.IsInsideTree())
            {
                GD.PrintErr("Cannot generate preview. Layer is not valid or not in the scene tree.");
                return;
            }
            var manager = layer.GetTree().GetFirstNodeInGroup("terrain_layer_manager") as TerrainLayerManager;
            if (manager == null)
            {
                GD.PrintErr("Cannot generate preview. TerrainLayerManager not found in the scene.");
                return;
            }

            previewButton.Disabled = true;
            previewButton.Text = "Generating...";

            layer.PrepareMaskResources(false);
            if (!layer.layerTextureRID.IsValid)
            {
                GD.PrintErr("Layer's texture RID is invalid after preparation. Aborting.");
                previewButton.Disabled = false;
                previewButton.Text = "Generate Mask Preview";
                return;
            }

            Action onComplete = () =>
            {
                if (!IsInstanceValid(previewTex)) return;

                int maskWidth = layer.Size.X;
                int maskHeight = layer.Size.Y;

                var data = Gpu.Rd.TextureGetData(layer.layerTextureRID, 0);
                if (data == null || data.Length == 0) return;

                var rfImage = Image.CreateFromData(maskWidth, maskHeight, false, Image.Format.Rf, data);

                var displayImage = Image.CreateEmpty(maskWidth, maskHeight, false, Image.Format.Rgba8);
                for (int y = 0; y < rfImage.GetHeight(); y++)
                {
                    for (int x = 0; x < rfImage.GetWidth(); x++)
                    {
                        float value = Mathf.Clamp(rfImage.GetPixel(x, y).R, 0.0f, 1.0f);
                        displayImage.SetPixel(x, y, new Color(value, value, value, 1.0f));
                    }
                }

                previewTex.Texture = ImageTexture.CreateFromImage(displayImage);

                previewButton.Disabled = false;
                previewButton.Text = "Generate Mask Preview";
            };

            AsyncGpuTask task = null;
            if (layer.GetLayerType() == LayerType.Feature)
            {
                task = Pipeline.LayerMaskPipeline.CreateUpdateFeatureLayerTextureTask(
                    layer.layerTextureRID,
                    (FeatureLayer)layer,
                    layer.Size.X,
                    layer.Size.Y,
                    new List<AsyncGpuTask>(),
                    onComplete
                );
            }
            else
            {
                task = Pipeline.LayerMaskPipeline.CreateUpdateLayerTextureTask(
                    layer.layerTextureRID,
                    layer,
                    layer.Size.X,
                    layer.Size.Y,
                    new Rid(),
                    new Rid(),
                    0,
                    null,
                    onComplete
                );
            }

            if (task != null)
            {
                AsyncGpuTaskManager.Instance.AddTask(task);
            }
            else
            {
                previewButton.Disabled = false;
                previewButton.Text = "Generate Mask Preview";
            }
        }
    }
}