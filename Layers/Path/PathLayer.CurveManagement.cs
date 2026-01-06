//PathLayer.CurveManagement.cs
using Godot;
using System;
using System.Collections.Generic;
using Terrain3DTools.Core.Debug;
using Terrain3DTools.Utils;

namespace Terrain3DTools.Layers
{
    /// <summary>
    /// Curve sampling, bounds computation, and point management for PathLayer.
    /// Shared logic used by both the Visualizer (Main Thread) and the Bake State Snapshot (Worker Thread).
    /// </summary>
    public partial class PathLayer
    {
        #region Bounds Computation
        /// <summary>
        /// Computes 2D world bounds (XZ) from the curve, expanding by profile width.
        /// <para>Used by OnCurveChanged (ExternalEditor) and Mask Generation.</para>
        /// </summary>
        private void ComputeBoundsFromCurve(Curve3D curve, out Vector2 minWorld, out Vector2 maxWorld)
        {
            DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.UpdateCycle,
                $"ComputeBoundsFromCurve - old bounds min:{_maskWorldMin} max:{_maskWorldMax} ");

            minWorld = new Vector2(float.MaxValue, float.MaxValue);
            maxWorld = new Vector2(float.MinValue, float.MinValue);

            if (curve == null || curve.PointCount == 0)
            {
                // Default small area if no curve
                minWorld = new Vector2(-32, -32);
                maxWorld = new Vector2(32, 32);
                return;
            }

            // 1. Iterate all control points and handles to ensure curve is strictly contained
            for (int i = 0; i < curve.PointCount; i++)
            {
                Vector3 pos = curve.GetPointPosition(i);
                Vector3 handleIn = curve.GetPointIn(i);
                Vector3 handleOut = curve.GetPointOut(i);

                ExpandBounds(pos, ref minWorld, ref maxWorld);

                if (handleIn.LengthSquared() > 0.001f)
                    ExpandBounds(pos + handleIn, ref minWorld, ref maxWorld);
                if (handleOut.LengthSquared() > 0.001f)
                    ExpandBounds(pos + handleOut, ref minWorld, ref maxWorld);
            }

            // 2. Sample baked curve to catch bezier bulges that extend beyond handles
            float length = curve.GetBakedLength();
            if (length > 0)
            {
                // Adaptive sampling count based on length
                int samples = Mathf.Clamp((int)(length / 2f), 16, 256);
                for (int i = 0; i <= samples; i++)
                {
                    float t = (float)i / samples;
                    Vector3 pos = curve.SampleBaked(t * length);
                    ExpandBounds(pos, ref minWorld, ref maxWorld);
                }
            }

            // 3. Add Profile Width Margin
            // We expand bounds by the HalfWidth of the profile + safety margin
            // to ensure the mask texture covers the entire path width + falloff.
            float margin = (ProfileHalfWidth * 1.2f) + 5.0f;

