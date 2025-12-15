// /Utils/CurveUtils.cs
using Godot;
using System;
using System.Collections.Generic;

namespace Terrain3DTools.Utils
{
    /// <summary>
    /// Utility class for creating and manipulating Godot Curve objects.
    /// Provides factory methods for common curve shapes used throughout the terrain tools.
    /// </summary>
    public static class CurveUtils
    {
        #region Basic Curves
        
        /// <summary>
        /// Creates a linear curve from (0,0) to (1,1).
        /// Output increases proportionally with input.
        /// </summary>
        public static Curve CreateLinearCurve()
        {
            var curve = new Curve();
            curve.AddPoint(Vector2.Zero);
            curve.AddPoint(Vector2.One);
            curve.BakeResolution = 64;
            return curve;
        }

        /// <summary>
        /// Creates a flat curve at value 1.0 across entire range.
        /// Useful for uniform effects with no falloff.
        /// </summary>
        public static Curve CreateFlatCurve(float value = 1.0f)
        {
            var curve = new Curve();
            curve.AddPoint(new Vector2(0, value));
            curve.AddPoint(new Vector2(1, value));
            curve.BakeResolution = 16;
            return curve;
        }

        /// <summary>
        /// Creates an inverted linear curve from (0,1) to (1,0).
        /// Output decreases as input increases.
        /// </summary>
        public static Curve CreateInverseLinearCurve()
        {
            var curve = new Curve();
            curve.AddPoint(new Vector2(0, 1));
            curve.AddPoint(new Vector2(1, 0));
            curve.BakeResolution = 64;
            return curve;
        }

        /// <summary>
        /// Creates a constant curve at the specified value.
        /// </summary>
        public static Curve CreateConstantCurve(float value)
        {
            var curve = new Curve();
            curve.AddPoint(new Vector2(0, value));
            curve.AddPoint(new Vector2(1, value));
            curve.BakeResolution = 16;
            return curve;
        }

        #endregion

        #region Ease Curves

        /// <summary>
        /// Creates an ease-in curve (slow start, fast end).
        /// Uses quadratic easing.
        /// </summary>
        public static Curve CreateEaseInCurve()
        {
            var curve = new Curve();
            curve.AddPoint(new Vector2(0, 0), 0, 0);
            curve.AddPoint(new Vector2(1, 1), 2, 0);
            curve.BakeResolution = 64;
            return curve;
        }

        /// <summary>
        /// Creates an ease-out curve (fast start, slow end).
        /// Ideal for falloff effects that taper smoothly.
        /// </summary>
        public static Curve CreateEaseOutCurve()
        {
            var curve = new Curve();
            curve.AddPoint(new Vector2(0, 1), 0, -2);
            curve.AddPoint(new Vector2(1, 0), -0.5f, 0);
            curve.BakeResolution = 64;
            return curve;
        }

        /// <summary>
        /// Creates an ease-in-out curve (slow start and end, fast middle).
        /// S-curve shape for smooth transitions.
        /// </summary>
        public static Curve CreateEaseInOutCurve()
        {
            var curve = new Curve();
            curve.AddPoint(new Vector2(0, 0), 0, 0);
            curve.AddPoint(new Vector2(0.5f, 0.5f), 1.5f, 1.5f);
            curve.AddPoint(new Vector2(1, 1), 0, 0);
            curve.BakeResolution = 64;
            return curve;
        }

        /// <summary>
        /// Creates an inverse ease-out curve (starts at 1, eases to 0).
        /// Good for strength falloff from center.
        /// </summary>
        public static Curve CreateInverseEaseOutCurve()
        {
            var curve = new Curve();
            curve.AddPoint(new Vector2(0, 1), 0, 0);
            curve.AddPoint(new Vector2(1, 0), -2, 0);
            curve.BakeResolution = 64;
            return curve;
        }

        #endregion

        #region Bell and Peak Curves

