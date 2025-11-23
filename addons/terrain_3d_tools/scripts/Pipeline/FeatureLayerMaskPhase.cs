using Godot;
using System.Collections.Generic;
using System.Linq;
using Terrain3DTools.Layers;
using Terrain3DTools.Core;
using Terrain3DTools.Utils;

namespace Terrain3DTools.Pipeline
{
    /// <summary>
    /// Phase 5: Generates mask textures for all dirty feature layers.
    /// Feature layers may depend on both height and texture data.
    /// Depends on: RegionHeightCompositePhase, RegionTextureCompositePhase (conditionally)
    /// </summary>
    public class FeatureLayerMaskPhase : IProcessingPhase
    {
        private bool DEBUG = false;

        public Dictionary<object, AsyncGpuTask> Execute(TerrainProcessingContext context)
        {
            var tasks = new Dictionary<object, AsyncGpuTask>();

            foreach (var layer in context.DirtyFeatureLayers)
            {
                var featureLayer = layer as FeatureLayer;
                if (featureLayer == null || !GodotObject.IsInstanceValid(layer)) continue;

                featureLayer.SetWorldHeightScale(context.WorldHeightScale);

                var dependencies = new List<AsyncGpuTask>();
                var overlappingRegionCoords = TerrainCoordinateHelper
                    .GetRegionBoundsForLayer(layer, context.RegionSize)
                    .GetRegionCoords();

                // If the feature layer modifies height, it needs height composite data
                if (featureLayer.ModifiesHeight)
                {
                    var heightDependencies = overlappingRegionCoords
                        .Where(rc => context.RegionHeightCompositeTasks.ContainsKey(rc))
                        .Select(rc => context.RegionHeightCompositeTasks[rc])
                        .ToList();
                    dependencies.AddRange(heightDependencies);
                }

                // If the feature layer modifies texture, it needs texture composite data
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
                    {
                        layer.Visualizer.Update();
                    }
                };

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

                    if (DEBUG) 
                        GD.Print($"[FeatureLayerMaskPhase] Created mask task for {featureLayer.LayerName}");
                }
            }

            return tasks;
        }
    }
}