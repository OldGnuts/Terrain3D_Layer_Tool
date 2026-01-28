// /Pipeline/TerrainProcessingContext.cs

using System;
using System.Collections.Generic;
using Godot;
using Terrain3DTools.Core;
using Terrain3DTools.Layers;
using Terrain3DTools.Layers.Instancer;

namespace Terrain3DTools.Pipeline
{
    /// <summary>
    /// Context object passed through all processing phases.
    /// Contains input data, intermediate results, and output task collections.
    /// </summary>
    public class TerrainProcessingContext
    {
        // Input data
        public HashSet<Vector2I> AllDirtyRegions { get; set; }
        public List<TerrainLayerBase> DirtyHeightLayers { get; set; }
        public List<TerrainLayerBase> DirtyTextureLayers { get; set; }
        public List<TerrainLayerBase> DirtyFeatureLayers { get; set; }
        public IReadOnlyCollection<Vector2I> CurrentlyActiveRegions { get; set; }
        public bool IsInteractiveResize { get; set; }
        public float WorldHeightScale { get; set; }

        // Managers
        public RegionMapManager RegionMapManager { get; set; }
        public RegionDependencyManager RegionDependencyManager { get; set; }
        public TerrainLayerManager LayerManager { get; set; }
        public int RegionSize { get; set; }
        public TerrainLayerBase SelectedLayer { get; set; }

        // Texture Gradient Blend Settings
        public bool EnableBlendSmoothing { get; set; }
        public int SmoothingPasses { get; set; }
        public float SmoothingStrength { get; set; }
        public float MinBlendForSmoothing { get; set; }
        public bool ConsiderSwappedPairs { get; set; }
        public float IsolationThreshold { get; set; }
        public float IsolationStrength { get; set; }
        public float IsolationBlendTarget { get; set; }

        // Task collections for each phase
        public Dictionary<TerrainLayerBase, AsyncGpuTask> HeightLayerMaskTasks { get; set; } = new();
        public Dictionary<Vector2I, AsyncGpuTask> RegionHeightCompositeTasks { get; set; } = new();
        public Dictionary<TerrainLayerBase, AsyncGpuTask> TextureLayerMaskTasks { get; set; } = new();
        public Dictionary<Vector2I, AsyncGpuTask> RegionTextureCompositeTasks { get; set; } = new();
        public Dictionary<TerrainLayerBase, AsyncGpuTask> FeatureLayerMaskTasks { get; set; } = new();
        public Dictionary<Vector2I, AsyncGpuTask> ManualEditApplicationTasks { get; set; } = new();
        public Dictionary<Vector2I, AsyncGpuTask> RegionFeatureApplicationTasks { get; set; } = new();
        public Dictionary<Vector2I, AsyncGpuTask> BlendSmoothingTasks { get; set; } = new();

        // Exclusion map tasks (keyed by region coords)
        public Dictionary<Vector2I, AsyncGpuTask> ExclusionWriteTasks { get; set; } = new();

        // Instancer placement tasks (keyed by layer instance ID -> region coords -> task)
        public Dictionary<ulong, Dictionary<Vector2I, AsyncGpuTask>> InstancerPlacementTasks { get; set; } = new();

        /// <summary>
        /// Clears all task collections for a new processing cycle.
        /// </summary>
        public void ClearTasks()
        {
            HeightLayerMaskTasks.Clear();
            RegionHeightCompositeTasks.Clear();
            TextureLayerMaskTasks.Clear();
            RegionTextureCompositeTasks.Clear();
            FeatureLayerMaskTasks.Clear();
            RegionFeatureApplicationTasks.Clear();
            BlendSmoothingTasks.Clear();
            ExclusionWriteTasks.Clear();
            ManualEditApplicationTasks.Clear();  
            InstancerPlacementTasks.Clear();
        }
    }
}