// /Editor/Utils/TerrainMeshAssetSelector.cs (continued)
using System;
using Godot;
using Terrain3DTools.Core;
using TokisanGames;

namespace Terrain3DTools.Editor.Utils
{
    /// <summary>
    /// Helpers for interacting with Terrain3DAssets and Mesh selection.
    /// Follows the same patterns as TerrainAssetSelector for textures.
    /// </summary>
    public static class TerrainMeshAssetSelector
    {
        private const int THUMBNAIL_SIZE = 48;
        private const int GRID_COLUMNS = 4;

        public static Terrain3DAssets GetAssets(Node layerNode)
        {
            if (layerNode == null || !layerNode.IsInsideTree()) return null;

            var manager = layerNode.GetTree().GetFirstNodeInGroup("terrain_layer_manager") as TerrainLayerManager;
            if (manager?.Terrain3DNode == null) return null;

            var terrain3D = Terrain3D.Bind(manager.Terrain3DNode);
            return terrain3D?.Assets;
        }

        /// <summary>
        /// Shows a mesh selection popup. Behavior adapts based on context.
        /// </summary>
        public static void ShowMeshPopup(
            Node contextNode,
            Terrain3DAssets assets,
            int currentId,
            Action<int> onSelected)
        {
            if (assets == null) return;

            Window parentWindow = FindParentWindow(contextNode);
            Window mainEditorWindow = EditorInterface.Singleton.GetBaseControl().GetWindow();

            if (parentWindow != null && parentWindow != mainEditorWindow)
            {
                ShowMeshPopupAsWindow(parentWindow, assets, currentId, onSelected);
            }
            else
            {
                ShowMeshPopupAsPanel(assets, currentId, onSelected);
            }
        }

        private static Window FindParentWindow(Node contextNode)
        {
            if (contextNode == null) return null;
            if (contextNode is Window window) return window;

            var current = contextNode.GetParent();
            while (current != null)
            {
                if (current is Window parentWindow) return parentWindow;
                current = current.GetParent();
            }
            return null;
        }

        private static void ShowMeshPopupAsWindow(
            Window parentWindow,
            Terrain3DAssets assets,
            int currentId,
            Action<int> onSelected)
        {
            var popup = new Window
            {
                Title = "Select Mesh",
                Size = new Vector2I(380, 420),
                Exclusive = false,
                Transient = true,
                Unresizable = false,
                MinSize = new Vector2I(300, 300)
            };

            popup.CloseRequested += () =>
            {
                popup.Hide();
                popup.QueueFree();
            };

            var content = BuildMeshSelectionContent(assets, currentId, (id) =>
            {
                onSelected?.Invoke(id);
                popup.Hide();
                popup.QueueFree();
            }, () =>
            {
                popup.Hide();
                popup.QueueFree();
            });

            popup.AddChild(content);
            parentWindow.AddChild(popup);
            popup.PopupCentered();
        }

        private static void ShowMeshPopupAsPanel(
            Terrain3DAssets assets,
            int currentId,
            Action<int> onSelected)
        {
            var popup = new PopupPanel();
            popup.Size = new Vector2I(380, 380);

            var content = BuildMeshSelectionContent(assets, currentId, (id) =>
            {
                onSelected?.Invoke(id);
                popup.Hide();
                popup.QueueFree();
            }, () =>
            {
                popup.Hide();
                popup.QueueFree();
            });

            popup.AddChild(content);

            var editorBase = EditorInterface.Singleton.GetBaseControl();
            editorBase.AddChild(popup);
            popup.PopupCentered();
        }

