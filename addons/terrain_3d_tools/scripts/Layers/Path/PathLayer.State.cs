// /Layers/Path/PathLayer.State.cs
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using Terrain3DTools.Core;
using Terrain3DTools.Core.Debug;
using Terrain3DTools.Utils;

namespace Terrain3DTools.Layers
{
    public partial class PathLayer
    {
        #region State Snapshot System (Thread Safety)

        /// <summary>
        /// An immutable snapshot of the PathLayer state required for background processing.
        /// Capturing this on the Main Thread prevents race conditions when the curve 
        /// is modified during asynchronous task execution.
        /// </summary>
        public class PathBakeState
        {
            /// <summary>
            /// Frame number when this state was captured. Used to detect stale state usage.
            /// </summary>
            public ulong CycleVersion;

            public Vector3[] Points;
            public Vector2 MaskWorldMin;
            public Vector2 MaskWorldMax;
            public Vector2I Size;
            public float WorldHeightScale;

            // Resources (Captured to prevent race conditions during resize)
            public Rid SdfTextureRid;
            public Rid ZoneTextureRid;
            public Rid LayerTextureRid;

            // GPU Buffers (Pre-calculated to save worker time/complexity)
            public byte[] ProfileGpuDataScaled;
            public byte[] ProfileGpuDataUnscaled;
            public byte[] ZoneCurveData;

            // Profile Params
            public float ProfileHalfWidth;
            public float CornerSmoothing;
            public bool SmoothCorners;
            public int EnabledZoneCount;

            public override string ToString() =>
                $"Version: {CycleVersion} | " +
                $"Points: {(Points?.Length ?? 0)} | " +
                $"MaskWorldMin: {MaskWorldMin} | " +
                $"MaskWorldMax: {MaskWorldMax} | " +
                $"Size: {Size} | " +
                $"WorldHeightScale: {WorldHeightScale:0.00} | " +
                $"SdfRid: {SdfTextureRid} | " +
                $"ZoneRid: {ZoneTextureRid} | " +
                $"LayerRid: {LayerTextureRid} | " +
                $"ProfileGpuBytes: {(ProfileGpuDataScaled?.Length ?? 0)} | " +
                $"UnscaledGpuBytes: {(ProfileGpuDataUnscaled?.Length ?? 0)} | " +
                $"ZoneCurveBytes: {(ZoneCurveData?.Length ?? 0)} | " +
                $"ProfileWidth: {ProfileHalfWidth:0.00} | " +
                $"Smoothing: {CornerSmoothing:0.00} | " +
                $"SmoothCorners: {SmoothCorners} | " +
                $"Zones: {EnabledZoneCount}";
        }

        /// <summary>
        /// Captures the current state of the Path and Curve.
        /// <para>MUST be called on the Main Thread.</para>
        /// <para>Should only be called from PrepareMaskResources() to ensure single capture per cycle.</para>
        /// </summary>
        private PathBakeState CaptureBakeState()
        {
            DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.UpdateCycle,
                $"CaptureBakeState - Capturing new state at frame {Engine.GetProcessFrames()}");

            if (_profile != null)
            {
                var zones = _profile.GetEnabledZones().ToList();
                for (int i = 0; i < zones.Count; i++)
                {
                    var zone = zones[i];
                    DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.UpdateCycle,
                        $"  Zone[{i}] at capture: Width={zone.Width:F2}, HeightOffset={zone.HeightOffset:F2}, HeightStrength={zone.HeightStrength:F2}");
                }
            }
            
            // 1. Snapshot Curve Points safely
            Vector3[] points;
            if (Curve != null && Curve.PointCount >= 2)
            {
                points = GetSampledPoints();
            }
            else
            {
                points = Array.Empty<Vector3>();
            }

            // 2. Snapshot Bounds
            Vector2 min = MaskWorldMin;
            Vector2 max = MaskWorldMax;
            Vector2I size = Size;

            // 3. Calculate Scale for Profile Buffer
            Vector2 maskWorldSize = max - min;
            float scaleX = maskWorldSize.X > 0.001f ? size.X / maskWorldSize.X : 1.0f;
            float scaleY = maskWorldSize.Y > 0.001f ? size.Y / maskWorldSize.Y : 1.0f;
            float avgScale = (scaleX + scaleY) / 2f;

            // 4. Handle Height Scale Default
            float heightScale = _worldHeightScale > 0.001f ? _worldHeightScale : 128.0f;

