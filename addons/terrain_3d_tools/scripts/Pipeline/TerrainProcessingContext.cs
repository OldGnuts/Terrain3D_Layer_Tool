// /Core/Pipeline/TerrainProcessingContext.cs
using Godot;
using System.Collections.Generic;
using Terrain3DTools.Layers;
using Terrain3DTools.Core;

namespace Terrain3DTools.Pipeline
{
    /// <summary>
    /// Contains all context data needed for terrain processing phases.
    /// This is passed through the processing pipeline and accumulates results from each phase.
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

        // Shared resources
        public RegionMapManager RegionMapManager { get; set; }
        public RegionDependencyManager RegionDependencyManager { get; set; }
        public int RegionSize { get; set; }

        // Phase results - populated by each phase for use by subsequent phases
        public Dictionary<TerrainLayerBase, AsyncGpuTask> HeightLayerMaskTasks { get; set; }
        public Dictionary<Vector2I, AsyncGpuTask> RegionHeightCompositeTasks { get; set; }
        public Dictionary<TerrainLayerBase, AsyncGpuTask> TextureLayerMaskTasks { get; set; }
        public Dictionary<Vector2I, AsyncGpuTask> RegionTextureCompositeTasks { get; set; }
        public Dictionary<TerrainLayerBase, AsyncGpuTask> FeatureLayerMaskTasks { get; set; }

        public TerrainProcessingContext()
        {
            HeightLayerMaskTasks = new Dictionary<TerrainLayerBase, AsyncGpuTask>();
            RegionHeightCompositeTasks = new Dictionary<Vector2I, AsyncGpuTask>();
            TextureLayerMaskTasks = new Dictionary<TerrainLayerBase, AsyncGpuTask>();
            RegionTextureCompositeTasks = new Dictionary<Vector2I, AsyncGpuTask>();
            FeatureLayerMaskTasks = new Dictionary<TerrainLayerBase, AsyncGpuTask>();
        }
    }
}