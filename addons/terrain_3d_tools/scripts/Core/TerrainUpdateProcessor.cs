// /Core/TerrainUpdateProcessor.cs
using Godot;
using System.Collections.Generic;
using System.Linq;
using Terrain3DTools.Layers;
using Terrain3DTools.Pipeline;
using Terrain3DTools.Core.Debug;
using Terrain3DTools.Settings;

namespace Terrain3DTools.Core
{
    /// <summary>
    /// Orchestrates the multi-phase terrain update pipeline.
    /// Each phase is handled by a dedicated phase handler for better separation of concerns.
    /// </summary>
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
        private readonly TerrainLayerManager _layerManager;  // ADD THIS FIELD

        // Phase handlers
        private readonly HeightLayerMaskPhase _heightLayerMaskPhase;
        private readonly RegionHeightCompositePhase _regionHeightCompositePhase;
        private readonly TextureLayerMaskPhase _textureLayerMaskPhase;
        private readonly RegionTextureCompositePhase _regionTextureCompositePhase;
        private readonly FeatureLayerMaskPhase _featureLayerMaskPhase;
        private readonly FeatureLayerApplicationPhase _featureLayerApplicationPhase;
        private readonly SelectedLayerVisualizationPhase _selectedLayerVisualizationPhase;
        private readonly BlendGradientSmoothingPhase _blendGradientSmoothingPhase;
        private readonly ExclusionMapWritePhase _exclusionMapWritePhase;           // ADD
        private readonly InstancerPlacementPhase _instancerPlacementPhase;
        private readonly ManualEditApplicationPhase _manualEditApplicationPhase;       // ADD

