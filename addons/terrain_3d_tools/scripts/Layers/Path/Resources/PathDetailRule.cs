// /Layers/Path/Resources/PathDetailRule.cs
using Godot;
using Godot.Collections;
using Terrain3DTools.Layers.Path;

namespace Terrain3DTools.Layers.Path
{
    /// <summary>
    /// Defines rules for placing details (foliage, rocks, signs, etc.) along a path.
    /// Foundation for future scatter/placement system.
    /// </summary>
    [GlobalClass, Tool]
    public partial class PathDetailRule : Resource
    {
        #region Private Fields
        private string _name = "Detail Rule";
        private bool _enabled = true;
        
        // What to place
        private PackedScene _scene;
        private Array<PackedScene> _sceneVariants = new();
        
        // Where to place
        private Array<ZoneType> _allowedZones = new();
        private float _minDistanceFromCenter = 0f;
        private float _maxDistanceFromCenter = 10f;
        
        // Spacing
        private float _spacing = 5.0f;
        private float _spacingVariation = 0.3f;
        private float _offset = 0f;
        private float _offsetVariation = 0.5f;
        
        // Alignment
        private DetailAlignment _alignment = DetailAlignment.PathDirection;
        private float _rotationVariation = 15f;
        private bool _alignToSlope = true;
        
        // Scale
        private Vector3 _baseScale = Vector3.One;
        private float _scaleVariation = 0.2f;
        
        // Filtering
        private float _minSlope = 0f;
        private float _maxSlope = 90f;
        private int _seed = 0;
        #endregion

        #region Exported Properties
        [ExportGroup("Identity")]
        
        [Export]
        public string Name
        {
            get => _name;
            set { _name = value; EmitChanged(); }
        }

        [Export]
        public bool Enabled
        {
            get => _enabled;
            set { _enabled = value; EmitChanged(); }
        }

        [ExportGroup("Objects")]
        
        [Export]
        public PackedScene Scene
        {
            get => _scene;
            set { _scene = value; EmitChanged(); }
        }

        [Export]
        public Array<PackedScene> SceneVariants
        {
            get => _sceneVariants;
            set { _sceneVariants = value ?? new Array<PackedScene>(); EmitChanged(); }
        }

        [ExportGroup("Placement Zone")]
        
        [Export]
        public Array<ZoneType> AllowedZones
        {
            get => _allowedZones;
            set { _allowedZones = value ?? new Array<ZoneType>(); EmitChanged(); }
        }

        [Export(PropertyHint.Range, "0,50,0.1")]
        public float MinDistanceFromCenter
        {
            get => _minDistanceFromCenter;
            set { _minDistanceFromCenter = Mathf.Max(0, value); EmitChanged(); }
        }

        [Export(PropertyHint.Range, "0,50,0.1")]
        public float MaxDistanceFromCenter
        {
            get => _maxDistanceFromCenter;
            set { _maxDistanceFromCenter = Mathf.Max(_minDistanceFromCenter, value); EmitChanged(); }
        }

        [ExportGroup("Spacing")]
        
        [Export(PropertyHint.Range, "0.1,100,0.1")]
        public float Spacing
        {
            get => _spacing;
            set { _spacing = Mathf.Max(0.1f, value); EmitChanged(); }
        }

        [Export(PropertyHint.Range, "0,1,0.01")]
        public float SpacingVariation
        {
            get => _spacingVariation;
            set { _spacingVariation = Mathf.Clamp(value, 0, 1); EmitChanged(); }
        }

        [Export(PropertyHint.Range, "-20,20,0.1")]
        public float Offset
        {
            get => _offset;
            set { _offset = value; EmitChanged(); }
        }

        [Export(PropertyHint.Range, "0,10,0.1")]
        public float OffsetVariation
        {
            get => _offsetVariation;
            set { _offsetVariation = Mathf.Max(0, value); EmitChanged(); }
        }

        [ExportGroup("Alignment")]
        
        [Export]
        public DetailAlignment Alignment
        {
            get => _alignment;
            set { _alignment = value; EmitChanged(); }
        }

        [Export(PropertyHint.Range, "0,180,1")]
        public float RotationVariation
        {
            get => _rotationVariation;
            set { _rotationVariation = Mathf.Clamp(value, 0, 180); EmitChanged(); }
        }

        [Export]
        public bool AlignToSlope
        {
            get => _alignToSlope;
            set { _alignToSlope = value; EmitChanged(); }
        }

        [ExportGroup("Scale")]
        
        [Export]
        public Vector3 BaseScale
        {
            get => _baseScale;
            set { _baseScale = value; EmitChanged(); }
        }

        [Export(PropertyHint.Range, "0,1,0.01")]
        public float ScaleVariation
        {
            get => _scaleVariation;
            set { _scaleVariation = Mathf.Clamp(value, 0, 1); EmitChanged(); }
        }

        [ExportGroup("Filtering")]
        
        [Export(PropertyHint.Range, "0,90,1")]
        public float MinSlope
        {
            get => _minSlope;
            set { _minSlope = Mathf.Clamp(value, 0, 90); EmitChanged(); }
        }

        [Export(PropertyHint.Range, "0,90,1")]
        public float MaxSlope
        {
            get => _maxSlope;
            set { _maxSlope = Mathf.Clamp(value, _minSlope, 90); EmitChanged(); }
        }

        [Export]
        public int Seed
        {
            get => _seed;
            set { _seed = value; EmitChanged(); }
        }
        #endregion

        #region Factory Methods
        public static PathDetailRule CreateFenceRule()
        {
            return new PathDetailRule
            {
                _name = "Fence Posts",
                _spacing = 3.0f,
                _spacingVariation = 0.1f,
                _offset = 0f,
                _alignment = DetailAlignment.PathPerpendicular,
                _allowedZones = new Array<ZoneType> { ZoneType.Shoulder, ZoneType.Edge }
            };
        }

        public static PathDetailRule CreateLampRule()
        {
            return new PathDetailRule
            {
                _name = "Street Lamps",
                _spacing = 15.0f,
                _spacingVariation = 0.05f,
                _offset = 0f,
                _alignment = DetailAlignment.WorldUp,
                _allowedZones = new Array<ZoneType> { ZoneType.Shoulder }
            };
        }

        public static PathDetailRule CreateRockRule()
        {
            return new PathDetailRule
            {
                _name = "Rocks",
                _spacing = 2.0f,
                _spacingVariation = 0.5f,
                _offsetVariation = 2.0f,
                _alignment = DetailAlignment.TerrainNormal,
                _rotationVariation = 180f,
                _scaleVariation = 0.4f,
                _allowedZones = new Array<ZoneType> { ZoneType.Shoulder, ZoneType.Edge, ZoneType.Wall }
            };
        }

        public static PathDetailRule CreateVegetationRule()
        {
            return new PathDetailRule
            {
                _name = "Vegetation",
                _spacing = 1.5f,
                _spacingVariation = 0.6f,
                _offsetVariation = 1.5f,
                _alignment = DetailAlignment.WorldUp,
                _rotationVariation = 180f,
                _scaleVariation = 0.3f,
                _allowedZones = new Array<ZoneType> { ZoneType.Edge, ZoneType.Transition }
            };
        }
        #endregion
    }
}