        private static Control BuildMeshSelectionContent(
            Terrain3DAssets assets,
            int currentId,
            Action<int> onSelected,
            Action onCancel)
        {
            var bg = new PanelContainer();
            bg.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            var bgStyle = new StyleBoxFlat
            {
                BgColor = new Color(0.15f, 0.15f, 0.17f),
                ContentMarginLeft = 8,
                ContentMarginRight = 8,
                ContentMarginTop = 8,
                ContentMarginBottom = 8
            };
            bg.AddThemeStyleboxOverride("panel", bgStyle);

            var vbox = new VBoxContainer();
            vbox.AddThemeConstantOverride("separation", 8);
            vbox.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            vbox.SizeFlagsVertical = Control.SizeFlags.ExpandFill;

            // Header
            var headerLabel = new Label
            {
                Text = "Select Mesh Asset",
                HorizontalAlignment = HorizontalAlignment.Center
            };
            headerLabel.AddThemeFontSizeOverride("font_size", 14);
            vbox.AddChild(headerLabel);

            // "None" option button
            var noneButton = new Button
            {
                Text = "None (No Mesh)",
                CustomMinimumSize = new Vector2(0, 32),
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
            };

            if (currentId < 0)
            {
                ApplySelectedStyle(noneButton);
            }

            noneButton.Pressed += () => onSelected?.Invoke(-1);
            vbox.AddChild(noneButton);

            // Scrollable mesh grid
            var scroll = new ScrollContainer
            {
                CustomMinimumSize = new Vector2(340, 260),
                SizeFlagsVertical = Control.SizeFlags.ExpandFill,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
            };

            var grid = new GridContainer
            {
                Columns = GRID_COLUMNS,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
            };
            grid.AddThemeConstantOverride("h_separation", 4);
            grid.AddThemeConstantOverride("v_separation", 4);

            // Populate mesh buttons
            var meshList = assets.MeshList;
            if (meshList != null)
            {
                for (int i = 0; i < meshList.Count; i++)
                {
                    var resource = meshList[i].As<Resource>();
                    if (resource == null) continue;

                    var asset = Terrain3DMeshAsset.Bind(resource);
                    if (asset == null) continue;

                    var button = CreateMeshButton(asset, currentId, onSelected);
                    grid.AddChild(button);
                }
            }

            // Show message if no meshes available
            if (grid.GetChildCount() == 0)
            {
                var noMeshLabel = new Label
                {
                    Text = "No mesh assets configured in Terrain3D.\nAdd meshes in Terrain3D's Assets panel.",
                    AutowrapMode = TextServer.AutowrapMode.Word,
                    HorizontalAlignment = HorizontalAlignment.Center
                };
                noMeshLabel.AddThemeColorOverride("font_color", new Color(0.6f, 0.6f, 0.6f));
                grid.AddChild(noMeshLabel);
            }

            scroll.AddChild(grid);
            vbox.AddChild(scroll);

            // Cancel button
            var cancelButton = new Button
            {
                Text = "Cancel",
                CustomMinimumSize = new Vector2(0, 28),
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
            };
            cancelButton.Pressed += () => onCancel?.Invoke();
            vbox.AddChild(cancelButton);

            bg.AddChild(vbox);
            return bg;
        }

        private static Button CreateMeshButton(
            Terrain3DMeshAsset asset,
            int currentId,
            Action<int> onSelected)
        {
            var button = new Button
            {
                CustomMinimumSize = new Vector2(THUMBNAIL_SIZE + 16, THUMBNAIL_SIZE + 32),
                TooltipText = $"[{asset.Id}] {asset.Name}",
                Text = asset.Name.Length > 8 ? asset.Name.Substring(0, 8) + "..." : asset.Name,
                IconAlignment = HorizontalAlignment.Center,
                VerticalIconAlignment = VerticalAlignment.Top,
                ClipText = true
            };

            // Load thumbnail
            var thumb = asset.GetThumbnail();
            if (thumb != null)
            {
                var img = thumb.GetImage();
                img?.Resize(THUMBNAIL_SIZE, THUMBNAIL_SIZE);
                button.Icon = img != null ? ImageTexture.CreateFromImage(img) : null;
            }
            else
            {
                // Create a placeholder icon
                var placeholderImg = Image.CreateEmpty(THUMBNAIL_SIZE, THUMBNAIL_SIZE, false, Image.Format.Rgba8);
                placeholderImg.Fill(new Color(0.3f, 0.3f, 0.35f));
                button.Icon = ImageTexture.CreateFromImage(placeholderImg);
            }

            int capturedId = (int)asset.Id;

            if (currentId == capturedId)
            {
                ApplySelectedStyle(button);
            }

            button.Pressed += () => onSelected?.Invoke(capturedId);

            return button;
        }

        private static void ApplySelectedStyle(Button button)
        {
            var style = new StyleBoxFlat
            {
                BgColor = new Color(0.2f, 0.6f, 1.0f, 0.5f),
                BorderWidthBottom = 2,
                BorderWidthTop = 2,
                BorderWidthLeft = 2,
                BorderWidthRight = 2,
                BorderColor = Colors.White,
                CornerRadiusBottomLeft = 3,
                CornerRadiusBottomRight = 3,
                CornerRadiusTopLeft = 3,
                CornerRadiusTopRight = 3
            };
            button.AddThemeStyleboxOverride("normal", style);
            button.AddThemeStyleboxOverride("hover", style);
        }

        /// <summary>
        /// Gets the name of a mesh asset by ID.
        /// Returns null if not found.
        /// </summary>
        public static string GetMeshName(Terrain3DAssets assets, int meshId)
        {
            if (assets == null || meshId < 0) return null;

            var meshList = assets.MeshList;
            if (meshList == null) return null;

            for (int i = 0; i < meshList.Count; i++)
            {
                var resource = meshList[i].As<Resource>();
                if (resource == null) continue;

                var asset = Terrain3DMeshAsset.Bind(resource);
                if (asset != null && asset.Id == meshId)
                {
                    return asset.Name;
                }
            }
            return null;
        }

        /// <summary>
        /// Gets the thumbnail for a mesh asset by ID.
        /// Returns null if not found.
        /// </summary>
        public static Texture2D GetMeshThumbnail(Terrain3DAssets assets, int meshId)
        {
            if (assets == null || meshId < 0) return null;

            var meshList = assets.MeshList;
            if (meshList == null) return null;

            for (int i = 0; i < meshList.Count; i++)
            {
                var resource = meshList[i].As<Resource>();
                if (resource == null) continue;

                var asset = Terrain3DMeshAsset.Bind(resource);
                if (asset != null && asset.Id == meshId)
                {
                    return asset.GetThumbnail();
                }
            }
            return null;
        }
    }
}