        public TerrainUpdateProcessor(
            RegionMapManager regionMapManager,
            RegionDependencyManager regionDependencyManager,
            int regionSize,
            TerrainLayerManager layerManager)  // ADD PARAMETER
        {
            _regionMapManager = regionMapManager;
            _regionDependencyManager = regionDependencyManager;
            _regionSize = regionSize;
            _layerManager = layerManager;  // ADD THIS

            _heightLayerMaskPhase = new HeightLayerMaskPhase();
            _regionHeightCompositePhase = new RegionHeightCompositePhase();
            _textureLayerMaskPhase = new TextureLayerMaskPhase();
            _regionTextureCompositePhase = new RegionTextureCompositePhase();
            _featureLayerMaskPhase = new FeatureLayerMaskPhase();
            _featureLayerApplicationPhase = new FeatureLayerApplicationPhase();
            _selectedLayerVisualizationPhase = new SelectedLayerVisualizationPhase();
            _blendGradientSmoothingPhase = new BlendGradientSmoothingPhase();
            _exclusionMapWritePhase = new ExclusionMapWritePhase();               // ADD
            _instancerPlacementPhase = new InstancerPlacementPhase();
            _manualEditApplicationPhase = new ManualEditApplicationPhase();          // ADD

            DebugManager.Instance?.RegisterClass(DEBUG_CLASS_NAME);

            DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.Initialization,
                $"TerrainUpdateProcessor initialized with 11 phase handlers");
        }

        public void ProcessUpdatesAsync(
            HashSet<Vector2I> allDirtyRegions,
            IEnumerable<TerrainLayerBase> dirtyHeightLayers,
            IEnumerable<TerrainLayerBase> dirtyTextureLayers,
            IEnumerable<TerrainLayerBase> dirtyFeatureLayers,
            IReadOnlyCollection<Vector2I> currentlyActiveRegions,
            bool isInteractiveResize,
            TerrainLayerBase selectedLayer)
        {
            if (AsyncGpuTaskManager.Instance == null)
            {
                DebugManager.Instance?.LogError(DEBUG_CLASS_NAME,
                    "AsyncGpuTaskManager not available");
                return;
            }

            DebugManager.Instance?.StartTimer(DEBUG_CLASS_NAME, DebugCategory.PhaseExecution, "ProcessUpdatesAsync");

            var settings = GlobalToolSettingsManager.Current;

            // Build the processing context
            var context = new TerrainProcessingContext
            {
                AllDirtyRegions = allDirtyRegions,
                DirtyHeightLayers = dirtyHeightLayers.Where(l => GodotObject.IsInstanceValid(l)).ToList(),
                DirtyTextureLayers = dirtyTextureLayers.Where(l => GodotObject.IsInstanceValid(l)).ToList(),
                DirtyFeatureLayers = dirtyFeatureLayers.Where(l => GodotObject.IsInstanceValid(l)).ToList(),
                CurrentlyActiveRegions = currentlyActiveRegions,
                IsInteractiveResize = isInteractiveResize,
                WorldHeightScale = settings.WorldHeightScale,
                RegionMapManager = _regionMapManager,
                RegionDependencyManager = _regionDependencyManager,
                RegionSize = _regionSize,
                SelectedLayer = selectedLayer,
                LayerManager = _layerManager  // ADD THIS LINE
            };

            // Texture Gradient Blend Settings
            context.EnableBlendSmoothing = settings.EnableBlendSmoothing;
            context.SmoothingPasses = settings.SmoothingPasses;
            context.SmoothingStrength = settings.SmoothingStrength;
            context.MinBlendForSmoothing = settings.MinBlendForSmoothing;
            context.ConsiderSwappedPairs = settings.ConsiderSwappedPairs;
            context.IsolationThreshold = settings.IsolationThreshold;
            context.IsolationStrength = settings.IsolationStrength;
            context.IsolationBlendTarget = settings.IsolationBlendTarget;

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

            ExecutePipeline(context);

            DebugManager.Instance?.EndTimer(DEBUG_CLASS_NAME, DebugCategory.PhaseExecution, "ProcessUpdatesAsync");
        }

        private void ExecutePipeline(TerrainProcessingContext context)
        {
            DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.PhaseExecution,
                "Starting pipeline execution");

            // PHASE 1: Height Layer Masks
            if (context.DirtyHeightLayers.Count > 0)
            {
                DebugManager.Instance?.StartTimer(DEBUG_CLASS_NAME, DebugCategory.PhaseExecution, "Phase1_HeightMasks");
                _heightLayerMaskPhase.Execute(context);
                DebugManager.Instance?.EndTimer(DEBUG_CLASS_NAME, DebugCategory.PhaseExecution, "Phase1_HeightMasks");
                DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.PerformanceMetrics,
                    $"Phase 1 complete - {context.HeightLayerMaskTasks?.Count ?? 0} mask tasks created");
            }

            if (context.IsInteractiveResize)
            {
                DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.PhaseExecution,
                    "Early exit: interactive resize mode");
                return;
            }

            // PHASE 2: Region Height Composites
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

            // PHASE 6: Feature Layer Application (non-instancers only)
            if (context.AllDirtyRegions.Count > 0)
            {
                bool anyRegionHasFeatures = context.AllDirtyRegions.Any(region =>
                {
                    var tieredLayers = context.RegionDependencyManager.GetTieredLayersForRegion(region);
                    // Only count non-instancer features
                    return tieredLayers?.FeatureLayers
                        .Where(f => f is FeatureLayer fl && !fl.IsInstancer)
                        .Any() ?? false;
                });

                if (anyRegionHasFeatures)
                {
                    DebugManager.Instance?.StartTimer(DEBUG_CLASS_NAME, DebugCategory.PhaseExecution, "Phase6_FeatureApplication");
                    _featureLayerApplicationPhase.Execute(context);
                    DebugManager.Instance?.EndTimer(DEBUG_CLASS_NAME, DebugCategory.PhaseExecution, "Phase6_FeatureApplication");
                    DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.PerformanceMetrics,
                        $"Phase 6 complete - feature application tasks created");
                }
                else
                {
                    DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.PhaseExecution,
                        "Phase 6 skipped - no dirty regions have non-instancer feature layers");
                }
            }

            // PHASE 7: Exclusion Map Write
            if (context.AllDirtyRegions.Count > 0)
            {
                // Check if any region has instancers that need exclusion data
                bool anyRegionHasInstancers = context.AllDirtyRegions.Any(region =>
                {
                    var tieredLayers = context.RegionDependencyManager.GetTieredLayersForRegion(region);
                    return tieredLayers?.FeatureLayers
                        .OfType<InstancerLayer>()
                        .Any(l => GodotObject.IsInstanceValid(l)) ?? false;
                });

                if (anyRegionHasInstancers)
                {
                    DebugManager.Instance?.StartTimer(DEBUG_CLASS_NAME, DebugCategory.PhaseExecution, "Phase7_ExclusionMapWrite");
                    _exclusionMapWritePhase.Execute(context);
                    DebugManager.Instance?.EndTimer(DEBUG_CLASS_NAME, DebugCategory.PhaseExecution, "Phase7_ExclusionMapWrite");
                    DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.PerformanceMetrics,
                        $"Phase 7 complete - {context.ExclusionWriteTasks?.Count ?? 0} exclusion write tasks created");
                }
                else
                {
                    DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.PhaseExecution,
                        "Phase 7 skipped - no regions have instancer layers");
                }
            }

            // PHASE 8: Blend Gradient Smoothing
            if (context.AllDirtyRegions.Count > 0 && context.EnableBlendSmoothing)
            {
                DebugManager.Instance?.StartTimer(DEBUG_CLASS_NAME, DebugCategory.PhaseExecution, "Phase8_BlendSmoothing");
                _blendGradientSmoothingPhase.Execute(context);
                DebugManager.Instance?.EndTimer(DEBUG_CLASS_NAME, DebugCategory.PhaseExecution, "Phase8_BlendSmoothing");
                DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.PerformanceMetrics,
                    $"Phase 8 complete - {context.BlendSmoothingTasks?.Count ?? 0} smoothing tasks created");
            }

            // PHASE 9: Manual Edit Application 
            if (context.AllDirtyRegions.Count > 0)
            {
                DebugManager.Instance?.StartTimer(DEBUG_CLASS_NAME, DebugCategory.PhaseExecution, "Phase9_ManualEditApplication");
                _manualEditApplicationPhase.Execute(context);
                DebugManager.Instance?.EndTimer(DEBUG_CLASS_NAME, DebugCategory.PhaseExecution, "Phase9_ManualEditApplication");
                DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.PerformanceMetrics,
                    $"Phase 9 complete - {context.ManualEditApplicationTasks?.Count ?? 0} manual edit tasks created");
            }

            // PHASE 10: Instancer Placement
            var instancerLayers = context.DirtyFeatureLayers
                .OfType<InstancerLayer>()
                .Where(l => GodotObject.IsInstanceValid(l))
                .ToList();

            if (instancerLayers.Count > 0 && context.AllDirtyRegions.Count > 0)
            {
                DebugManager.Instance?.StartTimer(DEBUG_CLASS_NAME, DebugCategory.PhaseExecution, "Phase9_InstancerPlacement");
                _instancerPlacementPhase.Execute(context);
                DebugManager.Instance?.EndTimer(DEBUG_CLASS_NAME, DebugCategory.PhaseExecution, "Phase9_InstancerPlacement");
                DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.PerformanceMetrics,
                    $"Phase 10 complete - {context.InstancerPlacementTasks?.Count ?? 0} instancer layer(s) processed");
            }
            else
            {
                DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.PhaseExecution,
                    "Phase 10 skipped - no instancer layers or no dirty regions");
            }

            // PHASE 11: Selected Layer Visualization
            if (context.SelectedLayer != null)
            {
                DebugManager.Instance?.StartTimer(DEBUG_CLASS_NAME, DebugCategory.PhaseExecution, "Phase11_SelectedLayerVisualization");
                _selectedLayerVisualizationPhase.Execute(context);
                DebugManager.Instance?.EndTimer(DEBUG_CLASS_NAME, DebugCategory.PhaseExecution, "Phase11_SelectedLayerVisualization");
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
                (context.FeatureLayerMaskTasks?.Count ?? 0) +
                (context.RegionFeatureApplicationTasks?.Count ?? 0) +
                (context.ExclusionWriteTasks?.Count ?? 0) +
                (context.ManualEditApplicationTasks?.Count ?? 0) + 
                (context.BlendSmoothingTasks?.Count ?? 0) +
                (context.InstancerPlacementTasks?.Values.Sum(d => d.Count) ?? 0);

            DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.PerformanceMetrics,
                $"Pipeline complete - {totalTasks} total tasks dispatched to GPU");
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