        /// <summary>
        /// Creates a bell curve (0 at edges, 1 at center).
        /// Ideal for embankments, ridges, and centered effects.
        /// </summary>
        public static Curve CreateBellCurve()
        {
            var curve = new Curve();
            curve.AddPoint(new Vector2(0, 0), 0, 2);
            curve.AddPoint(new Vector2(0.5f, 1.0f), 0, 0);
            curve.AddPoint(new Vector2(1, 0), -2, 0);
            curve.BakeResolution = 64;
            return curve;
        }

        /// <summary>
        /// Creates an asymmetric bell curve with adjustable peak position.
        /// </summary>
        public static Curve CreateBellCurve(float peakPosition, float peakValue = 1.0f)
        {
            peakPosition = Mathf.Clamp(peakPosition, 0.1f, 0.9f);
            var curve = new Curve();
            curve.AddPoint(new Vector2(0, 0), 0, 2);
            curve.AddPoint(new Vector2(peakPosition, peakValue), 0, 0);
            curve.AddPoint(new Vector2(1, 0), -2, 0);
            curve.BakeResolution = 64;
            return curve;
        }

        /// <summary>
        /// Creates a rounded top curve (high at start, smooth peak, high at end).
        /// Good for ridge crests.
        /// </summary>
        public static Curve CreateRoundedTopCurve()
        {
            var curve = new Curve();
            curve.AddPoint(new Vector2(0, 1), 0, 0.3f);
            curve.AddPoint(new Vector2(0.5f, 1.1f), 0, 0);
            curve.AddPoint(new Vector2(1, 1), -0.3f, 0);
            curve.BakeResolution = 64;
            return curve;
        }

        /// <summary>
        /// Creates a rim/lip curve for embankment edges.
        /// Rises to a peak then falls back down.
        /// </summary>
        public static Curve CreateRimCurve()
        {
            var curve = new Curve();
            curve.AddPoint(new Vector2(0, 0), 0, 3);
            curve.AddPoint(new Vector2(0.3f, 1), 0, 0);
            curve.AddPoint(new Vector2(0.7f, 1), 0, 0);
            curve.AddPoint(new Vector2(1, 0), -3, 0);
            curve.BakeResolution = 64;
            return curve;
        }

        #endregion

        #region Slope Curves

        /// <summary>
        /// Creates a slope-down curve (1 at start, decreasing to target at end).
        /// Good for shoulders and transitions.
        /// </summary>
        public static Curve CreateSlopeDownCurve(float endValue = 0.3f)
        {
            var curve = new Curve();
            curve.AddPoint(new Vector2(0, 1));
            curve.AddPoint(new Vector2(1, endValue));
            curve.BakeResolution = 64;
            return curve;
        }

        /// <summary>
        /// Creates a slope-up curve (0 at start, increasing to 1 at end).
        /// Good for banks climbing from channels.
        /// </summary>
        public static Curve CreateSlopeUpCurve(float startValue = 0f)
        {
            var curve = new Curve();
            curve.AddPoint(new Vector2(0, startValue));
            curve.AddPoint(new Vector2(1, 1));
            curve.BakeResolution = 64;
            return curve;
        }

        /// <summary>
        /// Creates a steep slope curve with quick transition.
        /// Good for embankment sides.
        /// </summary>
        public static Curve CreateSteepSlopeCurve()
        {
            var curve = new Curve();
            curve.AddPoint(new Vector2(0, 1), 0, -3);
            curve.AddPoint(new Vector2(0.3f, 0.5f));
            curve.AddPoint(new Vector2(1, 0), -0.5f, 0);
            curve.BakeResolution = 64;
            return curve;
        }

        /// <summary>
        /// Alias for CreateSteepSlopeCurve for API consistency.
        /// </summary>
        public static Curve CreateSteepCurve() => CreateSteepSlopeCurve();

        /// <summary>
        /// Creates a gentle slope curve with gradual transition.
        /// </summary>
        public static Curve CreateGentleSlopeCurve()
        {
            var curve = new Curve();
            curve.AddPoint(new Vector2(0, 1), 0, -0.5f);
            curve.AddPoint(new Vector2(0.5f, 0.5f));
            curve.AddPoint(new Vector2(1, 0), -0.5f, 0);
            curve.BakeResolution = 64;
            return curve;
        }

