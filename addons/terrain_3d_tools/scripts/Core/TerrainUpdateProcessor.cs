// /Core/TerrainUpdateProcessor.cs
using Godot;
using System.Collections.Generic;
using System.Linq;
using Terrain3DTools.Layers;
using Terrain3DTools.Pipeline;
using Terrain3DTools.Core.Debug;

namespace Terrain3DTools.Core
{
    /// <summary>
    /// Orchestrates the multi-phase terrain update pipeline.
    /// Each phase is handled by a dedicated phase handler for better separation of concerns.
    /// </summary>
    public class TerrainUpdateProcessor
    {
        private const string DEBUG_CLASS_NAME = "TerrainUpdateProcessor";

        private readonly RegionMapManager _regionMapManager;
        private readonly RegionDependencyManager _regionDependencyManager;
        private readonly int _regionSize;


        private readonly HeightLayerMaskPhase _heightLayerMaskPhase;
        private readonly RegionHeightCompositePhase _regionHeightCompositePhase;
        private readonly TextureLayerMaskPhase _textureLayerMaskPhase;
        private readonly RegionTextureCompositePhase _regionTextureCompositePhase;
        private readonly FeatureLayerMaskPhase _featureLayerMaskPhase;
        private readonly FeatureLayerApplicationPhase _featureLayerApplicationPhase;
        private readonly SelectedLayerVisualizationPhase _selectedLayerVisualizationPhase;

        public TerrainUpdateProcessor(
            RegionMapManager regionMapManager,
            RegionDependencyManager regionDependencyManager,
            int regionSize)
        {
            _regionMapManager = regionMapManager;
            _regionDependencyManager = regionDependencyManager;
            _regionSize = regionSize;

            _heightLayerMaskPhase = new HeightLayerMaskPhase();
            _regionHeightCompositePhase = new RegionHeightCompositePhase();
            _textureLayerMaskPhase = new TextureLayerMaskPhase();
            _regionTextureCompositePhase = new RegionTextureCompositePhase();
            _featureLayerMaskPhase = new FeatureLayerMaskPhase();
            _featureLayerApplicationPhase = new FeatureLayerApplicationPhase();
            _selectedLayerVisualizationPhase = new SelectedLayerVisualizationPhase();

            DebugManager.Instance?.RegisterClass(DEBUG_CLASS_NAME);

            DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.Initialization,
                $"TerrainUpdateProcessor initialized with 7 phase handlers");
        }


        /// <summary>
        /// Processes terrain updates through a multi-phase async pipeline.
        /// Each phase builds on the results of previous phases through dependency chains.
        /// </summary>
        public void ProcessUpdatesAsync(
            HashSet<Vector2I> allDirtyRegions,
            IEnumerable<TerrainLayerBase> dirtyHeightLayers,
            IEnumerable<TerrainLayerBase> dirtyTextureLayers,
            IEnumerable<TerrainLayerBase> dirtyFeatureLayers,
            IReadOnlyCollection<Vector2I> currentlyActiveRegions,
            bool isInteractiveResize,
            float worldHeightScale,
            TerrainLayerBase selectedLayer)
        {
            if (AsyncGpuTaskManager.Instance == null)
            {
                DebugManager.Instance?.LogError(DEBUG_CLASS_NAME,
                    "AsyncGpuTaskManager not available");
                return;
            }

            DebugManager.Instance?.StartTimer(DEBUG_CLASS_NAME, DebugCategory.PhaseExecution, "ProcessUpdatesAsync");

            // Build the processing context
            var context = new TerrainProcessingContext
            {
                AllDirtyRegions = allDirtyRegions,
                DirtyHeightLayers = dirtyHeightLayers.Where(l => GodotObject.IsInstanceValid(l)).ToList(),
                DirtyTextureLayers = dirtyTextureLayers.Where(l => GodotObject.IsInstanceValid(l)).ToList(),
                DirtyFeatureLayers = dirtyFeatureLayers.Where(l => GodotObject.IsInstanceValid(l)).ToList(),
                CurrentlyActiveRegions = currentlyActiveRegions,
                IsInteractiveResize = isInteractiveResize,
                WorldHeightScale = worldHeightScale,
                RegionMapManager = _regionMapManager,
                RegionDependencyManager = _regionDependencyManager,
                RegionSize = _regionSize,
                SelectedLayer = selectedLayer
            };

            DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.PerformanceMetrics,
                $"Pipeline input - Regions: {allDirtyRegions.Count}, " +
                $"Height: {context.DirtyHeightLayers.Count}, " +
                $"Texture: {context.DirtyTextureLayers.Count}, " +
                $"Feature: {context.DirtyFeatureLayers.Count}");

