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

            // Assuming Terrain3D.Bind is available or using reflection/casting
            // If Terrain3D is a C# class:
            // return manager.Terrain3DNode.Assets;
            
            // If using the wrapper pattern from your inspector:
            var terrain3D = Terrain3D.Bind(manager.Terrain3DNode);
            return terrain3D?.Assets;
        }

        public static void ShowTexturePopup(
            Node contextNode, 
            Terrain3DAssets assets, 
            int currentId, 
            Action<int> onSelected)
        {
            if (assets == null) return;

            var popup = new PopupPanel();
            popup.Size = new Vector2I(400, 300);

            var vbox = new VBoxContainer();
            vbox.AddThemeConstantOverride("separation", 8);
            vbox.AddChild(new Label { Text = "Select Texture" });

            var scroll = new ScrollContainer
            {
                CustomMinimumSize = new Vector2(380, 250),
                SizeFlagsVertical = Control.SizeFlags.ExpandFill
            };

            var grid = new GridContainer { Columns = GRID_COLUMNS };

            for (int i = 0; i < assets.TextureList.Count; i++)
            {
                var resource = assets.TextureList[i].As<Resource>();
                if (resource == null) continue;

                var asset = Terrain3DTextureAsset.Bind(resource);
                if (asset == null) continue;

                var button = new Button
                {
                    CustomMinimumSize = new Vector2(THUMBNAIL_SIZE + 8, THUMBNAIL_SIZE + 24),
                    TooltipText = $"[{asset.Id}] {asset.Name}",
                    Text = asset.Id.ToString(),
                    IconAlignment = HorizontalAlignment.Center,
                    VerticalIconAlignment = VerticalAlignment.Top
                };

                var thumb = asset.GetThumbnail();
                if (thumb != null)
                {
                    var img = thumb.GetImage();
                    img?.Resize(THUMBNAIL_SIZE, THUMBNAIL_SIZE);
                    button.Icon = img != null ? ImageTexture.CreateFromImage(img) : null;
                }

                int capturedId = (int)asset.Id;
                button.Pressed += () =>
                {
                    onSelected?.Invoke(capturedId);
                    popup.Hide();
                    popup.QueueFree();
                };

                if (currentId == capturedId)
                {
                    var style = new StyleBoxFlat
                    {
                        BgColor = new Color(0.2f, 0.6f, 1.0f, 0.5f),
                        BorderWidthBottom = 2,
                        BorderWidthTop = 2,
                        BorderWidthLeft = 2,
                        BorderWidthRight = 2,
                        BorderColor = Colors.White
                    };
                    button.AddThemeStyleboxOverride("normal", style);
                }

                grid.AddChild(button);
            }

            scroll.AddChild(grid);
            vbox.AddChild(scroll);
            popup.AddChild(vbox);

            var editorBase = EditorInterface.Singleton.GetBaseControl();
            editorBase.AddChild(popup);
            popup.PopupCentered();
        }
    }
}