        #endregion

        #region Path Profile Curves

        /// <summary>
        /// Creates a flat curve with slight camber (center higher than edges).
        /// Good for road surfaces that shed water.
        /// </summary>
        public static Curve CreateFlatWithCamberCurve(float camber = 0.02f)
        {
            var curve = new Curve();
            curve.AddPoint(new Vector2(0, 1));
            curve.AddPoint(new Vector2(0.5f, 1 + camber));
            curve.AddPoint(new Vector2(1, 1));
            curve.BakeResolution = 64;
            return curve;
        }

        /// <summary>
        /// Creates a bank/slope curve for river banks.
        /// Slow start, accelerating toward the top.
        /// </summary>
        public static Curve CreateBankCurve()
        {
            var curve = new Curve();
            curve.AddPoint(new Vector2(0, 0), 0, 0.5f);
            curve.AddPoint(new Vector2(0.7f, 0.8f), 1.5f, 1.5f);
            curve.AddPoint(new Vector2(1, 1), 0.5f, 0);
            curve.BakeResolution = 64;
            return curve;
        }

        /// <summary>
        /// Creates a river bed curve (slight V-shape for natural channels).
        /// </summary>
        public static Curve CreateRiverBedCurve()
        {
            var curve = new Curve();
            curve.AddPoint(new Vector2(0, 1), 0, -0.3f);
            curve.AddPoint(new Vector2(0.5f, 0.95f), 0, 0);
            curve.AddPoint(new Vector2(1, 1), 0.3f, 0);
            curve.BakeResolution = 64;
            return curve;
        }

        /// <summary>
        /// Creates a V-shape curve for narrow channels and streams.
        /// Deepest at center, rising to edges.
        /// </summary>
        public static Curve CreateVShapeCurve()
        {
            var curve = new Curve();
            curve.AddPoint(new Vector2(0, 0.5f), 0, 1);
            curve.AddPoint(new Vector2(0.5f, 1), 0, 0);
            curve.AddPoint(new Vector2(1, 0.5f), -1, 0);
            curve.BakeResolution = 64;
            return curve;
        }

        /// <summary>
        /// Creates a U-shape curve for wider channels.
        /// Flat bottom with curved sides.
        /// </summary>
        public static Curve CreateUShapeCurve()
        {
            var curve = new Curve();
            curve.AddPoint(new Vector2(0, 0), 0, 3);
            curve.AddPoint(new Vector2(0.2f, 0.8f));
            curve.AddPoint(new Vector2(0.5f, 1), 0, 0);
            curve.AddPoint(new Vector2(0.8f, 0.8f));
            curve.AddPoint(new Vector2(1, 0), -3, 0);
            curve.BakeResolution = 64;
            return curve;
        }

        /// <summary>
        /// Creates a vertical wall curve (near-vertical transition).
        /// Good for trench walls and canal sides.
        /// </summary>
        public static Curve CreateVerticalWallCurve()
        {
            var curve = new Curve();
            curve.AddPoint(new Vector2(0, 0), 0, 10);
            curve.AddPoint(new Vector2(0.1f, 0.9f), 1, 1);
            curve.AddPoint(new Vector2(1, 1), 0.1f, 0);
            curve.BakeResolution = 64;
            return curve;
        }

        /// <summary>
        /// Creates a shelf curve (flat area then drop-off).
        /// Good for ledges and terraces.
        /// </summary>
        public static Curve CreateShelfCurve(float shelfWidth = 0.4f)
        {
            shelfWidth = Mathf.Clamp(shelfWidth, 0.1f, 0.8f);
            var curve = new Curve();
            curve.AddPoint(new Vector2(0, 1));
            curve.AddPoint(new Vector2(shelfWidth, 1));
            curve.AddPoint(new Vector2(shelfWidth + 0.1f, 0.5f), -5, -5);
            curve.AddPoint(new Vector2(1, 0));
            curve.BakeResolution = 64;
            return curve;
        }

        #endregion

        #region Noise and Variation Curves

