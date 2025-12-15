// /Core/Pipeline/FeatureLayerMaskPhase.cs
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
    /// Feature layers represent complex terrain features like paths, roads, or plateaus
    /// that can modify both height and texture data. Unlike simple height/texture layers,
    /// features may generate their own height geometry and influence patterns.
    /// Dependencies vary based on what the feature modifies (height, texture, or both).
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

                    DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.PhaseExecution,
                        $"Feature '{featureLayer.LayerName}' depends on {heightDependencies.Count} height composite task(s)");
                }

                if (featureLayer.ModifiesTexture)
                {
                    var textureDependencies = overlappingRegionCoords
                        .Where(rc => context.RegionTextureCompositeTasks.ContainsKey(rc))
                        .Select(rc => context.RegionTextureCompositeTasks[rc])
                        .ToList();
                    dependencies.AddRange(textureDependencies);

                    DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.PhaseExecution,
                        $"Feature '{featureLayer.LayerName}' depends on {textureDependencies.Count} texture composite task(s)");
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

                    DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.MaskGeneration,
                        $"Created mask task for feature '{featureLayer.LayerName}'");
                }
            }

            return tasks;
        }
    }
}