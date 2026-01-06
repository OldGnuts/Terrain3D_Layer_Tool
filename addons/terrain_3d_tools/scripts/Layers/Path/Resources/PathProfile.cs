using Godot;
using Godot.Collections;
using System.Collections.Generic;
using System.Linq;
using Terrain3DTools.Core;

namespace Terrain3DTools.Layers.Path
{
    /// <summary>
    /// Defines the complete cross-section profile of a path.
    /// Contains ordered zones from center outward, plus global settings.
    /// </summary>
    [GlobalClass, Tool]
    public partial class PathProfile : Resource
    {
        #region Constants
        private const int PROFILE_HEADER_FLOATS = 4;
        private const int MAX_ZONE_BOUNDARIES = 8;
        #endregion

        #region Private Fields
        private string _name = "Custom Profile";
        private Array<ProfileZone> _zones = new();
        private Curve _transitionCurve;
        private float _globalSmoothingRadius = 1.0f;
        private bool _symmetrical = true;
        private bool _transitionCurveSubscribed = false;
        #endregion

        #region Exported Properties
        [Export]
        public string Name
        {
            get => _name;
            set { _name = value; EmitChanged(); }
        }

        [Export]
        public Array<ProfileZone> Zones
        {
            get => _zones;
            set
            {
                UnsubscribeFromZones();
                _zones = value ?? new Array<ProfileZone>();
                SubscribeToZones();
                EmitChanged();
            }
        }

        [Export]
        public Curve TransitionCurve
        {
            get => _transitionCurve;
            set
            {
                if (_transitionCurveSubscribed && _transitionCurve != null)
                {
                    _transitionCurve.Changed -= OnChanged;
                    _transitionCurveSubscribed = false;
                }

                _transitionCurve = value;

                if (_transitionCurve != null)
                {
                    _transitionCurve.Changed += OnChanged;
                    _transitionCurveSubscribed = true;
                }

                EmitChanged();
            }
        }

        [Export(PropertyHint.Range, "0.0,10.0,0.1")]
        public float GlobalSmoothingRadius
        {
            get => _globalSmoothingRadius;
            set { _globalSmoothingRadius = Mathf.Max(0, value); EmitChanged(); }
        }

        [Export]
        public bool Symmetrical
        {
            get => _symmetrical;
            set { _symmetrical = value; EmitChanged(); }
        }
        #endregion

        #region Computed Properties
        public float TotalWidth
        {
            get
            {
                float sum = GetEnabledZones().Sum(z => z.Width);
                return _symmetrical ? sum * 2 : sum;
            }
        }

        public float HalfWidth => GetEnabledZones().Sum(z => z.Width);

        public int EnabledZoneCount => GetEnabledZones().Count();
        #endregion

        #region Zone Access Helpers
        /// <summary>
        /// Returns all enabled zones. Use this instead of filtering Zones directly.
        /// </summary>
        public IEnumerable<ProfileZone> GetEnabledZones()
        {
            return _zones.Where(z => z != null && z.Enabled);
        }

        /// <summary>
        /// Returns all zones (including disabled).
        /// </summary>
        public IEnumerable<ProfileZone> GetAllZones()
        {
            return _zones.Where(z => z != null);
        }
        #endregion

        #region Zone Management
        public void AddZone(ProfileZone zone)
        {
            if (zone == null) return;
            _zones.Add(zone);
            zone.Changed += OnChanged;
            EmitChanged();
        }

        public void RemoveZone(int index)
        {
            if (index < 0 || index >= _zones.Count) return;
            var zone = _zones[index];
            if (zone != null) zone.Changed -= OnChanged;
            _zones.RemoveAt(index);
            EmitChanged();
        }

        public void InsertZone(int index, ProfileZone zone)
        {
            if (zone == null) return;
            index = Mathf.Clamp(index, 0, _zones.Count);
            _zones.Insert(index, zone);
            zone.Changed += OnChanged;
            EmitChanged();
        }

