// /Layers/PathLayer.cs
using Godot;
using Terrain3DTools.Core;
namespace Terrain3DTools.Layers
{
    public partial class PathLayer : FeatureLayer
    {
        #region GPU Resources
        // Standard mask texture (influence)
        // Inherited: public Rid layerTextureRID;

        // Height data texture (path Y values)
        private Rid layerHeightDataRID;
        #endregion
        #region Private Fields
        private Path3D _path3D;
        private PathType _pathType = PathType.Path;
        private float _pathWidth = 4.0f;
        private float _embankmentWidth = 2.0f;
        private float _transitionWidth = 1.0f;

        // Height modification properties
        private bool _carveHeight = true;
        private float _carveStrength = 1.0f;
        private Curve _carveCurve;
        private float _pathElevation = 0.5f;
        private float _terrainConformance = 0.0f;

        // Embankment properties
        private bool _createEmbankments = false;
        private float _embankmentHeight = 1.0f;
        private Curve _embankmentCurve;
        private float _embankmentFalloff = 3.0f;

        // Texture properties
        private bool _applyTextures = true;
        private PathTextureMode _textureMode = PathTextureMode.CenterEmbankment;
        private int _centerTextureId = 0;
        private int _embankmentTextureId = 1;
        private int _transitionTextureId = 2;
        private float _textureBlendStrength = 1.0f;

        // Advanced properties
        private bool _smoothPath = true;
        private float _smoothingRadius = 1.0f;
        private int _pathResolution = 32;
        private bool _adaptiveResolution = true;
        private float _minCurveRadius = 2.0f;

        // River-specific
        private float _riverDepth = 2.0f;
        private float _riverBankWidth = 3.0f;
        private bool _riverFlowDirection = true;
        private float _riverFlowStrength = 1.0f;

        // Road-specific  
        private float _roadCamber = 0.1f;
        private bool _roadDrainage = false;
        private float _shoulderWidth = 1.0f;

        // Debug 
        private bool _debugMode = false;

        #endregion

        #region Exported Properties
        [ExportGroup("Path Setup")]
        [Export]
        public Path3D Path3D
        {
            get => _path3D;
            set => SetProperty(ref _path3D, value);
        }

        [Export]
        public PathType PathType
        {
            get => _pathType;
            set
            {
                if (_pathType != value)
                {
                    _pathType = value;
                    ApplyPathTypeDefaults();
                    SetProperty(ref _pathType, value);
                }
            }
        }

        [Export(PropertyHint.Range, "0.1,50.0,0.1")]
        public float PathWidth
        {
            get => _pathWidth;
            set => SetProperty(ref _pathWidth, value);
        }
        [Export(PropertyHint.Range, "-1.00,1.00,0.05")]
        public float PathHeightChange
        {
            get => _pathElevation;
            set => SetProperty(ref _pathElevation, value);
        }

        [ExportGroup("Height Modification")]
        [Export]
        public bool CarveHeight
        {
            get => _carveHeight;
            set
            {
                SetProperty(ref _carveHeight, value);
                ModifiesHeight = value || _createEmbankments;
            }
        }

        [Export(PropertyHint.Range, "0.01,1.0,0.01")]
        public float CarveStrength
        {
            get => _carveStrength;
            set => SetProperty(ref _carveStrength, value);
        }

        [Export]
        public Curve CarveCurve
        {
            get => _carveCurve;
            set
            {
                if (IsNodeReady() && IsInstanceValid(_carveCurve))
                    _carveCurve.Changed -= OnCurveChanged;
                _carveCurve = value;
                if (IsNodeReady() && IsInstanceValid(_carveCurve))
                    _carveCurve.Changed += OnCurveChanged;
                ForceDirty();
            }
        }

        [Export(PropertyHint.Range, "0.0,1.0,0.01")]
        public float TerrainConformance
        {
            get => _terrainConformance;
            set => SetProperty(ref _terrainConformance, value);
        }

        [ExportGroup("Embankments")]
        [Export]
        public bool CreateEmbankments
        {
            get => _createEmbankments;
            set
            {
                SetProperty(ref _createEmbankments, value);
                ModifiesHeight = _carveHeight || value;
            }
        }

        [Export(PropertyHint.Range, "0.1,20.0,0.1")]
        public float EmbankmentWidth
        {
            get => _embankmentWidth;
            set => SetProperty(ref _embankmentWidth, value);
        }

        [Export(PropertyHint.Range, "-10.0,10.0,0.1")]
        public float EmbankmentHeight
        {
            get => _embankmentHeight;
            set => SetProperty(ref _embankmentHeight, value);
        }

