// /Layers/Path/Resources/ProfileZone.cs
using Godot;
using Terrain3DTools.Utils;

namespace Terrain3DTools.Layers.Path
{
    /// <summary>
    /// Defines a single zone within a path's cross-section profile.
    /// Zones are ordered from center outward. Each zone has independent
    /// settings for width, height modification, texture, and noise.
    /// </summary>
    [GlobalClass, Tool]
    public partial class ProfileZone : Resource
    {
        #region Private Fields
        private string _name = "Zone";
        private ZoneType _type = ZoneType.Center;
        private float _width = 2.0f;

        // Height settings
        private float _heightOffset = 0.0f;
        private Curve _heightCurve;
        private HeightBlendMode _heightBlendMode = HeightBlendMode.Replace;
        private float _heightStrength = 1.0f;

        // Texture settings
        private int _textureId = -1; // -1 = no texture change
        private TextureBlendMode _textureBlendMode = TextureBlendMode.Replace;
        private float _textureStrength = 1.0f;

        // Noise (independent for height and texture)
        private NoiseConfig _heightNoise;
        private NoiseConfig _textureNoise;

        // Advanced
        private float _terrainConformance = 0.0f;
        private bool _enabled = true;

        // Subscription tracking
        private bool _heightCurveSubscribed = false;
        private bool _heightNoiseSubscribed = false;
        private bool _textureNoiseSubscribed = false;

        #endregion

        #region Exported Properties
        [ExportGroup("Zone Identity")]

        [Export]
        public string Name
        {
            get => _name;
            set { _name = value; EmitChanged(); }
        }

        [Export]
        public ZoneType Type
        {
            get => _type;
            set { _type = value; EmitChanged(); }
        }

        [Export]
        public bool Enabled
        {
            get => _enabled;
            set { _enabled = value; EmitChanged(); }
        }

        [ExportGroup("Dimensions")]

        [Export(PropertyHint.Range, "0.1,50.0,0.1")]
        public float Width
        {
            get => _width;
            set { _width = Mathf.Max(0.1f, value); EmitChanged(); }
        }

        [ExportGroup("Height Modification")]

        [Export(PropertyHint.Range, "-20.0,20.0,0.1")]
        public float HeightOffset
        {
            get => _heightOffset;
            set { _heightOffset = value; EmitChanged(); }
        }

        /// <summary>
        /// Curve defining height profile across this zone (0=inner edge, 1=outer edge).
        /// Y values multiply the HeightOffset.
        /// </summary>
        [Export]
        public Curve HeightCurve
        {
            get => _heightCurve;
            set
            {
                if (_heightCurveSubscribed && _heightCurve != null)
                {
                    _heightCurve.Changed -= OnCurveChanged;
                    _heightCurveSubscribed = false;
                }

                _heightCurve = value;

                if (_heightCurve != null)
                {
                    _heightCurve.Changed += OnCurveChanged;
                    _heightCurveSubscribed = true;
                }

                EmitChanged();
            }
        }

        [Export]
        public HeightBlendMode HeightBlendMode
        {
            get => _heightBlendMode;
            set { _heightBlendMode = value; EmitChanged(); }
        }

        [Export(PropertyHint.Range, "0.0,1.0,0.01")]
        public float HeightStrength
        {
            get => _heightStrength;
            set { _heightStrength = Mathf.Clamp(value, 0f, 1f); EmitChanged(); }
        }

        [Export(PropertyHint.Range, "0.0,1.0,0.01")]
        public float TerrainConformance
        {
            get => _terrainConformance;
            set { _terrainConformance = Mathf.Clamp(value, 0f, 1f); EmitChanged(); }
        }

        [ExportGroup("Height Noise")]

        [Export]
        public NoiseConfig HeightNoise
        {
            get => _heightNoise;
            set
            {
                if (_heightNoiseSubscribed && _heightNoise != null)
                {
                    _heightNoise.Changed -= OnNoiseChanged;
                    _heightNoiseSubscribed = false;
                }

                _heightNoise = value;

                if (_heightNoise != null)
                {
                    _heightNoise.Changed += OnNoiseChanged;
                    _heightNoiseSubscribed = true;
                }

                EmitChanged();
            }
        }

        [ExportGroup("Texture")]

        /// <summary>
        /// Terrain3D texture index (0-31). Set to -1 to not modify texture.
        /// </summary>
        [Export(PropertyHint.Range, "-1,31,1")]
        public int TextureId
        {
            get => _textureId;
            set { _textureId = Mathf.Clamp(value, -1, 31); EmitChanged(); }
        }

        [Export]
        public TextureBlendMode TextureBlendMode
        {
            get => _textureBlendMode;
            set { _textureBlendMode = value; EmitChanged(); }
        }

        [Export(PropertyHint.Range, "0.0,1.0,0.01")]
        public float TextureStrength
        {
            get => _textureStrength;
            set { _textureStrength = Mathf.Clamp(value, 0f, 1f); EmitChanged(); }
        }