        public void MoveZone(int fromIndex, int toIndex)
        {
            if (fromIndex < 0 || fromIndex >= _zones.Count) return;
            if (toIndex < 0 || toIndex >= _zones.Count) return;
            var zone = _zones[fromIndex];
            _zones.RemoveAt(fromIndex);
            _zones.Insert(toIndex, zone);
            EmitChanged();
        }

        private void SubscribeToZones()
        {
            foreach (var zone in GetAllZones())
            {
                zone.Changed += OnChanged;
            }
        }

        private void UnsubscribeFromZones()
        {
            foreach (var zone in GetAllZones())
            {
                zone.Changed -= OnChanged;
            }
        }

        private void OnChanged() => EmitChanged();
        #endregion

        #region Zone Queries
        public (int zoneIndex, float parameter) GetZoneAtDistance(float distance)
        {
            distance = Mathf.Abs(distance);
            float accumulated = 0f;

            int index = 0;
            foreach (var zone in GetEnabledZones())
            {
                float zoneEnd = accumulated + zone.Width;
                if (distance <= zoneEnd)
                {
                    float parameter = (distance - accumulated) / zone.Width;
                    return (index, Mathf.Clamp(parameter, 0f, 1f));
                }
                accumulated = zoneEnd;
                index++;
            }

            return (-1, 1f);
        }

        public float[] GetZoneBoundaries()
        {
            var boundaries = new List<float>();
            float accumulated = 0f;

            foreach (var zone in GetEnabledZones())
            {
                accumulated += zone.Width;
                boundaries.Add(accumulated);
            }

            return boundaries.ToArray();
        }
        #endregion

        #region GPU Data
        /// <summary>
        /// Convert profile to GPU buffer with values in world units.
        /// Use for apply shaders where zone index comes from pre-computed textures.
        /// </summary>
        public byte[] ToGpuBuffer()
        {
            return ToGpuBufferInternal(1.0f);
        }

        /// <summary>
        /// Convert profile to GPU buffer with widths/distances scaled to pixel space.
        /// Use for SDF and Mask generation shaders.
        /// </summary>
        /// <param name="worldToPixelScale">Scale factor to convert world units to pixels</param>
        public byte[] ToGpuBufferScaled(float worldToPixelScale)
        {
            return ToGpuBufferInternal(worldToPixelScale);
        }

        private byte[] ToGpuBufferInternal(float scale)
        {
            var enabledZones = GetEnabledZones().ToList();
            var data = new List<float>();

            // Header (4 floats = 16 bytes)
            data.Add((float)enabledZones.Count);
            data.Add(_globalSmoothingRadius * scale);  // Scaled
            data.Add(_symmetrical ? 1.0f : 0.0f);
            data.Add(HalfWidth * scale);  // Scaled

            // Zone boundaries (8 floats = 32 bytes) - Scaled
            var boundaries = GetZoneBoundaries();
            for (int i = 0; i < MAX_ZONE_BOUNDARIES; i++)
            {
                float boundary = i < boundaries.Length ? boundaries[i] : 9999.0f;
                data.Add(boundary * scale);
            }

            // Zone data
            foreach (var zone in enabledZones)
            {
                data.AddRange(zone.ToGpuData(scale));
            }

            // Pad to ensure we have data for at least one zone
            int minSize = PROFILE_HEADER_FLOATS + MAX_ZONE_BOUNDARIES + ProfileZone.GPU_DATA_SIZE;
            while (data.Count < minSize)
            {
                data.Add(0.0f);
            }

            return GpuUtils.FloatArrayToBytes(data.ToArray());
        }
        #endregion

        #region Lifecycle
        public PathProfile()
        {
            _transitionCurve = CreateDefaultTransitionCurve();
            if (_transitionCurve != null)
            {
                _transitionCurve.Changed += OnChanged;
                _transitionCurveSubscribed = true;
            }

            _zones = new Array<ProfileZone>();
        }

        private static Curve CreateDefaultTransitionCurve()
        {
            var curve = new Curve();
            curve.AddPoint(new Vector2(0, 0));
            curve.AddPoint(new Vector2(0.5f, 0.5f));
            curve.AddPoint(new Vector2(1, 1));
            return curve;
        }
        #endregion
    }
}