        [Export]
        public Curve EmbankmentCurve
        {
            get => _embankmentCurve;
            set
            {
                if (IsNodeReady() && IsInstanceValid(_embankmentCurve))
                    _embankmentCurve.Changed -= OnCurveChanged;
                _embankmentCurve = value;
                if (IsNodeReady() && IsInstanceValid(_embankmentCurve))
                    _embankmentCurve.Changed += OnCurveChanged;
                ForceDirty();
            }
        }

        [Export(PropertyHint.Range, "0.1,10.0,0.1")]
        public float EmbankmentFalloff
        {
            get => _embankmentFalloff;
            set => SetProperty(ref _embankmentFalloff, value);
        }

        [ExportGroup("Texturing")]
        [Export]
        public bool ApplyTextures
        {
            get => _applyTextures;
            set
            {
                SetProperty(ref _applyTextures, value);
                ModifiesTexture = value;
            }
        }

        [Export]
        public PathTextureMode TextureMode
        {
            get => _textureMode;
            set => SetProperty(ref _textureMode, value);
        }

        [Export(PropertyHint.Range, "0,31,1")]
        public int CenterTextureId
        {
            get => _centerTextureId;
            set => SetProperty(ref _centerTextureId, value);
        }

        [Export(PropertyHint.Range, "0,31,1")]
        public int EmbankmentTextureId
        {
            get => _embankmentTextureId;
            set => SetProperty(ref _embankmentTextureId, value);
        }

        [Export(PropertyHint.Range, "0,31,1")]
        public int TransitionTextureId
        {
            get => _transitionTextureId;
            set => SetProperty(ref _transitionTextureId, value);
        }

        [Export(PropertyHint.Range, "0.1,10.0,0.1")]
        public float TransitionWidth
        {
            get => _transitionWidth;
            set => SetProperty(ref _transitionWidth, value);
        }

        [Export(PropertyHint.Range, "0.0,2.0,0.01")]
        public float TextureBlendStrength
        {
            get => _textureBlendStrength;
            set => SetProperty(ref _textureBlendStrength, value);
        }

        [ExportGroup("Path Quality")]
        [Export]
        public bool SmoothPath
        {
            get => _smoothPath;
            set => SetProperty(ref _smoothPath, value);
        }

        [Export(PropertyHint.Range, "0.1,5.0,0.1")]
        public float SmoothingRadius
        {
            get => _smoothingRadius;
            set => SetProperty(ref _smoothingRadius, value);
        }

        [Export(PropertyHint.Range, "8,128,1")]
        public int PathResolution
        {
            get => _pathResolution;
            set => SetProperty(ref _pathResolution, value);
        }

        [Export]
        public bool AdaptiveResolution
        {
            get => _adaptiveResolution;
            set => SetProperty(ref _adaptiveResolution, value);
        }

        [Export(PropertyHint.Range, "0.1,20.0,0.1")]
        public float MinCurveRadius
        {
            get => _minCurveRadius;
            set => SetProperty(ref _minCurveRadius, value);
        }

        [ExportGroup("River Properties")]
        [Export(PropertyHint.Range, "0.0,20.0,0.1")]
        public float RiverDepth
        {
            get => _riverDepth;
            set => SetProperty(ref _riverDepth, value);
        }

        [Export(PropertyHint.Range, "0.1,20.0,0.1")]
        public float RiverBankWidth
        {
            get => _riverBankWidth;
            set => SetProperty(ref _riverBankWidth, value);
        }

        [Export]
        public bool RiverFlowDirection
        {
            get => _riverFlowDirection;
            set => SetProperty(ref _riverFlowDirection, value);
        }

        [Export(PropertyHint.Range, "0.0,2.0,0.01")]
        public float RiverFlowStrength
        {
            get => _riverFlowStrength;
            set => SetProperty(ref _riverFlowStrength, value);
        }

        [ExportGroup("Road Properties")]
        [Export(PropertyHint.Range, "0.0,1.0,0.01")]
        public float RoadCamber
        {
            get => _roadCamber;
            set => SetProperty(ref _roadCamber, value);
        }

        [Export]
        public bool RoadDrainage
        {
            get => _roadDrainage;
            set => SetProperty(ref _roadDrainage, value);
        }

        [Export(PropertyHint.Range, "0.0,10.0,0.1")]
        public float ShoulderWidth
        {
            get => _shoulderWidth;
            set => SetProperty(ref _shoulderWidth, value);
        }

        [ExportGroup("Debug")]
        [Export]
        public bool DebugMode
        {
            get => _debugMode;
            set => SetProperty(ref _debugMode, value);
        }

        #endregion
    }
}
