// /Editor/TerrainLayerInspector.cs
using System;
using System.Collections.Generic;
using Godot;
using Terrain3DTools.Core;
using Terrain3DTools.Layers;
using Terrain3DTools.Layers.Path;

namespace Terrain3DTools.Editor
{
    /// <summary>
    /// Base inspector for terrain layers that don't have specialized inspectors.
    /// Currently handles HeightLayer and any future layer types.
    /// 
    /// Specialized inspectors:
    /// - PathLayer -> PathLayerInspector
    /// - TextureLayer -> TextureLayerInspector
    /// </summary>
    [Tool, GlobalClass]
    public partial class TerrainLayerInspector : EditorInspectorPlugin
    {
        public override bool _CanHandle(GodotObject obj)
        {
            // PathLayer has its own dedicated inspector
            if (obj is PathLayer) return false;
            
            // TextureLayer has its own dedicated inspector
            if (obj is TextureLayer) return false;

            return obj is TerrainLayerBase;
        }

        public override bool _ParseProperty(GodotObject @object, Variant.Type type, string name, 
            PropertyHint hintType, string hintString, PropertyUsageFlags usageFlags, bool wide)
        {
            // Let all properties pass through to default inspector
            return false;
        }

        public override void _ParseBegin(GodotObject obj)
        {
            if (obj is not TerrainLayerBase layer) return;

            // Add mask preview UI for layers that benefit from it (e.g., HeightLayer)
            if (ShouldShowMaskPreview(layer))
            {
                AddMaskPreviewUI(layer);
            }
        }

        /// <summary>
        /// Determines if a layer should show the mask preview UI.
        /// </summary>
        private bool ShouldShowMaskPreview(TerrainLayerBase layer)
        {
            // Height layers benefit from mask preview since they don't have
            // obvious 3D feedback for stamps/noise
            return layer.GetLayerType() == LayerType.Height;
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