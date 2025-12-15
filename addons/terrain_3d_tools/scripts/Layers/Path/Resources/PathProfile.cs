// /Layers/Path/Resources/PathProfile.cs
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

        /// <summary>
        /// Zones ordered from center outward. The first zone starts at the path centerline.
        /// </summary>
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

        /// <summary>
        /// Global curve applied to transitions between zones.
        /// </summary>
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

        /// <summary>
        /// Smoothing applied to the SDF for softer corners.
        /// </summary>
        [Export(PropertyHint.Range, "0.0,10.0,0.1")]
        public float GlobalSmoothingRadius
        {
            get => _globalSmoothingRadius;
            set { _globalSmoothingRadius = Mathf.Max(0, value); EmitChanged(); }
        }

        /// <summary>
        /// If true, profile is mirrored on both sides. If false, zones only apply to one side.
        /// </summary>
        [Export]
        public bool Symmetrical
        {
            get => _symmetrical;
            set { _symmetrical = value; EmitChanged(); }
        }
        #endregion

        #region Computed Properties
        /// <summary>
        /// Total width of the profile (sum of all zone widths, doubled if symmetrical).
        /// </summary>
        public float TotalWidth
        {
            get
            {
                float sum = _zones.Where(z => z != null && z.Enabled).Sum(z => z.Width);
                return _symmetrical ? sum * 2 : sum;
            }
        }

        /// <summary>
        /// Half-width from centerline to outer edge.
        /// </summary>
        public float HalfWidth => _zones.Where(z => z != null && z.Enabled).Sum(z => z.Width);

        /// <summary>
        /// Number of enabled zones.
        /// </summary>
        public int EnabledZoneCount => _zones.Count(z => z != null && z.Enabled);
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
            foreach (var zone in _zones.Where(z => z != null))
            {
                zone.Changed += OnChanged;
            }
        }

        private void UnsubscribeFromZones()
        {
            foreach (var zone in _zones.Where(z => z != null))
            {
                zone.Changed -= OnChanged;
            }
        }

        private void OnChanged() => EmitChanged();
        #endregion

        #region Zone Queries
        /// <summary>
        /// Get the zone and parameter at a given distance from centerline.
        /// Returns (zoneIndex, parameterWithinZone) where parameter is 0-1.
        /// </summary>
        public (int zoneIndex, float parameter) GetZoneAtDistance(float distance)
        {
            distance = Mathf.Abs(distance);
            float accumulated = 0f;

            for (int i = 0; i < _zones.Count; i++)
            {
                var zone = _zones[i];
                if (zone == null || !zone.Enabled) continue;

                float zoneEnd = accumulated + zone.Width;
                if (distance <= zoneEnd)
                {
                    float parameter = (distance - accumulated) / zone.Width;
                    return (i, Mathf.Clamp(parameter, 0f, 1f));
                }
                accumulated = zoneEnd;
            }

            // Beyond all zones
            return (-1, 1f);
        }

        /// <summary>
        /// Get cumulative distances for zone boundaries (for GPU).
        /// </summary>
        public float[] GetZoneBoundaries()
        {
            var boundaries = new List<float>();
            float accumulated = 0f;

            foreach (var zone in _zones.Where(z => z != null && z.Enabled))
            {
                accumulated += zone.Width;
                boundaries.Add(accumulated);
            }

            return boundaries.ToArray();
        }
        #endregion

        #region GPU Data
        /// <summary>
        /// Pack profile data for GPU upload.
        /// </summary>
        public byte[] ToGpuBuffer()
        {
            var enabledZones = _zones.Where(z => z != null && z.Enabled).ToList();
            var data = new List<float>();

            // Header (16 bytes, 4 floats)
            data.Add((float)enabledZones.Count);
            data.Add(_globalSmoothingRadius);
            data.Add(_symmetrical ? 1.0f : 0.0f);
            data.Add(HalfWidth);

            // Zone boundaries (up to 8 zones, 32 bytes)
            var boundaries = GetZoneBoundaries();
            for (int i = 0; i < 8; i++)
            {
                data.Add(i < boundaries.Length ? boundaries[i] : 9999.0f);
            }

            // Zone data
            foreach (var zone in enabledZones)
            {
                data.AddRange(zone.ToGpuData());
            }

            // Pad to ensure we have data for at least one zone
            while (data.Count < 4 + 8 + ProfileZone.GPU_DATA_SIZE)
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