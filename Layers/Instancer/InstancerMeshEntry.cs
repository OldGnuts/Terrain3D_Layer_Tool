// /Layers/Instancer/InstancerMeshEntry.cs
using Godot;

namespace Terrain3DTools.Layers.Instancer
{
    /// <summary>
    /// Configuration for a single mesh type within an InstancerLayer.
    /// </summary>
    [GlobalClass, Tool]
    public partial class InstancerMeshEntry : Resource
    {
        private int _meshAssetId = -1;
        private float _probabilityWeight = 1.0f;
        private Vector2 _scaleRange = new(0.8f, 1.2f);
        private float _yRotationRange = 360f;
        private bool _alignToNormal = true;
        private float _normalAlignmentStrength = 1.0f;
        private float _heightOffset = 0f;
        
        [Export]
        public int MeshAssetId
        {
            get => _meshAssetId;
            set { _meshAssetId = value; EmitChanged(); }
        }
        
        [Export(PropertyHint.Range, "0.01,10,0.01")]
        public float ProbabilityWeight
        {
            get => _probabilityWeight;
            set { _probabilityWeight = Mathf.Max(0.01f, value); EmitChanged(); }
        }
        
        [Export]
        public Vector2 ScaleRange
        {
            get => _scaleRange;
            set { _scaleRange = new Vector2(Mathf.Max(0.01f, value.X), Mathf.Max(0.01f, value.Y)); EmitChanged(); }
        }
        
        [Export(PropertyHint.Range, "0,360,1")]
        public float YRotationRange
        {
            get => _yRotationRange;
            set { _yRotationRange = Mathf.Clamp(value, 0f, 360f); EmitChanged(); }
        }
        
        [Export]
        public bool AlignToNormal
        {
            get => _alignToNormal;
            set { _alignToNormal = value; EmitChanged(); }
        }
        
        [Export(PropertyHint.Range, "0,1,0.05")]
        public float NormalAlignmentStrength
        {
            get => _normalAlignmentStrength;
            set { _normalAlignmentStrength = Mathf.Clamp(value, 0f, 1f); EmitChanged(); }
        }
        
        [Export]
        public float HeightOffset
        {
            get => _heightOffset;
            set { _heightOffset = value; EmitChanged(); }
        }
    }
}