        /// <summary>
        /// Creates a curve that emphasizes middle values (compresses extremes).
        /// Good for softening noise effects.
        /// </summary>
        public static Curve CreateSoftenCurve()
        {
            var curve = new Curve();
            curve.AddPoint(new Vector2(0, 0.2f));
            curve.AddPoint(new Vector2(0.5f, 0.5f));
            curve.AddPoint(new Vector2(1, 0.8f));
            curve.BakeResolution = 64;
            return curve;
        }

        /// <summary>
        /// Creates a curve that emphasizes extreme values (expands from center).
        /// Good for increasing contrast in noise.
        /// </summary>
        public static Curve CreateContrastCurve()
        {
            var curve = new Curve();
            curve.AddPoint(new Vector2(0, 0), 0, 0.5f);
            curve.AddPoint(new Vector2(0.5f, 0.5f), 2, 2);
            curve.AddPoint(new Vector2(1, 1), 0.5f, 0);
            curve.BakeResolution = 64;
            return curve;
        }

        /// <summary>
        /// Creates a step curve with specified number of steps.
        /// Good for terracing effects.
        /// </summary>
        public static Curve CreateStepCurve(int steps = 4)
        {
            steps = Mathf.Clamp(steps, 2, 16);
            var curve = new Curve();
            
            float stepSize = 1.0f / steps;
            
            for (int i = 0; i <= steps; i++)
            {
                float x = i * stepSize;
                float y = i * stepSize;
                
                if (i > 0)
                {
                    // Add point just before step (same Y as previous)
                    curve.AddPoint(new Vector2(x - 0.001f, (i - 1) * stepSize));
                }
                
                curve.AddPoint(new Vector2(x, y));
            }
            
            curve.BakeResolution = 128;
            return curve;
        }

        #endregion

        #region Utility Methods

        /// <summary>
        /// Creates a curve from an array of Y values (evenly spaced X).
        /// </summary>
        public static Curve CreateFromValues(float[] values)
        {
            if (values == null || values.Length < 2)
            {
                return CreateLinearCurve();
            }

            var curve = new Curve();
            float step = 1.0f / (values.Length - 1);

            for (int i = 0; i < values.Length; i++)
            {
                curve.AddPoint(new Vector2(i * step, values[i]));
            }

            curve.BakeResolution = Mathf.Max(64, values.Length * 4);
            return curve;
        }

        /// <summary>
        /// Creates a curve from a list of Vector2 points.
        /// Points should be sorted by X value.
        /// </summary>
        public static Curve CreateFromPoints(IEnumerable<Vector2> points)
        {
            var curve = new Curve();

            foreach (var point in points)
            {
                curve.AddPoint(point);
            }

            if (curve.PointCount < 2)
            {
                curve.AddPoint(Vector2.Zero);
                curve.AddPoint(Vector2.One);
            }

            curve.BakeResolution = 64;
            return curve;
        }

        /// <summary>
        /// Inverts a curve vertically (1 - y for all points).
        /// </summary>
        public static Curve InvertCurve(Curve source)
        {
            if (source == null) return CreateLinearCurve();

            var curve = new Curve();

            for (int i = 0; i < source.PointCount; i++)
            {
                Vector2 point = source.GetPointPosition(i);
                float leftTangent = -source.GetPointLeftTangent(i);
                float rightTangent = -source.GetPointRightTangent(i);

                curve.AddPoint(
                    new Vector2(point.X, 1.0f - point.Y),
                    leftTangent,
                    rightTangent
                );
            }

            curve.BakeResolution = source.BakeResolution;
            return curve;
        }

        /// <summary>
        /// Reverses a curve horizontally (mirrors along X = 0.5).
        /// </summary>
        public static Curve ReverseCurve(Curve source)
        {
            if (source == null) return CreateLinearCurve();

            var curve = new Curve();
            var points = new List<(Vector2 pos, float left, float right)>();

            // Collect points in reverse order
            for (int i = source.PointCount - 1; i >= 0; i--)
            {
                Vector2 point = source.GetPointPosition(i);
                float leftTangent = -source.GetPointRightTangent(i);
                float rightTangent = -source.GetPointLeftTangent(i);

                points.Add((new Vector2(1.0f - point.X, point.Y), leftTangent, rightTangent));
            }

            foreach (var (pos, left, right) in points)
            {
                curve.AddPoint(pos, left, right);
            }

            curve.BakeResolution = source.BakeResolution;
            return curve;
        }

