using Godot;
using System;
using System.Collections.Generic;
using Terrain3DTools.Core;

namespace Terrain3DTools.Layers
{
    public partial class PathLayer : FeatureLayer
    {
        private bool DoesPathIntersectRegion(Vector2I regionCoords, int regionSize, Vector2 regionMinWorld, Vector2 regionSizeWorld)
        {
            if (Path3D?.Curve == null)
            {
                DebugPrint("DoesPathIntersectRegion: Path3D or Curve is null");
                return false;
            }

            var curve = Path3D.Curve;
            var regionBounds = new Rect2(regionMinWorld, regionSizeWorld);

            // Extend bounds by maximum path influence
            float maxInfluence = Mathf.Max(_pathWidth, _embankmentWidth) + _embankmentFalloff;
            regionBounds = regionBounds.Grow(maxInfluence);

            DebugPrint($"Checking intersection for region {regionCoords}, bounds: {regionBounds}");

            // Sample curve points and check intersection
            int samples = _adaptiveResolution ? CalculateAdaptiveSamples(curve) : _pathResolution;

            for (int i = 0; i < samples; i++)
            {
                float t = (float)i / (samples - 1);
                var worldPos = Path3D.ToGlobal(curve.SampleBaked(t * curve.GetBakedLength()));
                var pos2D = new Vector2(worldPos.X, worldPos.Z);

                if (regionBounds.HasPoint(pos2D))
                {
                    DebugPrint($"Path intersects at sample {i}: {pos2D}");
                    return true;
                }
            }

            DebugPrint($"Path does not intersect region {regionCoords}");
            return false;
        }

        private byte[] GeneratePathDataForRegion(Vector2I regionCoords, int regionSize, Vector2 regionMinWorld, Vector2 regionSizeWorld)
        {
            if (Path3D?.Curve == null) return new byte[0];

            var curve = Path3D.Curve;
            var segments = new List<float>();
            var regionBounds = new Rect2(regionMinWorld, regionSizeWorld);

            // IMPORTANT: Extend bounds significantly to capture segments that might affect this region
            // Use a generous buffer to avoid boundary artifacts
            float maxInfluence = Mathf.Max(_pathWidth, _embankmentWidth) + _embankmentFalloff;
            float boundaryBuffer = maxInfluence * 2.0f; // Double the influence for safety
            var extendedBounds = regionBounds.Grow(boundaryBuffer);

            int samples = _adaptiveResolution ? CalculateAdaptiveSamples(curve) : _pathResolution;

            for (int i = 0; i < samples - 1; i++)
            {
                float t1 = (float)i / (samples - 1);
                float t2 = (float)(i + 1) / (samples - 1);

                var worldPos1 = Path3D.ToGlobal(curve.SampleBaked(t1 * curve.GetBakedLength()));
                var worldPos2 = Path3D.ToGlobal(curve.SampleBaked(t2 * curve.GetBakedLength()));

                var pos2D1 = new Vector2(worldPos1.X, worldPos1.Z);
                var pos2D2 = new Vector2(worldPos2.X, worldPos2.Z);

                // Check with extended bounds to ensure we capture all potentially affecting segments
                if (DoesSegmentIntersectRect(pos2D1, pos2D2, extendedBounds))
                {
                    segments.Add(pos2D1.X);
                    segments.Add(pos2D1.Y);
                    segments.Add(worldPos1.Y / _worldHeightScale);
                    segments.Add(pos2D2.X );
                    segments.Add(pos2D2.Y);
                    segments.Add(worldPos2.Y / _worldHeightScale);
                    segments.Add(1.0f);
                    segments.Add(CalculateFlowDirection(pos2D1, pos2D2));
                }
            }

            return GpuUtils.FloatArrayToBytes(segments.ToArray());
        }

        private bool DoesSegmentIntersectRect(Vector2 start, Vector2 end, Rect2 rect)
        {
            // Simple AABB line intersection test
            if (rect.HasPoint(start) || rect.HasPoint(end)) return true;

            // Check intersection with each edge of the rectangle
            var edges = new Vector2[][]
            {
                new Vector2[] { rect.Position, new Vector2(rect.Position.X + rect.Size.X, rect.Position.Y) },
                new Vector2[] { new Vector2(rect.Position.X + rect.Size.X, rect.Position.Y), rect.Position + rect.Size },
                new Vector2[] { rect.Position + rect.Size, new Vector2(rect.Position.X, rect.Position.Y + rect.Size.Y) },
                new Vector2[] { new Vector2(rect.Position.X, rect.Position.Y + rect.Size.Y), rect.Position }
            };

            foreach (var edge in edges)
            {
                if (DoLinesIntersect(start, end, edge[0], edge[1]))
                    return true;
            }

            return false;
        }