            PathBakeState state = new PathBakeState
            {
                CycleVersion = Engine.GetProcessFrames(),
                Points = points,
                MaskWorldMin = min,
                MaskWorldMax = max,
                Size = size,
                WorldHeightScale = heightScale,

                // Capture RIDs at this exact moment
                SdfTextureRid = _sdfTextureRid,
                ZoneTextureRid = _zoneDataTextureRid,
                LayerTextureRid = layerTextureRID,

                ProfileGpuDataScaled = _profile?.ToGpuBufferScaled(avgScale) ?? new byte[64],
                ProfileGpuDataUnscaled = _profile?.ToGpuBuffer() ?? new byte[64],
                ZoneCurveData = BakeZoneCurves(),

                ProfileHalfWidth = _profile?.HalfWidth ?? 2.0f,
                CornerSmoothing = _cornerSmoothing,
                SmoothCorners = _smoothCorners,
                EnabledZoneCount = _profile?.EnabledZoneCount ?? 1
            };

            DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.UpdateCycle,
                $"CaptureBakeState - New state: {state}");

            return state;
        }

        /// <summary>
        /// Returns the bake state that was captured during PrepareMaskResources().
        /// This is the single source of truth for the current update cycle.
        /// <para>WARNING: Will log a warning if called before PrepareMaskResources().</para>
        /// </summary>
        public PathBakeState GetActiveBakeState()
        {
            if (_activeBakeState == null)
            {
                DebugManager.Instance?.LogWarning(DEBUG_CLASS_NAME,
                    $"GetActiveBakeState called before PrepareMaskResources() - state is null. " +
                    $"This may cause pipeline issues. Frame: {Engine.GetProcessFrames()}");

                // Return null instead of capturing - let the caller handle it
                // This makes the bug visible rather than hiding it with a silent capture
                return null;
            }

            return _activeBakeState;
        }

        /// <summary>
        /// Bake all zone height curves into a GPU buffer.
        /// </summary>
        private byte[] BakeZoneCurves()
        {
            const int CURVE_RESOLUTION = 64;

            var data = new List<float>();

            if (_profile?.Zones == null)
            {
                for (int i = 0; i < CURVE_RESOLUTION; i++) data.Add(1.0f);
                return GpuUtils.FloatArrayToBytes(data.ToArray());
            }

            foreach (var zone in _profile.Zones.Where(z => z != null && z.Enabled))
            {
                var curve = zone.HeightCurve;
                for (int i = 0; i < CURVE_RESOLUTION; i++)
                {
                    float t = (float)i / (CURVE_RESOLUTION - 1);
                    float value = curve?.SampleBaked(t) ?? 1.0f;
                    data.Add(value);
                }
            }

            while (data.Count < CURVE_RESOLUTION) data.Add(1.0f);

            return GpuUtils.FloatArrayToBytes(data.ToArray());
        }
        #endregion

        #region State-Aware Generation (Worker Thread Safe)
        private static float[] GenerateSegmentsFromState(PathBakeState state)
        {
            if (state.Points == null || state.Points.Length < 2) return Array.Empty<float>();

            Vector2 layerMin = state.MaskWorldMin;
            Vector2 layerMax = state.MaskWorldMax;
            Vector2 maskWorldSize = layerMax - layerMin;

            if (maskWorldSize.X < 0.001f || maskWorldSize.Y < 0.001f) return Array.Empty<float>();

            float scaleX = state.Size.X / maskWorldSize.X;
            float scaleY = state.Size.Y / maskWorldSize.Y;

            var segments = new List<float>();
            float accumulatedDist = 0f;

            for (int i = 0; i < state.Points.Length - 1; i++)
            {
                Vector3 p1 = state.Points[i];
                Vector3 p2 = state.Points[i + 1];

                Vector2 start2D = (new Vector2(p1.X, p1.Z) - layerMin) * new Vector2(scaleX, scaleY);
                Vector2 end2D = (new Vector2(p2.X, p2.Z) - layerMin) * new Vector2(scaleX, scaleY);

                float segmentLength = start2D.DistanceTo(end2D);
                if (segmentLength < 0.001f) continue;

                Vector2 tangent = (end2D - start2D).Normalized();
                Vector2 perpendicular = new Vector2(-tangent.Y, tangent.X);

                float startHeight = p1.Y / state.WorldHeightScale;
                float endHeight = p2.Y / state.WorldHeightScale;

                segments.Add(start2D.X); segments.Add(start2D.Y); segments.Add(startHeight); segments.Add(accumulatedDist);
                segments.Add(end2D.X); segments.Add(end2D.Y); segments.Add(endHeight); segments.Add(accumulatedDist + segmentLength);
                segments.Add(tangent.X); segments.Add(tangent.Y); segments.Add(perpendicular.X); segments.Add(perpendicular.Y);

                accumulatedDist += segmentLength;
            }

            return segments.ToArray();
        }
        #endregion
    }
}