        /// <summary>
        /// Scales curve output values by a multiplier.
        /// </summary>
        public static Curve ScaleCurve(Curve source, float scale)
        {
            if (source == null) return CreateLinearCurve();

            var curve = new Curve();

            for (int i = 0; i < source.PointCount; i++)
            {
                Vector2 point = source.GetPointPosition(i);
                float leftTangent = source.GetPointLeftTangent(i) * scale;
                float rightTangent = source.GetPointRightTangent(i) * scale;

                curve.AddPoint(
                    new Vector2(point.X, point.Y * scale),
                    leftTangent,
                    rightTangent
                );
            }

            curve.BakeResolution = source.BakeResolution;
            return curve;
        }

        /// <summary>
        /// Offsets curve output values by an amount.
        /// </summary>
        public static Curve OffsetCurve(Curve source, float offset)
        {
            if (source == null) return CreateLinearCurve();

            var curve = new Curve();

            for (int i = 0; i < source.PointCount; i++)
            {
                Vector2 point = source.GetPointPosition(i);
                float leftTangent = source.GetPointLeftTangent(i);
                float rightTangent = source.GetPointRightTangent(i);

                curve.AddPoint(
                    new Vector2(point.X, point.Y + offset),
                    leftTangent,
                    rightTangent
                );
            }

            curve.BakeResolution = source.BakeResolution;
            return curve;
        }

        /// <summary>
        /// Combines two curves by multiplying their values.
        /// </summary>
        public static Curve MultiplyCurves(Curve a, Curve b, int resolution = 64)
        {
            if (a == null) return b ?? CreateLinearCurve();
            if (b == null) return a;

            a.Bake();
            b.Bake();

            var values = new float[resolution];
            for (int i = 0; i < resolution; i++)
            {
                float t = (float)i / (resolution - 1);
                values[i] = a.SampleBaked(t) * b.SampleBaked(t);
            }

            return CreateFromValues(values);
        }

        /// <summary>
        /// Combines two curves by adding their values.
        /// </summary>
        public static Curve AddCurves(Curve a, Curve b, int resolution = 64)
        {
            if (a == null) return b ?? CreateLinearCurve();
            if (b == null) return a;

            a.Bake();
            b.Bake();

            var values = new float[resolution];
            for (int i = 0; i < resolution; i++)
            {
                float t = (float)i / (resolution - 1);
                values[i] = a.SampleBaked(t) + b.SampleBaked(t);
            }

            return CreateFromValues(values);
        }

        /// <summary>
        /// Blends between two curves based on a blend factor.
        /// </summary>
        public static Curve BlendCurves(Curve a, Curve b, float blend, int resolution = 64)
        {
            if (a == null) return b ?? CreateLinearCurve();
            if (b == null) return a;

            blend = Mathf.Clamp(blend, 0f, 1f);
            a.Bake();
            b.Bake();

            var values = new float[resolution];
            for (int i = 0; i < resolution; i++)
            {
                float t = (float)i / (resolution - 1);
                values[i] = Mathf.Lerp(a.SampleBaked(t), b.SampleBaked(t), blend);
            }

            return CreateFromValues(values);
        }

        /// <summary>
        /// Remaps input range [inMin, inMax] to [0, 1] before sampling curve.
        /// </summary>
        public static float SampleRemapped(Curve curve, float value, float inMin, float inMax)
        {
            if (curve == null) return value;
            
            float t = Mathf.Clamp((value - inMin) / (inMax - inMin), 0f, 1f);
            return curve.SampleBaked(t);
        }

        /// <summary>
        /// Samples a curve and remaps output from [0, 1] to [outMin, outMax].
        /// </summary>
        public static float SampleWithOutputRange(Curve curve, float t, float outMin, float outMax)
        {
            if (curve == null) return Mathf.Lerp(outMin, outMax, t);
            
            float sample = curve.SampleBaked(Mathf.Clamp(t, 0f, 1f));
            return Mathf.Lerp(outMin, outMax, sample);
        }

