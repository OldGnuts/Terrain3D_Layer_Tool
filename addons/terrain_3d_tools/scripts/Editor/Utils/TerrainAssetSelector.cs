// /Editor/Utils/TerrainAssetSelector.cs
using System;
using Godot;
using Terrain3DTools.Core;
using TokisanGames;

namespace Terrain3DTools.Editor.Utils
{
    /// <summary>
    /// Helpers for interacting with Terrain3DAssets and Texture selection.
    /// </summary>
    public static class TerrainAssetSelector
    {
        private const int THUMBNAIL_SIZE = 48;
        private const int GRID_COLUMNS = 6;

        public static Terrain3DAssets GetAssets(Node layerNode)
        {
            if (layerNode == null || !layerNode.IsInsideTree()) return null;

            var manager = layerNode.GetTree().GetFirstNodeInGroup("terrain_layer_manager") as TerrainLayerManager;
            if (manager?.Terrain3DNode == null) return null;

            var terrain3D = Terrain3D.Bind(manager.Terrain3DNode);
            return terrain3D?.Assets;
        }

        /// <summary>
        /// Shows a texture selection popup. Behavior adapts based on context:
        /// - From a Window: Creates a transient child window
        /// - From inspector/other: Uses PopupPanel on EditorInterface
        /// </summary>
        public static void ShowTexturePopup(
            Node contextNode,
            Terrain3DAssets assets,
            int currentId,
            Action<int> onSelected)
        {
            if (assets == null) return;

            // Determine if we're in a Window context
            Window parentWindow = FindParentWindow(contextNode);

            // Get the main editor window to compare against
            Window mainEditorWindow = EditorInterface.Singleton.GetBaseControl().GetWindow();

            if (parentWindow != null && parentWindow != mainEditorWindow)
            {
                // Use Window-based popup for custom Window contexts (like GlobalSettingsWindow)
                ShowTexturePopupAsWindow(parentWindow, assets, currentId, onSelected);
            }
            else
            {
                // Use PopupPanel for inspector/editor contexts (original behavior)
                ShowTexturePopupAsPanel(assets, currentId, onSelected);
            }
        }

        /// <summary>
        /// Finds the nearest Window ancestor, or null if none exists.
        /// </summary>
        private static Window FindParentWindow(Node contextNode)
        {
            if (contextNode == null) return null;

            // If contextNode itself is a Window, use it
            if (contextNode is Window window)
            {
                return window;
            }

            // Search up the tree for a Window ancestor
            var current = contextNode.GetParent();
            while (current != null)
            {
                if (current is Window parentWindow)
                {
                    return parentWindow;
                }
                current = current.GetParent();
            }

            return null;
        }

        /// <summary>
        /// Shows texture popup as a Window - used when called from another Window.
        /// This prevents the parent window from closing when the popup closes.
        /// </summary>
        private static void ShowTexturePopupAsWindow(
            Window parentWindow,
            Terrain3DAssets assets,
            int currentId,
            Action<int> onSelected)
        {
            var popup = new Window
            {
                Title = "Select Texture",
                Size = new Vector2I(420, 380),
                Exclusive = false,
                Transient = true,
                Unresizable = false,
                MinSize = new Vector2I(300, 250)
            };

            // Handle close request
            popup.CloseRequested += () =>
            {
                popup.Hide();
                popup.QueueFree();
            };

            // Build the content
            var content = BuildTextureSelectionContent(assets, currentId, (id) =>
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

            // Add to parent window
            parentWindow.AddChild(popup);
            popup.PopupCentered();
        }

        /// <summary>
        /// Shows texture popup as a PopupPanel - used for inspector contexts.
        /// This is the original behavior that works well with the editor inspector.
        /// </summary>
        private static void ShowTexturePopupAsPanel(
            Terrain3DAssets assets,
            int currentId,
            Action<int> onSelected)
        {
            var popup = new PopupPanel();
            popup.Size = new Vector2I(420, 340);

            // Build the content
            var content = BuildTextureSelectionContent(assets, currentId, (id) =>
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

        /// <summary>
        /// Builds the texture selection UI content. Shared between Window and PopupPanel versions.
        /// </summary>
        private static Control BuildTextureSelectionContent(
            Terrain3DAssets assets,
            int currentId,
            Action<int> onSelected,
            Action onCancel)
        {
            // Background panel
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
                Text = "Select Texture",
                HorizontalAlignment = HorizontalAlignment.Center
            };
            headerLabel.AddThemeFontSizeOverride("font_size", 14);
            vbox.AddChild(headerLabel);

            // "None" option button
            var noneButton = new Button
            {
                Text = "None (No Texture)",
                CustomMinimumSize = new Vector2(0, 32),
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
            };

            if (currentId < 0)
            {
                ApplySelectedStyle(noneButton);
            }

            noneButton.Pressed += () => onSelected?.Invoke(-1);
            vbox.AddChild(noneButton);

            // Scrollable texture grid
            var scroll = new ScrollContainer
            {
                CustomMinimumSize = new Vector2(380, 220),
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

            // Populate texture buttons
            for (int i = 0; i < assets.TextureList.Count; i++)
            {
                var resource = assets.TextureList[i].As<Resource>();
                if (resource == null) continue;

                var asset = Terrain3DTextureAsset.Bind(resource);
                if (asset == null) continue;

                var button = CreateTextureButton(asset, currentId, onSelected);
                grid.AddChild(button);
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

        /// <summary>
        /// Creates a button for a single texture asset.
        /// </summary>
        private static Button CreateTextureButton(
            Terrain3DTextureAsset asset,
            int currentId,
            Action<int> onSelected)
        {
            var button = new Button
            {
                CustomMinimumSize = new Vector2(THUMBNAIL_SIZE + 8, THUMBNAIL_SIZE + 24),
                TooltipText = $"[{asset.Id}] {asset.Name}",
                Text = asset.Id.ToString(),
                IconAlignment = HorizontalAlignment.Center,
                VerticalIconAlignment = VerticalAlignment.Top
            };

            // Load thumbnail
            var thumb = asset.GetThumbnail();
            if (thumb != null)
            {
                var img = thumb.GetImage();
                img?.Resize(THUMBNAIL_SIZE, THUMBNAIL_SIZE);
                button.Icon = img != null ? ImageTexture.CreateFromImage(img) : null;
            }

            int capturedId = (int)asset.Id;

            // Apply selected style if this is the current texture
            if (currentId == capturedId)
            {
                ApplySelectedStyle(button);
            }

            button.Pressed += () => onSelected?.Invoke(capturedId);

            return button;
        }

        /// <summary>
        /// Applies the "selected" visual style to a button.
        /// </summary>
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
    }
}