// /Layers/Instancer/InstancerBakeState.cs
using Godot;
using System.Collections.Generic;

namespace Terrain3DTools.Layers.Instancer
{
    /// <summary>
    /// Captures the state of an InstancerLayer at a specific moment for thread-safe GPU work.
    /// Similar to PathBakeState pattern.
    /// </summary>
    public class InstancerBakeState
    {
        public ulong LayerInstanceId { get; set; }
        public Rid DensityMaskRid { get; set; }
        public Vector2 MaskWorldMin { get; set; }
        public Vector2 MaskWorldMax { get; set; }
        public Vector2I Size { get; set; }
        
        // Placement parameters
        public float BaseDensity { get; set; }
        public float MinimumSpacing { get; set; }
        public int Seed { get; set; }
        public float ExclusionThreshold { get; set; }
        
        // Mesh configuration snapshot
        public List<MeshEntrySnapshot> MeshEntries { get; set; } = new();
        public float TotalProbabilityWeight { get; set; }
        
        public class MeshEntrySnapshot
        {
            public int MeshAssetId { get; set; }
            public float ProbabilityWeight { get; set; }
            public float MinScale { get; set; }
            public float MaxScale { get; set; }
            public float YRotationRangeRadians { get; set; }
            public bool AlignToNormal { get; set; }
            public float NormalAlignmentStrength { get; set; }
            public float HeightOffset { get; set; }
        }
    }
}