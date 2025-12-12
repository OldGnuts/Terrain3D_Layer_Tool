// /Core/Pipeline/HeightLayerMaskPhase.cs
using Godot;
using System.Collections.Generic;
using Terrain3DTools.Core;
using Terrain3DTools.Core.Debug;

namespace Terrain3DTools.Pipeline
{
    /// <summary>
    /// Phase 1: Generates mask textures for all dirty height layers.
    /// Height layers define terrain elevation changes through additive, subtractive,
    /// multiplicative, or overwrite operations. This phase produces the layer's
    /// influence texture without any terrain context (pure mask generation).
    /// </summary>
    public class HeightLayerMaskPhase : IProcessingPhase
    {
        private const string DEBUG_CLASS_NAME = "HeightLayerMaskPhase";

        public HeightLayerMaskPhase()
        {
            DebugManager.Instance?.RegisterClass(DEBUG_CLASS_NAME);
        }

        public Dictionary<object, AsyncGpuTask> Execute(TerrainProcessingContext context)
        {
            var tasks = new Dictionary<object, AsyncGpuTask>();

            DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.PhaseExecution,
                $"Processing {context.DirtyHeightLayers.Count} height layer(s)");

            foreach (var layer in context.DirtyHeightLayers)
            {
                if (!GodotObject.IsInstanceValid(layer)) continue;

                var task = LayerMaskPipeline.CreateUpdateLayerTextureTask(
                    layer.layerTextureRID,
                    layer,
                    layer.Size.X,
                    layer.Size.Y,
                    new Rid(),
                    new Rid(),
                    0,
                    new List<AsyncGpuTask>(),
                    null);

                if (task != null)
                {
                    context.HeightLayerMaskTasks[layer] = task;
                    tasks[layer] = task;
                    AsyncGpuTaskManager.Instance.AddTask(task);

                    DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.MaskGeneration,
                        $"Layer '{layer.LayerName}' - RID valid: {layer.layerTextureRID.IsValid}, Size: {layer.Size}");
                }
            }

            return tasks;
        }
    }
}