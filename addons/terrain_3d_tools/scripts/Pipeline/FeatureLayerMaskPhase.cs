using Godot;
using System.Collections.Generic;
using System.Linq;
using Terrain3DTools.Layers;
using Terrain3DTools.Core;
using Terrain3DTools.Utils;
using Terrain3DTools.Core.Debug;

namespace Terrain3DTools.Pipeline
{
    /// <summary>
    /// Phase 5: Generates mask textures for all dirty feature layers.
    /// Refactored to use Lazy (JIT) initialization.
    /// </summary>
    public class FeatureLayerMaskPhase : IProcessingPhase
    {
        private const string DEBUG_CLASS_NAME = "FeatureLayerMaskPhase";

        public FeatureLayerMaskPhase()
        {
            DebugManager.Instance?.RegisterClass(DEBUG_CLASS_NAME);
        }

        public Dictionary<object, AsyncGpuTask> Execute(TerrainProcessingContext context)
        {
            var tasks = new Dictionary<object, AsyncGpuTask>();
            DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.PhaseExecution,
                $"Processing {context.DirtyFeatureLayers.Count} feature layer(s)");

            foreach (var layer in context.DirtyFeatureLayers)
            {
                var featureLayer = layer as FeatureLayer;
                if (featureLayer == null || !GodotObject.IsInstanceValid(layer)) continue;

                featureLayer.SetWorldHeightScale(context.WorldHeightScale);

                // Build Dependencies
                var dependencies = new List<AsyncGpuTask>();
                var overlappingRegionCoords = TerrainCoordinateHelper
                    .GetRegionBoundsForLayer(layer, context.RegionSize)
                    .GetRegionCoords();

                if (featureLayer.ModifiesHeight)
                {
                    var heightDependencies = overlappingRegionCoords
                        .Where(rc => context.RegionHeightCompositeTasks.ContainsKey(rc))
                        .Select(rc => context.RegionHeightCompositeTasks[rc])
                        .ToList();
                    dependencies.AddRange(heightDependencies);
                }

                if (featureLayer.ModifiesTexture)
                {
                    var textureDependencies = overlappingRegionCoords
                        .Where(rc => context.RegionTextureCompositeTasks.ContainsKey(rc))
                        .Select(rc => context.RegionTextureCompositeTasks[rc])
                        .ToList();
                    dependencies.AddRange(textureDependencies);
                }

                System.Action onComplete = () =>
                {
                    if (GodotObject.IsInstanceValid(layer))
                        layer.Visualizer?.Update();
                };

                // Call the Pipeline (which returns a Lazy task)
                var maskTask = LayerMaskPipeline.CreateUpdateFeatureLayerTextureTask(
                    layer.layerTextureRID,
                    featureLayer,
                    layer.Size.X,
                    layer.Size.Y,
                    dependencies,
                    onComplete);

                if (maskTask != null)
                {
                    context.FeatureLayerMaskTasks[layer] = maskTask;
                    tasks[layer] = maskTask;
                    AsyncGpuTaskManager.Instance.AddTask(maskTask);
                    DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.PhaseExecution,
                        $"Feature layer mask task added to AsynceGpuTaskManager on Frame : {Engine.GetProcessFrames}");
                }
            }
            return tasks;
        }
    }
}