        /// <summary>
        /// Bakes a curve to a float array for GPU upload.
        /// </summary>
        public static float[] BakeToArray(Curve curve, int resolution = 64)
        {
            if (curve == null)
            {
                var linear = new float[resolution];
                for (int i = 0; i < resolution; i++)
                {
                    linear[i] = (float)i / (resolution - 1);
                }
                return linear;
            }

            curve.Bake();
            var values = new float[resolution];

            for (int i = 0; i < resolution; i++)
            {
                float t = (float)i / (resolution - 1);
                values[i] = curve.SampleBaked(t);
            }

            return values;
        }

        /// <summary>
        /// Bakes a curve to a byte array for GPU upload.
        /// </summary>
        public static byte[] BakeToBytes(Curve curve, int resolution = 64)
        {
            float[] values = BakeToArray(curve, resolution);
            byte[] bytes = new byte[values.Length * sizeof(float)];
            Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
            return bytes;
        }

        /// <summary>
        /// Validates and repairs a curve if needed.
        /// Ensures curve has at least 2 points and is properly ordered.
        /// </summary>
        public static Curve ValidateOrRepair(Curve curve)
        {
            if (curve == null)
            {
                return CreateLinearCurve();
            }

            if (curve.PointCount < 2)
            {
                return CreateLinearCurve();
            }

            // Check if points are in order
            bool needsRepair = false;
            float lastX = -1f;

            for (int i = 0; i < curve.PointCount; i++)
            {
                float x = curve.GetPointPosition(i).X;
                if (x <= lastX)
                {
                    needsRepair = true;
                    break;
                }
                lastX = x;
            }

            if (!needsRepair)
            {
                return curve;
            }

            // Rebuild curve from baked values
            curve.Bake();
            return CreateFromValues(BakeToArray(curve, 64));
        }

        #endregion

        #region Preset Collections

        /// <summary>
        /// Gets a curve by name (useful for serialization or UI).
        /// </summary>
        public static Curve GetPresetByName(string name)
        {
            return name?.ToLower() switch
            {
                "linear" => CreateLinearCurve(),
                "flat" => CreateFlatCurve(),
                "inverse" or "inverselinear" => CreateInverseLinearCurve(),
                "easein" => CreateEaseInCurve(),
                "easeout" => CreateEaseOutCurve(),
                "easeinout" or "smooth" => CreateEaseInOutCurve(),
                "bell" => CreateBellCurve(),
                "rim" => CreateRimCurve(),
                "roundedtop" => CreateRoundedTopCurve(),
                "slopedown" => CreateSlopeDownCurve(),
                "slopeup" => CreateSlopeUpCurve(),
                "steep" or "steepslope" => CreateSteepSlopeCurve(),
                "gentle" or "gentleslope" => CreateGentleSlopeCurve(),
                "bank" => CreateBankCurve(),
                "riverbed" => CreateRiverBedCurve(),
                "vshape" => CreateVShapeCurve(),
                "ushape" => CreateUShapeCurve(),
                "verticalwall" or "wall" => CreateVerticalWallCurve(),
                "shelf" => CreateShelfCurve(),
                "step" or "terrace" => CreateStepCurve(),
                "contrast" => CreateContrastCurve(),
                "soften" => CreateSoftenCurve(),
                _ => CreateLinearCurve()
            };
        }

        /// <summary>
        /// Gets all available preset names.
        /// </summary>
        public static string[] GetPresetNames()
        {
            return new[]
            {
                "Linear",
                "Flat",
                "Inverse",
                "EaseIn",
                "EaseOut",
                "EaseInOut",
                "Bell",
                "Rim",
                "RoundedTop",
                "SlopeDown",
                "SlopeUp",
                "Steep",
                "Gentle",
                "Bank",
                "RiverBed",
                "VShape",
                "UShape",
                "VerticalWall",
                "Shelf",
                "Step",
                "Contrast",
                "Soften"
            };
        }

        #endregion
    }
}