// /Core/Pipeline/SelectedLayerVisualizationPhase.cs
using Godot;
using System.Collections.Generic;
using System.Linq;
using Terrain3DTools.Layers;
using Terrain3DTools.Utils;
using Terrain3DTools.Core;
using Terrain3DTools.Core.Debug;

namespace Terrain3DTools.Pipeline
{
    /// <summary>
    /// Phase 7: Updates visualization for the currently selected layer.
    /// This runs after all processing (including features) is complete,
    /// ensuring the visualization shows the accurate, fully-composited terrain shape.
    /// Only processes height layers as they require terrain geometry visualization.
    /// </summary>
    public class SelectedLayerVisualizationPhase : IProcessingPhase
    {
        private const string DEBUG_CLASS_NAME = "SelectedLayerVisualizationPhase";

        public SelectedLayerVisualizationPhase()
        {
            DebugManager.Instance?.RegisterClass(DEBUG_CLASS_NAME);
        }

        public Dictionary<object, AsyncGpuTask> Execute(TerrainProcessingContext context)
        {
            var tasks = new Dictionary<object, AsyncGpuTask>();

            var selectedLayer = context.SelectedLayer;
            
            if (selectedLayer == null || !GodotObject.IsInstanceValid(selectedLayer))
            {
                DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.PhaseExecution,
                    "No selected layer - skipping visualization update");
                return tasks;
            }

            if (selectedLayer.GetLayerType() != LayerType.Height)
            {
                DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.PhaseExecution,
                    $"Selected layer '{selectedLayer.LayerName}' is not a height layer - skipping");
                return tasks;
            }

            DebugManager.Instance?.StartTimer(DEBUG_CLASS_NAME, DebugCategory.PhaseExecution,
                $"UpdateVisualization:{selectedLayer.LayerName}");

            var overlappingRegions = TerrainCoordinateHelper
                .GetRegionBoundsForLayer(selectedLayer, context.RegionSize)
                .GetRegionCoords()
                .Where(coord => context.CurrentlyActiveRegions.Contains(coord))
                .ToList();

            if (overlappingRegions.Count == 0)
            {
                DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.PhaseExecution,
                    $"Layer '{selectedLayer.LayerName}' has no active overlapping regions");
                DebugManager.Instance?.EndTimer(DEBUG_CLASS_NAME, DebugCategory.PhaseExecution,
                    $"UpdateVisualization:{selectedLayer.LayerName}");
                return tasks;
            }

            var dependencies = new List<AsyncGpuTask>();

            foreach (var coord in overlappingRegions)
            {
                if (context.RegionHeightCompositeTasks.TryGetValue(coord, out var heightTask))
                {
                    dependencies.Add(heightTask);
                }
            }

            if (context.RegionFeatureCompositeTasks != null)
            {
                foreach (var coord in overlappingRegions)
                {
                    if (context.RegionFeatureCompositeTasks.TryGetValue(coord, out var featureTask))
                    {
                        dependencies.Add(featureTask);
                    }
                }
            }

            DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.PhaseExecution,
                $"Layer '{selectedLayer.LayerName}' - Creating visualization from {overlappingRegions.Count} regions with {dependencies.Count} dependencies");

            var task = GpuKernels.CreateVisualizationTask(
                selectedLayer,
                context.RegionMapManager,
                context.RegionSize,
                dependencies,
                DEBUG_CLASS_NAME);

            if (task != null)
            {
                tasks[selectedLayer] = task;
                AsyncGpuTaskManager.Instance.AddTask(task);

                DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.PerformanceMetrics,
                    $"Layer '{selectedLayer.LayerName}' - Visualization task created");
            }
            else
            {
                DebugManager.Instance?.LogWarning(DEBUG_CLASS_NAME,
                    $"Failed to create visualization task for layer '{selectedLayer.LayerName}'");
            }

            DebugManager.Instance?.EndTimer(DEBUG_CLASS_NAME, DebugCategory.PhaseExecution,
                $"UpdateVisualization:{selectedLayer.LayerName}");

            return tasks;
        }
    }
}