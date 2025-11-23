using System;
using Godot;
using Terrain3DTools.Core;
using Terrain3DTools.Layers;
using System.IO;

namespace Terrain3DTools.Editor
{
    [Tool, GlobalClass]
    public partial class TerrainLayerInspector : EditorInspectorPlugin
    {
        public override bool _CanHandle(GodotObject obj)
        {
            return obj is TerrainLayerBase;
        }

        public override void _ParseBegin(GodotObject obj)
        {
            if (obj is not TerrainLayerBase layer) return;

            var vbox = new VBoxContainer();

            var previewTex = new TextureRect
            {
                StretchMode = TextureRect.StretchModeEnum.Scale,
                CustomMinimumSize = new Vector2(0, 256f),
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
            };

            var previewButton = new Button { Text = "Generate Mask Preview" };
            previewButton.Pressed += () =>
            {
                // 1. Safety Checks and find the main manager
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

                // Give user feedback and prevent spamming the button
                previewButton.Disabled = true;
                previewButton.Text = "Generating...";

                // 2. Prepare the layer's GPU resources (ensures RIDs are valid and sized correctly)
                layer.PrepareMaskResources(false);
                if (!layer.layerTextureRID.IsValid)
                {
                    GD.PrintErr("Layer's texture RID is invalid after preparation. Aborting.");
                    previewButton.Disabled = false;
                    previewButton.Text = "Generate Mask Preview";
                    return;
                }

                // 3. Define the OnComplete callback. This runs on the main thread AFTER the GPU is done.
                Action onComplete = () =>
                {
                    // The user might have closed the inspector while the GPU was working.
                    if (!IsInstanceValid(previewTex)) return;

                    int maskWidth = layer.Size.X;
                    int maskHeight = layer.Size.Y;

                    // This is now safe. The stall is minimal (just data transfer time) because Sync() is done.
                    var data = Gpu.Rd.TextureGetData(layer.layerTextureRID, 0);
                    if (data == null || data.Length == 0) return;

                    var rfImage = Image.CreateFromData(maskWidth, maskHeight, false, Image.Format.Rf, data);

                    // Convert the single-channel float image to a displayable RGBA image.
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

                    // Re-enable the button for the next use.
                    previewButton.Disabled = false;
                    previewButton.Text = "Generate Mask Preview";
                };
                // 4. Create a one-shot GPU task using the same pipeline as the main system.
                AsyncGpuTask task = null;
                if (layer.GetLayerType() == LayerType.Feature)
                {
                    task = Pipeline.LayerMaskPipeline.CreateUpdateFeatureLayerTextureTask(
                        layer.layerTextureRID,
                        (FeatureLayer)layer,
                        layer.Size.X,
                        layer.Size.Y,
                        new System.Collections.Generic.List<AsyncGpuTask>(), // No dependencies
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
                        new Rid(), // No stitched heightmap needed for a simple preview
                        new Rid(),
                        0,
                        null, // No dependencies
                        onComplete
                    );
                }

                // 5. Submit the task to the manager.
                if (task != null)
                {
                    AsyncGpuTaskManager.Instance.AddTask(task);
                }
                else
                {
                    // Something went wrong, reset the button.
                    previewButton.Disabled = false;
                    previewButton.Text = "Generate Mask Preview";
                }
            };

            vbox.AddChild(previewButton);
            vbox.AddChild(previewTex);

            AddCustomControl(vbox);
        }
    }
}