        private bool DoLinesIntersect(Vector2 p1, Vector2 q1, Vector2 p2, Vector2 q2)
        {
            // Find the four orientations needed for general and special cases
            int o1 = Orientation(p1, q1, p2);
            int o2 = Orientation(p1, q1, q2);
            int o3 = Orientation(p2, q2, p1);
            int o4 = Orientation(p2, q2, q1);

            // General case
            if (o1 != o2 && o3 != o4) return true;

            // Special cases
            if (o1 == 0 && OnSegment(p1, p2, q1)) return true;
            if (o2 == 0 && OnSegment(p1, q2, q1)) return true;
            if (o3 == 0 && OnSegment(p2, p1, q2)) return true;
            if (o4 == 0 && OnSegment(p2, q1, q2)) return true;

            return false;
        }

        private int Orientation(Vector2 p, Vector2 q, Vector2 r)
        {
            float val = (q.Y - p.Y) * (r.X - q.X) - (q.X - p.X) * (r.Y - q.Y);
            if (Mathf.Abs(val) < 0.001f) return 0; // Collinear
            return (val > 0) ? 1 : 2; // Clock or Counterclock wise
        }

        private bool OnSegment(Vector2 p, Vector2 q, Vector2 r)
        {
            return q.X <= Mathf.Max(p.X, r.X) && q.X >= Mathf.Min(p.X, r.X) &&
                   q.Y <= Mathf.Max(p.Y, r.Y) && q.Y >= Mathf.Min(p.Y, r.Y);
        }

        private int CalculateAdaptiveSamples(Curve3D curve)
        {
            // Calculate sample density based on curve complexity
            float totalLength = curve.GetBakedLength();
            int baseSamples = Mathf.Max(8, (int)(totalLength / _minCurveRadius));

            // Add more samples for curves with sharp turns
            float curvature = CalculateAverageCurvature(curve);
            int curvatureSamples = (int)(curvature * 50);

            return Mathf.Clamp(baseSamples + curvatureSamples, 8, 512);
        }

        private float CalculateAverageCurvature(Curve3D curve)
        {
            float totalCurvature = 0f;
            int samples = 20;

            for (int i = 0; i < samples - 2; i++)
            {
                float t1 = (float)i / samples;
                float t2 = (float)(i + 1) / samples;
                float t3 = (float)(i + 2) / samples;

                var p1 = curve.SampleBaked(t1 * curve.GetBakedLength());
                var p2 = curve.SampleBaked(t2 * curve.GetBakedLength());
                var p3 = curve.SampleBaked(t3 * curve.GetBakedLength());

                // Calculate curvature using three points
                var v1 = (p2 - p1).Normalized();
                var v2 = (p3 - p2).Normalized();

                float angle = Mathf.Abs(v1.AngleTo(v2));
                totalCurvature += angle;
            }

            return totalCurvature / samples;
        }

        private float CalculateFlowDirection(Vector2 start, Vector2 end)
        {
            // Calculate direction vector for flow/orientation
            var direction = (end - start).Normalized();
            return Mathf.Atan2(direction.Y, direction.X);
        }

        private byte[] GenerateCurveData(Curve curve, int resolution)
        {
            if (curve == null)
            {
                // Default linear curve
                curve = new Curve();
                curve.AddPoint(Vector2.Zero);
                curve.AddPoint(Vector2.One);
            }

            curve.Bake();
            float[] curveValues = new float[resolution];

            for (int i = 0; i < resolution; i++)
            {
                float t = (float)i / (resolution - 1);
                curveValues[i] = Mathf.Clamp(curve.SampleBaked(t), 0f, 1f);
            }

            // Add curve point count at the beginning
            var result = new List<byte>();
            result.AddRange(BitConverter.GetBytes(resolution));
            result.AddRange(GpuUtils.FloatArrayToBytes(curveValues));

            return result.ToArray();
        }

        // Debug
        private void DebugPrint(string message)
        {
            if (_debugMode)
            {
                GD.Print($"[PathLayer:{LayerName}] {message}");
            }
        }

        private void ErrorPrint(string message)
        {
            GD.PrintErr($"[PathLayer:{LayerName}] ERROR: {message}");
        }

        private void WarningPrint(string message)
        {
            GD.PushWarning($"[PathLayer:{LayerName}] WARNING: {message}");
        }
    }
}