        [ExportGroup("Texture Noise")]

        [Export]
        public NoiseConfig TextureNoise
        {
            get => _textureNoise;
            set
            {
                if (_textureNoiseSubscribed && _textureNoise != null)
                {
                    _textureNoise.Changed -= OnNoiseChanged;
                    _textureNoiseSubscribed = false;
                }

                _textureNoise = value;

                if (_textureNoise != null)
                {
                    _textureNoise.Changed += OnNoiseChanged;
                    _textureNoiseSubscribed = true;
                }

                EmitChanged();
            }
        }
        #endregion

        #region Lifecycle
        public ProfileZone()
        {
            // Don't subscribe in constructor - let the setters handle it
            _heightNoise = NoiseConfig.CreateDefault();
            _textureNoise = NoiseConfig.CreateDefault();
            _heightCurve = CurveUtils.CreateLinearCurve();

            // Now subscribe since we just created them
            if (_heightNoise != null)
            {
                _heightNoise.Changed += OnNoiseChanged;
                _heightNoiseSubscribed = true;
            }
            if (_textureNoise != null)
            {
                _textureNoise.Changed += OnNoiseChanged;
                _textureNoiseSubscribed = true;
            }
            if (_heightCurve != null)
            {
                _heightCurve.Changed += OnCurveChanged;
                _heightCurveSubscribed = true;
            }
        }

        private void OnCurveChanged() => EmitChanged();
        private void OnNoiseChanged() => EmitChanged();
        #endregion

        #region Factory Methods
        public static ProfileZone CreateCenter(float width, float heightOffset, int textureId)
        {
            return new ProfileZone
            {
                _name = "Center",
                _type = ZoneType.Center,
                _width = width,
                _heightOffset = heightOffset,
                _textureId = textureId,
                _heightBlendMode = HeightBlendMode.Replace,
                _heightCurve = CurveUtils.CreateFlatCurve()
            };
        }

        public static ProfileZone CreateShoulder(float width, float heightOffset, int textureId)
        {
            return new ProfileZone
            {
                _name = "Shoulder",
                _type = ZoneType.Shoulder,
                _width = width,
                _heightOffset = heightOffset,
                _textureId = textureId,
                _heightBlendMode = HeightBlendMode.Blend,
                _heightCurve = CurveUtils.CreateLinearCurve()
            };
        }

        public static ProfileZone CreateEdge(float width)
        {
            return new ProfileZone
            {
                _name = "Edge",
                _type = ZoneType.Edge,
                _width = width,
                _heightOffset = 0.0f,
                _textureId = -1,
                _heightBlendMode = HeightBlendMode.Blend,
                _heightStrength = 0.5f,
                _terrainConformance = 1.0f,
                _heightCurve = CurveUtils.CreateEaseOutCurve()
            };
        }

        public static ProfileZone CreateWall(float width, float depth)
        {
            var zone = new ProfileZone
            {
                _name = "Wall",
                _type = ZoneType.Wall,
                _width = width,
                _heightOffset = depth,
                _textureId = -1,
                _heightBlendMode = HeightBlendMode.Blend,
                _heightCurve = CurveUtils.CreateSteepCurve()
            };
            return zone;
        }

        public static ProfileZone CreateRim(float width, float height, int textureId)
        {
            return new ProfileZone
            {
                _name = "Rim",
                _type = ZoneType.Rim,
                _width = width,
                _heightOffset = height,
                _textureId = textureId,
                _heightBlendMode = HeightBlendMode.Add,
                _heightCurve = CurveUtils.CreateBellCurve()
            };
        }
        #endregion

        #region GPU Data
        /// <summary>
        /// Pack zone data for GPU. Must match GLSL struct layout.
        /// </summary>
        public float[] ToGpuData()
        {
            var data = new System.Collections.Generic.List<float>
            {
                // Zone basic info (16 bytes, 4 floats)
                _enabled ? 1.0f : 0.0f,
                _width,
                _heightOffset,
                _heightStrength,

                // Height settings (16 bytes, 4 floats)
                (float)_heightBlendMode,
                _terrainConformance,
                0.0f, // padding
                0.0f, // padding

                // Texture settings (16 bytes, 4 floats)
                (float)_textureId,
                _textureStrength,
                (float)_textureBlendMode,
                0.0f, // padding
            };

            // Height noise (40 bytes, 10 floats)
            if (_heightNoise != null)
                data.AddRange(_heightNoise.ToGpuData());
            else
                data.AddRange(new float[NoiseConfig.GPU_DATA_SIZE]);

            // Texture noise (40 bytes, 10 floats)
            if (_textureNoise != null)
                data.AddRange(_textureNoise.ToGpuData());
            else
                data.AddRange(new float[NoiseConfig.GPU_DATA_SIZE]);

            return data.ToArray();
        }

        public const int GPU_DATA_SIZE = 4 + 4 + 4 + NoiseConfig.GPU_DATA_SIZE * 2; // 32 floats
        #endregion
    }
}