            if (isInteractiveResize)
            {
                DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.PhaseExecution,
                    "Interactive resize mode - limiting pipeline execution");
            }

            // Execute the pipeline phases in order
            ExecutePipeline(context);

            DebugManager.Instance?.EndTimer(DEBUG_CLASS_NAME, DebugCategory.PhaseExecution, "ProcessUpdatesAsync");
        }

        /// <summary>
        /// Executes all phases of the terrain processing pipeline.
        /// Phases are executed sequentially, but tasks within each phase may run in parallel.
        /// </summary>
        private void ExecutePipeline(TerrainProcessingContext context)
        {
            DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.PhaseExecution,
                "Starting pipeline execution");

            // PHASE 1: Height Layer Masks
            // Generate mask textures for all dirty height layers
            if (context.DirtyHeightLayers.Count > 0)
            {
                DebugManager.Instance?.StartTimer(DEBUG_CLASS_NAME, DebugCategory.PhaseExecution, "Phase1_HeightMasks");

                _heightLayerMaskPhase.Execute(context);

                DebugManager.Instance?.EndTimer(DEBUG_CLASS_NAME, DebugCategory.PhaseExecution, "Phase1_HeightMasks");
                DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.PerformanceMetrics,
                    $"Phase 1 complete - {context.HeightLayerMaskTasks?.Count ?? 0} mask tasks created");
            }

            // Early exit during interactive resize to keep things responsive
            if (context.IsInteractiveResize)
            {
                DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.PhaseExecution,
                    "Early exit: interactive resize mode");
                return;
            }

            // PHASE 2: Region Height Composites
            // Run whenever there are dirty regions, not just when layers are dirty
            // Position-only changes still need regions to recomposite with existing masks
            if (context.AllDirtyRegions.Count > 0)
            {
                DebugManager.Instance?.StartTimer(DEBUG_CLASS_NAME, DebugCategory.PhaseExecution, "Phase2_HeightComposite");

                _regionHeightCompositePhase.Execute(context);

                DebugManager.Instance?.EndTimer(DEBUG_CLASS_NAME, DebugCategory.PhaseExecution, "Phase2_HeightComposite");
                DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.PerformanceMetrics,
                    $"Phase 2 complete - {context.RegionHeightCompositeTasks?.Count ?? 0} composite tasks created");
            }

            // PHASE 3: Texture Layer Masks
            if (context.DirtyTextureLayers.Count > 0)
            {
                DebugManager.Instance?.StartTimer(DEBUG_CLASS_NAME, DebugCategory.PhaseExecution, "Phase3_TextureMasks");

                _textureLayerMaskPhase.Execute(context);

                DebugManager.Instance?.EndTimer(DEBUG_CLASS_NAME, DebugCategory.PhaseExecution, "Phase3_TextureMasks");
                DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.PerformanceMetrics,
                    $"Phase 3 complete - {context.TextureLayerMaskTasks?.Count ?? 0} mask tasks created");
            }

            // PHASE 4: Region Texture Composites
            // FIXED: Same logic - run when regions are dirty
            if (context.AllDirtyRegions.Count > 0)
            {
                DebugManager.Instance?.StartTimer(DEBUG_CLASS_NAME, DebugCategory.PhaseExecution, "Phase4_TextureComposite");

                _regionTextureCompositePhase.Execute(context);

                DebugManager.Instance?.EndTimer(DEBUG_CLASS_NAME, DebugCategory.PhaseExecution, "Phase4_TextureComposite");
                DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.PerformanceMetrics,
                    $"Phase 4 complete - {context.RegionTextureCompositeTasks?.Count ?? 0} composite tasks created");
            }

            // PHASE 5: Feature Layer Masks
            if (context.DirtyFeatureLayers.Count > 0)
            {
                DebugManager.Instance?.StartTimer(DEBUG_CLASS_NAME, DebugCategory.PhaseExecution, "Phase5_FeatureMasks");

                _featureLayerMaskPhase.Execute(context);

                DebugManager.Instance?.EndTimer(DEBUG_CLASS_NAME, DebugCategory.PhaseExecution, "Phase5_FeatureMasks");
                DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.PerformanceMetrics,
                    $"Phase 5 complete - {context.FeatureLayerMaskTasks?.Count ?? 0} mask tasks created");
            }

            // PHASE 6: Feature Layer Application
            if (context.AllDirtyRegions.Count > 0 && context.DirtyFeatureLayers.Count > 0)
            {
                DebugManager.Instance?.StartTimer(DEBUG_CLASS_NAME, DebugCategory.PhaseExecution, "Phase6_FeatureApplication");

                _featureLayerApplicationPhase.Execute(context);

                DebugManager.Instance?.EndTimer(DEBUG_CLASS_NAME, DebugCategory.PhaseExecution, "Phase6_FeatureApplication");
                DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.PerformanceMetrics,
                    $"Phase 6 complete - feature application tasks created");
            }

            // PHASE 7: Update Selected Layer Visualization
            if (context.SelectedLayer != null)
            {
                DebugManager.Instance?.StartTimer(DEBUG_CLASS_NAME, DebugCategory.PhaseExecution,
                    "Phase7_SelectedLayerVisualization");

                _selectedLayerVisualizationPhase.Execute(context);

                DebugManager.Instance?.EndTimer(DEBUG_CLASS_NAME, DebugCategory.PhaseExecution,
                    "Phase7_SelectedLayerVisualization");
            }
            else
            {
                DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.PhaseExecution, "Selected layer is null");
            }

            // Log final task counts
            int totalTasks =
                (context.HeightLayerMaskTasks?.Count ?? 0) +
                (context.RegionHeightCompositeTasks?.Count ?? 0) +
                (context.TextureLayerMaskTasks?.Count ?? 0) +
                (context.RegionTextureCompositeTasks?.Count ?? 0) +
                (context.FeatureLayerMaskTasks?.Count ?? 0);

            DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.PerformanceMetrics,
                $"Pipeline complete - {totalTasks} total tasks dispatched to GPU");
        }

        /// <summary>
        /// Gets statistics about the current processing context.
        /// Useful for debugging and monitoring.
        /// </summary>
        public ProcessingStats GetStats(TerrainProcessingContext context)
        {
            var stats = new ProcessingStats
            {
                DirtyRegionCount = context.AllDirtyRegions?.Count ?? 0,
                HeightLayerCount = context.DirtyHeightLayers?.Count ?? 0,
                TextureLayerCount = context.DirtyTextureLayers?.Count ?? 0,
                FeatureLayerCount = context.DirtyFeatureLayers?.Count ?? 0,
                HeightMaskTaskCount = context.HeightLayerMaskTasks?.Count ?? 0,
                HeightCompositeTaskCount = context.RegionHeightCompositeTasks?.Count ?? 0,
                TextureMaskTaskCount = context.TextureLayerMaskTasks?.Count ?? 0,
                TextureCompositeTaskCount = context.RegionTextureCompositeTasks?.Count ?? 0,
                FeatureMaskTaskCount = context.FeatureLayerMaskTasks?.Count ?? 0
            };

            DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.PerformanceMetrics,
                stats.ToString());

            return stats;
        }
    }

    /// <summary>
    /// Statistics about terrain processing for monitoring and debugging.
    /// </summary>
    public struct ProcessingStats
    {
        public int DirtyRegionCount;
        public int HeightLayerCount;
        public int TextureLayerCount;
        public int FeatureLayerCount;
        public int HeightMaskTaskCount;
        public int HeightCompositeTaskCount;
        public int TextureMaskTaskCount;
        public int TextureCompositeTaskCount;
        public int FeatureMaskTaskCount;

        public override string ToString()
        {
            return $"Processing Stats:\n" +
                   $"  Regions: {DirtyRegionCount}\n" +
                   $"  Layers: H={HeightLayerCount}, T={TextureLayerCount}, F={FeatureLayerCount}\n" +
                   $"  Tasks: HM={HeightMaskTaskCount}, HC={HeightCompositeTaskCount}, " +
                   $"TM={TextureMaskTaskCount}, TC={TextureCompositeTaskCount}, FM={FeatureMaskTaskCount}";
        }
    }
}