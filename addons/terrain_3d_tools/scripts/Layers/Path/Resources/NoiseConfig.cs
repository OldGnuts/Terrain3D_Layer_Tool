// /Layers/Path/Resources/NoiseConfig.cs
using Godot;

namespace Terrain3DTools.Layers.Path
{
    /// <summary>
    /// Configurable noise settings for path zones.
    /// Can be applied to height, texture, or both independently.
    /// </summary>
    [GlobalClass, Tool]
    public partial class NoiseConfig : Resource
    {
        #region Private Fields
        private bool _enabled = false;
        private float _amplitude = 0.5f;
        private float _frequency = 0.1f;
        private int _octaves = 3;
        private float _persistence = 0.5f;
        private float _lacunarity = 2.0f;
        private int _seed = 0;
        private Vector2 _offset = Vector2.Zero;
        private bool _useWorldCoords = true;
        private Curve _remapCurve;
        #endregion

        #region Exported Properties
        [Export]
        public bool Enabled
        {
            get => _enabled;
            set { _enabled = value; EmitChanged(); }
        }

        [Export(PropertyHint.Range, "0.0,10.0,0.01")]
        public float Amplitude
        {
            get => _amplitude;
            set { _amplitude = value; EmitChanged(); }
        }

        [Export(PropertyHint.Range, "0.001,1.0,0.001")]
        public float Frequency
        {
            get => _frequency;
            set { _frequency = value; EmitChanged(); }
        }

        [Export(PropertyHint.Range, "1,8,1")]
        public int Octaves
        {
            get => _octaves;
            set { _octaves = Mathf.Clamp(value, 1, 8); EmitChanged(); }
        }

        [Export(PropertyHint.Range, "0.1,1.0,0.05")]
        public float Persistence
        {
            get => _persistence;
            set { _persistence = value; EmitChanged(); }
        }

        [Export(PropertyHint.Range, "1.0,4.0,0.1")]
        public float Lacunarity
        {
            get => _lacunarity;
            set { _lacunarity = value; EmitChanged(); }
        }

        [Export]
        public int Seed
        {
            get => _seed;
            set { _seed = value; EmitChanged(); }
        }

        [Export]
        public Vector2 Offset
        {
            get => _offset;
            set { _offset = value; EmitChanged(); }
        }

        /// <summary>
        /// If true, noise samples world coordinates. If false, samples path-local UV.
        /// </summary>
        [Export]
        public bool UseWorldCoords
        {
            get => _useWorldCoords;
            set { _useWorldCoords = value; EmitChanged(); }
        }

        /// <summary>
        /// Optional curve to remap noise output (0-1 input → custom output).
        /// </summary>
        [Export]
        public Curve RemapCurve
        {
            get => _remapCurve;
            set { _remapCurve = value; EmitChanged(); }
        }
        #endregion

        #region Factory Methods
        public static NoiseConfig CreateDefault()
        {
            return new NoiseConfig
            {
                _enabled = false,
                _amplitude = 0.5f,
                _frequency = 0.1f,
                _octaves = 3,
                _persistence = 0.5f,
                _lacunarity = 2.0f,
                _seed = 0
            };
        }

        public static NoiseConfig CreateSubtle()
        {
            return new NoiseConfig
            {
                _enabled = true,
                _amplitude = 0.1f,
                _frequency = 0.2f,
                _octaves = 2,
                _persistence = 0.5f,
                _lacunarity = 2.0f,
                _seed = 0
            };
        }

        public static NoiseConfig CreateRough()
        {
            return new NoiseConfig
            {
                _enabled = true,
                _amplitude = 1.0f,
                _frequency = 0.05f,
                _octaves = 4,
                _persistence = 0.6f,
                _lacunarity = 2.0f,
                _seed = 0
            };
        }
        #endregion

        #region Serialization Helpers
        /// <summary>
        /// Pack noise config into float array for GPU upload.
        /// Layout: [enabled, amplitude, frequency, octaves, persistence, lacunarity, seed, offsetX, offsetY, useWorldCoords]
        /// </summary>
        public float[] ToGpuData()
        {
            return new float[]
            {
                _enabled ? 1.0f : 0.0f,
                _amplitude,
                _frequency,
                (float)_octaves,
                _persistence,
                _lacunarity,
                (float)_seed,
                _offset.X,
                _offset.Y,
                _useWorldCoords ? 1.0f : 0.0f
            };
        }

        public const int GPU_DATA_SIZE = 10; // floats
        #endregion
    }
}