            minWorld -= new Vector2(margin, margin);
            maxWorld += new Vector2(margin, margin);
            DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.UpdateCycle,
                $"ComputeBoundsFromCurve - new bounds min:{minWorld} max:{maxWorld} ");
        }

        /// <summary>
        /// Compute bounds and update internal state. 
        /// Called explicitly when bounds are dirty but curve hasn't fired changed signal.
        /// </summary>
        private void ComputeMaskBounds()
        {
            ComputeBoundsFromCurve(Curve, out _maskWorldMin, out _maskWorldMax);
            _maskBoundsInitialized = true;
        }

        private static void ExpandBounds(Vector3 worldPos, ref Vector2 min, ref Vector2 max)
        {
            min.X = Mathf.Min(min.X, worldPos.X);
            min.Y = Mathf.Min(min.Y, worldPos.Z);
            max.X = Mathf.Max(max.X, worldPos.X);
            max.Y = Mathf.Max(max.Y, worldPos.Z);
        }
        #endregion

        #region Curve Sampling
        /// <summary>
        /// Samples the Curve3D into a list of points.
        /// <para>MUST be called on Main Thread.</para>
        /// Used by the Visualizer and the Snapshot System.
        /// </summary>
        internal Vector3[] GetSampledPoints()
        {
            // Return cached data if valid
            if (!_curveDataDirty && _cachedSamplePoints != null && _cachedSamplePoints.Length > 0)
            {
                return _cachedSamplePoints;
            }

            var curve = Curve;
            if (curve == null || curve.PointCount < 2)
            {
                _cachedSamplePoints = Array.Empty<Vector3>();
                _cachedDistances = Array.Empty<float>();
                _curveDataDirty = false;
                return _cachedSamplePoints;
            }

            float totalLength = curve.GetBakedLength();
            if (totalLength <= 0)
            {
                _cachedSamplePoints = Array.Empty<Vector3>();
                _cachedDistances = Array.Empty<float>();
                _curveDataDirty = false;
                return _cachedSamplePoints;
            }

            var points = new List<Vector3>();
            var distances = new List<float>();

            if (_adaptiveResolution)
            {
                SampleAdaptive(curve, points, distances, totalLength);
            }
            else
            {
                SampleUniform(curve, points, distances, totalLength);
            }

            _cachedSamplePoints = points.ToArray();
            _cachedDistances = distances.ToArray();
            _curveDataDirty = false;

            return _cachedSamplePoints;
        }

        private void SampleUniform(Curve3D curve, List<Vector3> points, List<float> distances, float totalLength)
        {
            float step = totalLength / (_resolution - 1);

            for (int i = 0; i < _resolution; i++)
            {
                float dist = i * step;
                Vector3 point = curve.SampleBaked(dist);
                points.Add(point);
                distances.Add(dist);
            }
        }

        // In PathLayer.CurveManagement.cs - replace the SampleAdaptive method

        private void SampleAdaptive(Curve3D curve, List<Vector3> points, List<float> distances, float totalLength)
        {
            float minAngleRad = Mathf.DegToRad(_adaptiveMinAngle);
            float minStep = MIN_SEGMENT_LENGTH;
            float maxStep = Mathf.Min(totalLength / 8.0f, 10.0f);

            float currentDist = 0f;

            points.Add(curve.SampleBaked(0));
            distances.Add(0f);

            while (currentDist < totalLength - 0.001f)
            {
                float step = maxStep;

                // Test progressively smaller steps until we find one that's smooth enough
                for (int iteration = 0; iteration < 10; iteration++)
                {
                    if (step <= minStep) break;

                    float testEndDist = Mathf.Min(currentDist + step, totalLength);

                    // Check if segment is smooth by sampling MULTIPLE points along it
                    if (!IsSegmentSmooth(curve, currentDist, testEndDist, minAngleRad))
                    {
                        step *= 0.5f;
                    }
                    else
                    {
                        break;
                    }
                }

                step = Mathf.Max(step, minStep);
                currentDist = Mathf.Min(currentDist + step, totalLength);

                points.Add(curve.SampleBaked(currentDist));
                distances.Add(currentDist);

                if (currentDist >= totalLength - 0.001f) break;
            }

            // Ensure end point
            if (distances.Count == 0 || Mathf.Abs(distances[^1] - totalLength) > 0.01f)
            {
                points.Add(curve.SampleBaked(totalLength));
                distances.Add(totalLength);
            }
        }

        /// <summary>
        /// Checks if a segment of the curve is smooth enough by sampling multiple points
        /// along the segment and checking tangent deviation.
        /// </summary>
        private bool IsSegmentSmooth(Curve3D curve, float startDist, float endDist, float maxAngleRad)
        {
            float segmentLength = endDist - startDist;
            if (segmentLength < MIN_SEGMENT_LENGTH) return true;

            // Sample 4 points along the segment to check for hidden curvature
            const int checkPoints = 4;

            Vector3 startPoint = curve.SampleBaked(startDist);
            Vector3 endPoint = curve.SampleBaked(endDist);

            // The straight line from start to end
            Vector3 chordDir = (endPoint - startPoint).Normalized();
            if (chordDir.LengthSquared() < 0.001f) return true;

            float maxDeviation = 0f;
            Vector3 lastTangent = chordDir;

            for (int i = 1; i <= checkPoints; i++)
            {
                float t = (float)i / (checkPoints + 1);
                float sampleDist = Mathf.Lerp(startDist, endDist, t);

                Vector3 samplePoint = curve.SampleBaked(sampleDist);

                // Check how far this point deviates from the straight line
                Vector3 toSample = samplePoint - startPoint;
                float projLength = toSample.Dot(chordDir);
                Vector3 projected = startPoint + chordDir * projLength;
                float deviation = samplePoint.DistanceTo(projected);
                maxDeviation = Mathf.Max(maxDeviation, deviation);

                // Also check tangent direction change
                float tangentDist = Mathf.Min(sampleDist + 0.1f, endDist);
                Vector3 tangent = (curve.SampleBaked(tangentDist) - samplePoint).Normalized();
                if (tangent.LengthSquared() > 0.001f)
                {
                    float angle = lastTangent.AngleTo(tangent);
                    if (angle > maxAngleRad)
                    {
                        return false; // Too much curvature
                    }
                    lastTangent = tangent;
                }
            }

            // If any point deviates too far from the chord, segment is not smooth
            // Threshold: deviation should be less than 5% of segment length, or 0.5 units max
            float deviationThreshold = Mathf.Min(segmentLength * 0.05f, 0.5f);
            if (maxDeviation > deviationThreshold)
            {
                return false;
            }

            // Also check overall tangent change from start to end
            float startTangentDist = Mathf.Min(startDist + 0.1f, endDist);
            Vector3 startTangent = (curve.SampleBaked(startTangentDist) - startPoint).Normalized();

            float endTangentDist = Mathf.Max(endDist - 0.1f, startDist);
            Vector3 endTangent = (endPoint - curve.SampleBaked(endTangentDist)).Normalized();

            if (startTangent.LengthSquared() > 0.001f && endTangent.LengthSquared() > 0.001f)
            {
                float totalAngle = startTangent.AngleTo(endTangent);
                if (totalAngle > maxAngleRad * 2f) // Allow slightly more for total span
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Get tangent direction at a distance along the curve (world space).
        /// Used by Visualization.
        /// </summary>
        public Vector3 GetTangentAtDistance(float distance)
        {
            var curve = Curve;
            if (curve == null) return Vector3.Forward;

            float totalLength = curve.GetBakedLength();
            distance = Mathf.Clamp(distance, 0, totalLength);

            float delta = 0.1f;
            Vector3 p1 = curve.SampleBaked(Mathf.Max(0, distance - delta));
            Vector3 p2 = curve.SampleBaked(Mathf.Min(totalLength, distance + delta));

            return (p2 - p1).Normalized();
        }
        #endregion

        #region Point Management (Proxies to External Curve)
        public void AddPoint(Vector3 worldPosition)
        {
            Curve?.AddPoint(worldPosition);
        }

        public void InsertPoint(int index, Vector3 worldPosition)
        {
            var curve = Curve;
            if (curve == null) return;

            if (index >= curve.PointCount)
            {
                curve.AddPoint(worldPosition);
            }
            else
            {
                // Rebuild curve to insert
                var points = new List<Vector3>();
                var inHandles = new List<Vector3>();
                var outHandles = new List<Vector3>();

                for (int i = 0; i < curve.PointCount; i++)
                {
                    if (i == index)
                    {
                        points.Add(worldPosition);
                        inHandles.Add(Vector3.Zero);
                        outHandles.Add(Vector3.Zero);
                    }
                    points.Add(curve.GetPointPosition(i));
                    inHandles.Add(curve.GetPointIn(i));
                    outHandles.Add(curve.GetPointOut(i));
                }

                curve.ClearPoints();
                for (int i = 0; i < points.Count; i++)
                {
                    curve.AddPoint(points[i], inHandles[i], outHandles[i]);
                }
            }
        }

        public void RemovePoint(int index)
        {
            var curve = Curve;
            if (curve == null || index < 0 || index >= curve.PointCount) return;
            curve.RemovePoint(index);
        }

        public void ClearPoints()
        {
            Curve?.ClearPoints();
        }

        public Vector3 GetPointWorldPosition(int index)
        {
            var curve = Curve;
            if (curve == null || index < 0 || index >= curve.PointCount) return Vector3.Zero;
            return curve.GetPointPosition(index);
        }

        public void SetPointWorldPosition(int index, Vector3 worldPosition)
        {
            var curve = Curve;
            if (curve == null || index < 0 || index >= curve.PointCount) return;
            curve.SetPointPosition(index, worldPosition);
        }
        #endregion

        // In PathLayer.CurveManagement.cs - add these methods

        #region Curve Smoothing

        /// <summary>
        /// Automatically generates smooth bezier handles for all curve points.
        /// Uses Catmull-Rom style tangent calculation.
        /// </summary>
        /// <param name="tension">0 = very smooth, 1 = linear. Default 0.5</param>
        public void AutoSmoothCurvePoints(float tension = 0.5f)
        {
            var curve = Curve;
            if (curve == null || curve.PointCount < 2) return;
            if (_isAutoSmoothing) return; // Prevent recursion

            _isAutoSmoothing = true;

            try
            {
                tension = Mathf.Clamp(tension, 0f, 1f);
                float handleScale = 1f - tension;

                for (int i = 0; i < curve.PointCount; i++)
                {
                    Vector3 tangent = CalculateTangentAt(curve, i);
                    float handleLength = CalculateHandleLength(curve, i) * handleScale * 0.5f;

                    Vector3 handleIn = -tangent * handleLength;
                    Vector3 handleOut = tangent * handleLength;

                    // First point: no in handle
                    if (i == 0)
                    {
                        handleIn = Vector3.Zero;
                    }
                    // Last point: no out handle
                    if (i == curve.PointCount - 1)
                    {
                        handleOut = Vector3.Zero;
                    }

                    curve.SetPointIn(i, handleIn);
                    curve.SetPointOut(i, handleOut);
                }
            }
            finally
            {
                _isAutoSmoothing = false;
            }
        }

        /// <summary>
        /// Public method for manual smoothing button.
        /// </summary>
        public void ApplyAutoSmooth(float tension)
        {
            AutoSmoothCurvePoints(tension);
        }

        /// <summary>
        /// Removes all bezier handles, making the curve sharp/linear between points.
        /// </summary>
        public void ClearCurveHandles()
        {
            var curve = Curve;
            if (curve == null) return;
            if (_isAutoSmoothing) return;

            _isAutoSmoothing = true;

            try
            {
                for (int i = 0; i < curve.PointCount; i++)
                {
                    curve.SetPointIn(i, Vector3.Zero);
                    curve.SetPointOut(i, Vector3.Zero);
                }
            }
            finally
            {
                _isAutoSmoothing = false;
            }
        }

        /// <summary>
        /// Calculates smooth handles for a single point.
        /// Useful when adding new points.
        /// </summary>
        public void SmoothPointHandles(int index, float tension = 0.5f)
        {
            var curve = Curve;
            if (curve == null || index < 0 || index >= curve.PointCount) return;
            if (_isAutoSmoothing) return;

            _isAutoSmoothing = true;

            try
            {
                tension = Mathf.Clamp(tension, 0f, 1f);
                float handleScale = 1f - tension;

                // Smooth the new point
                ApplySmoothToPoint(curve, index, handleScale);

                // Also smooth neighboring points for continuity
                if (index > 0)
                {
                    ApplySmoothToPoint(curve, index - 1, handleScale);
                }
                if (index < curve.PointCount - 1)
                {
                    ApplySmoothToPoint(curve, index + 1, handleScale);
                }
            }
            finally
            {
                _isAutoSmoothing = false;
            }
        }

        private void ApplySmoothToPoint(Curve3D curve, int index, float handleScale)
        {
            Vector3 tangent = CalculateTangentAt(curve, index);
            float handleLength = CalculateHandleLength(curve, index) * handleScale * 0.5f;

            Vector3 handleIn = -tangent * handleLength;
            Vector3 handleOut = tangent * handleLength;

            if (index == 0) handleIn = Vector3.Zero;
            if (index == curve.PointCount - 1) handleOut = Vector3.Zero;

            curve.SetPointIn(index, handleIn);
            curve.SetPointOut(index, handleOut);
        }

        /// <summary>
        /// Calculates the tangent direction at a point using neighboring points.
        /// Uses Catmull-Rom style calculation.
        /// </summary>
        private Vector3 CalculateTangentAt(Curve3D curve, int index)
        {
            if (curve.PointCount == 1)
            {
                return Vector3.Forward;
            }

            Vector3 current = curve.GetPointPosition(index);

            if (curve.PointCount == 2)
            {
                Vector3 other = curve.GetPointPosition(index == 0 ? 1 : 0);
                return (other - current).Normalized();
            }

            if (index == 0)
            {
                // First point: direction toward second point
                Vector3 next = curve.GetPointPosition(1);
                return (next - current).Normalized();
            }
            else if (index == curve.PointCount - 1)
            {
                // Last point: direction from second-to-last
                Vector3 prev = curve.GetPointPosition(index - 1);
                return (current - prev).Normalized();
            }
            else
            {
                // Middle point: average direction from prev to next
                Vector3 prev = curve.GetPointPosition(index - 1);
                Vector3 next = curve.GetPointPosition(index + 1);
                return (next - prev).Normalized();
            }
        }

        /// <summary>
        /// Calculates appropriate handle length based on distance to neighbors.
        /// </summary>
        private float CalculateHandleLength(Curve3D curve, int index)
        {
            if (curve.PointCount < 2) return 1f;

            Vector3 current = curve.GetPointPosition(index);
            float totalDist = 0f;
            int count = 0;

            if (index > 0)
            {
                totalDist += current.DistanceTo(curve.GetPointPosition(index - 1));
                count++;
            }
            if (index < curve.PointCount - 1)
            {
                totalDist += current.DistanceTo(curve.GetPointPosition(index + 1));
                count++;
            }

            return count > 0 ? totalDist / count : 1f;
        }

        /// <summary>
        /// Subdivides the curve by adding midpoints, then smooths.
        /// Good for creating more detail in sharp curves.
        /// </summary>
        public void SubdivideCurve()
        {
            var curve = Curve;
            if (curve == null || curve.PointCount < 2) return;
            if (_isAutoSmoothing) return;

            _isAutoSmoothing = true;

            try
            {
                // Collect midpoints by sampling the baked curve
                var newPoints = new List<(Vector3 pos, int insertAfter)>();
                float totalLength = curve.GetBakedLength();

                for (int i = 0; i < curve.PointCount - 1; i++)
                {
                    Vector3 p1 = curve.GetPointPosition(i);
                    Vector3 p2 = curve.GetPointPosition(i + 1);

                    // Find the approximate distance along the baked curve for these points
                    float dist1 = curve.GetClosestOffset(p1);
                    float dist2 = curve.GetClosestOffset(p2);
                    float midDist = (dist1 + dist2) * 0.5f;

                    // Sample the actual bezier curve at midpoint
                    Vector3 midPoint = curve.SampleBaked(midDist);

                    newPoints.Add((midPoint, i));
                }

                // Insert in reverse order to maintain indices
                for (int i = newPoints.Count - 1; i >= 0; i--)
                {
                    var (pos, insertAfter) = newPoints[i];
                    InsertPointInternal(insertAfter + 1, pos);
                }
            }
            finally
            {
                _isAutoSmoothing = false;
            }

            // Re-smooth the curve if auto-smooth is enabled
            if (_autoSmoothCurve)
            {
                AutoSmoothCurvePoints(_autoSmoothTension);
            }
        }

        private void InsertPointInternal(int index, Vector3 position)
        {
            var curve = Curve;
            if (curve == null) return;

            var points = new List<(Vector3 pos, Vector3 inH, Vector3 outH)>();

            for (int i = 0; i < curve.PointCount; i++)
            {
                if (i == index)
                {
                    points.Add((position, Vector3.Zero, Vector3.Zero));
                }
                points.Add((
                    curve.GetPointPosition(i),
                    curve.GetPointIn(i),
                    curve.GetPointOut(i)
                ));
            }

            if (index >= curve.PointCount)
            {
                points.Add((position, Vector3.Zero, Vector3.Zero));
            }

            curve.ClearPoints();
            foreach (var (pos, inH, outH) in points)
            {
                curve.AddPoint(pos, inH, outH);
            }
        }

        #endregion
    }
}