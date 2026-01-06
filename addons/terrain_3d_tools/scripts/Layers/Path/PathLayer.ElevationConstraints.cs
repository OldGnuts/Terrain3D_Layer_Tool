// PathLayer.ElevationConstraints.cs
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using Terrain3DTools.Core.Debug;

namespace Terrain3DTools.Layers
{
    /// <summary>
    /// Elevation constraint checking and enforcement for PathLayer.
    /// Handles grade limits for roads/trails and downhill-only constraints for waterways.
    /// </summary>
    public partial class PathLayer
    {
        #region Constants

        private const float HEIGHT_TOLERANCE = 0.001f;
        private const float MIN_HORIZONTAL_SEGMENT = 0.1f;
        private const float SWITCHBACK_MIN_LENGTH = 8.0f;
        private const int MAX_ENFORCEMENT_ITERATIONS = 20;

        #endregion

        #region Constraint Data Structures

        /// <summary>
        /// Information about a grade violation between two curve points.
        /// </summary>
        public struct GradeViolation
        {
            public int StartPointIndex;
            public int EndPointIndex;
            public float ActualGradePercent;
            public float MaxAllowedGrade;
            public float ExcessGrade;
            public float HorizontalDistance;
            public float VerticalDistance;

            public override string ToString() =>
                $"Segment {StartPointIndex}→{EndPointIndex}: {ActualGradePercent:F1}% " +
                $"(max {MaxAllowedGrade:F1}%, excess {ExcessGrade:F1}%)";
        }

        /// <summary>
        /// Information about a downhill violation (water flowing uphill).
        /// </summary>
        public struct DownhillViolation
        {
            public int PointIndex;
            public float PointHeight;
            public float PreviousHeight;
            public float HeightIncrease;

            public override string ToString() =>
                $"Point {PointIndex}: rises {HeightIncrease:F2}m (from {PreviousHeight:F2}m to {PointHeight:F2}m)";
        }

        /// <summary>
        /// Result of an enforcement operation.
        /// </summary>
        public struct EnforcementResult
        {
            public bool Success;
            public int PointsModified;
            public int PointsAdded;
            public string Message;
            public List<string> Details;

            public static EnforcementResult NoChanges() => new()
            {
                Success = true,
                PointsModified = 0,
                PointsAdded = 0,
                Message = "No violations found - no changes needed.",
                Details = new List<string>()
            };

            public static EnforcementResult Modified(int modified, int added = 0, List<string> details = null) => new()
            {
                Success = true,
                PointsModified = modified,
                PointsAdded = added,
                Message = added > 0
                    ? $"Adjusted {modified} point(s) and added {added} switchback point(s)."
                    : $"Adjusted {modified} point(s).",
                Details = details ?? new List<string>()
            };

            public static EnforcementResult Failed(string reason, List<string> details = null) => new()
            {
                Success = false,
                PointsModified = 0,
                PointsAdded = 0,
                Message = reason,
                Details = details ?? new List<string>()
            };
        }

        /// <summary>
        /// Describes the feasibility analysis for grade constraints.
        /// </summary>
        public struct FeasibilityAnalysis
        {
            public bool IsFeasible;
            public bool RequiresSwitchbacks;
            public float RequiredGradePercent;
            public float TotalHorizontalDistance;
            public float TotalVerticalDistance;
            public float AdditionalHorizontalNeeded;
            public int SuggestedSwitchbacks;
            public string Summary;
            public List<string> Details;
        }

        /// <summary>
        /// Internal structure for tracking point geometry during enforcement.
        /// </summary>
        private struct PointGeometry
        {
            public Vector3 Position;
            public float OriginalY;
            public Vector3 HandleIn;
            public Vector3 HandleOut;
            public float HDistToNext;
        }

        #endregion

        #region Constraint Violation Checking

        /// <summary>
        /// Checks all curve segments for grade violations.
        /// </summary>
        public List<GradeViolation> GetGradeViolations()
        {
            var violations = new List<GradeViolation>();
            var curve = Curve;

            if (curve == null || curve.PointCount < 2 || !_enableGradeConstraint)
                return violations;

            for (int i = 0; i < curve.PointCount - 1; i++)
            {
                Vector3 p1 = curve.GetPointPosition(i);
                Vector3 p2 = curve.GetPointPosition(i + 1);

                float horizontalDist = new Vector2(p2.X - p1.X, p2.Z - p1.Z).Length();
                float verticalDist = p2.Y - p1.Y;
                float absVerticalDist = Mathf.Abs(verticalDist);

                if (horizontalDist < MIN_HORIZONTAL_SEGMENT) continue;

                float gradePercent = (absVerticalDist / horizontalDist) * 100f;

                if (gradePercent > _maxGradePercent + 0.01f)
                {
                    violations.Add(new GradeViolation
                    {
                        StartPointIndex = i,
                        EndPointIndex = i + 1,
                        ActualGradePercent = gradePercent,
                        MaxAllowedGrade = _maxGradePercent,
                        ExcessGrade = gradePercent - _maxGradePercent,
                        HorizontalDistance = horizontalDist,
                        VerticalDistance = verticalDist
                    });
                }
            }

            return violations;
        }

        /// <summary>
        /// Checks all curve points for downhill violations.
        /// </summary>
        public List<DownhillViolation> GetDownhillViolations()
        {
            var violations = new List<DownhillViolation>();
            var curve = Curve;

            if (curve == null || curve.PointCount < 2 || !_enableDownhillConstraint)
                return violations;

            bool flowIsForward = IsFlowDirectionForward();

            if (flowIsForward)
            {
                float previousHeight = curve.GetPointPosition(0).Y;
                for (int i = 1; i < curve.PointCount; i++)
                {
                    float currentHeight = curve.GetPointPosition(i).Y;
                    if (currentHeight > previousHeight + HEIGHT_TOLERANCE)
                    {
                        violations.Add(new DownhillViolation
                        {
                            PointIndex = i,
                            PointHeight = currentHeight,
                            PreviousHeight = previousHeight,
                            HeightIncrease = currentHeight - previousHeight
                        });
                    }
                    previousHeight = currentHeight;
                }
            }
            else
            {
                float previousHeight = curve.GetPointPosition(curve.PointCount - 1).Y;
                for (int i = curve.PointCount - 2; i >= 0; i--)
                {
                    float currentHeight = curve.GetPointPosition(i).Y;
                    if (currentHeight > previousHeight + HEIGHT_TOLERANCE)
                    {
                        violations.Add(new DownhillViolation
                        {
                            PointIndex = i,
                            PointHeight = currentHeight,
                            PreviousHeight = previousHeight,
                            HeightIncrease = currentHeight - previousHeight
                        });
                    }
                    previousHeight = currentHeight;
                }
            }

            return violations;
        }

        /// <summary>
        /// Returns true if the detected flow direction is forward (first point is source/highest).
        /// </summary>
        public bool IsFlowDirectionForward()
        {
            var curve = Curve;
            if (curve == null || curve.PointCount < 2) return true;

            Vector3 firstPoint = curve.GetPointPosition(0);
            Vector3 lastPoint = curve.GetPointPosition(curve.PointCount - 1);

            return firstPoint.Y >= lastPoint.Y;
        }

        /// <summary>
        /// Gets a summary string describing current constraint status.
        /// </summary>
        public string GetConstraintStatusSummary()
        {
            var parts = new List<string>();

            if (_enableGradeConstraint)
            {
                var gradeViolations = GetGradeViolations();
                if (gradeViolations.Count > 0)
                {
                    var analysis = AnalyzeGradeFeasibility();
                    if (analysis.RequiresSwitchbacks && !_allowSwitchbackGeneration)
                        parts.Add($"⚠️ {gradeViolations.Count} grade violation(s) - needs switchbacks (disabled)");
                    else if (analysis.RequiresSwitchbacks)
                        parts.Add($"⚠️ {gradeViolations.Count} grade violation(s) - will add switchbacks");
                    else
                        parts.Add($"⚠️ {gradeViolations.Count} grade violation(s) - fixable with height adjustment");
                }
                else
                {
                    parts.Add($"✓ Grade OK (max {_maxGradePercent}%)");
                }
            }

            if (_enableDownhillConstraint)
            {
                var downhillViolations = GetDownhillViolations();
                string flowDir = IsFlowDirectionForward() ? "→" : "←";
                if (downhillViolations.Count > 0)
                    parts.Add($"⚠️ {downhillViolations.Count} uphill violation(s) [flow {flowDir}]");
                else
                    parts.Add($"✓ Downhill OK [flow {flowDir}]");
            }

            return parts.Count > 0 ? string.Join(" | ", parts) : "No constraints enabled";
        }

        #endregion

        #region Feasibility Analysis

        /// <summary>
        /// Analyzes whether grade constraints can be satisfied with current settings.
        /// </summary>
        public FeasibilityAnalysis AnalyzeGradeFeasibility()
        {
            var curve = Curve;
            var analysis = new FeasibilityAnalysis
            {
                IsFeasible = true,
                RequiresSwitchbacks = false,
                Details = new List<string>()
            };

            if (curve == null || curve.PointCount < 2)
            {
                analysis.Summary = "No valid curve";
                analysis.IsFeasible = false;
                return analysis;
            }

            var violations = GetGradeViolations();
            if (violations.Count == 0)
            {
                analysis.Summary = $"All segments within {_maxGradePercent}% grade";
                return analysis;
            }

            float maxSlope = _maxGradePercent / 100f;

            // Gather geometry
            var geometry = GatherGeometry(curve);
            float totalHDist = 0f;
            for (int i = 0; i < geometry.Length - 1; i++)
                totalHDist += geometry[i].HDistToNext;

            float startY = geometry[0].OriginalY;
            float endY = geometry[geometry.Length - 1].OriginalY;
            float endpointVDist = Mathf.Abs(endY - startY);

            analysis.TotalHorizontalDistance = totalHDist;
            analysis.TotalVerticalDistance = endpointVDist;
            analysis.RequiredGradePercent = totalHDist > 0 ? (endpointVDist / totalHDist) * 100f : 0;

            // Check if height-only adjustment is possible
            // This requires that the overall grade from start to end is achievable
            bool heightOnlyPossible = analysis.RequiredGradePercent <= _maxGradePercent;

            if (heightOnlyPossible)
            {
                // Verify each segment can be solved
                var (feasibleMin, feasibleMax) = ComputeFeasibilityTube(geometry, maxSlope);
                
                for (int i = 0; i < geometry.Length; i++)
                {
                    if (feasibleMin[i] > feasibleMax[i] + HEIGHT_TOLERANCE)
                    {
                        heightOnlyPossible = false;
                        analysis.Details.Add($"Point {i}: no valid height in range [{feasibleMin[i]:F1}, {feasibleMax[i]:F1}]");
                        break;
                    }
                }
            }

            if (heightOnlyPossible)
            {
                analysis.IsFeasible = true;
                analysis.RequiresSwitchbacks = false;
                analysis.Summary = $"Solvable by adjusting point heights (avg grade: {analysis.RequiredGradePercent:F1}%)";
                return analysis;
            }

            // Height-only won't work - calculate switchback requirements
            analysis.RequiresSwitchbacks = true;

            float totalAdditionalHDist = 0f;
            int totalPointsNeeded = 0;

            foreach (var v in violations)
            {
                float requiredHDist = Mathf.Abs(v.VerticalDistance) / maxSlope;
                float additionalNeeded = Mathf.Max(0, requiredHDist - v.HorizontalDistance);
                
                if (additionalNeeded > 0)
                {
                    totalAdditionalHDist += additionalNeeded;
                    int pointsForSegment = CalculateSwitchbackPointsNeeded(v.HorizontalDistance, Mathf.Abs(v.VerticalDistance), maxSlope);
                    totalPointsNeeded += pointsForSegment;
                    
                    analysis.Details.Add($"Seg {v.StartPointIndex}→{v.EndPointIndex}: {v.ActualGradePercent:F1}% → " +
                        $"needs +{additionalNeeded:F1}m horizontal ({pointsForSegment} pts)");
                }
            }

            analysis.AdditionalHorizontalNeeded = totalAdditionalHDist;
            analysis.SuggestedSwitchbacks = totalPointsNeeded;

            // Check if switchbacks are allowed and within limits
            if (!_allowSwitchbackGeneration)
            {
                analysis.IsFeasible = false;
                analysis.Summary = $"Needs {totalPointsNeeded} switchback points, but switchback generation is disabled. " +
                                  $"Enable switchbacks or increase max grade to ≥{violations.Max(v => v.ActualGradePercent):F0}%.";
            }
            else if (totalPointsNeeded > _maxSwitchbackPoints)
            {
                analysis.IsFeasible = false;
                analysis.Summary = $"Needs {totalPointsNeeded} switchback points (max allowed: {_maxSwitchbackPoints}). " +
                                  $"Increase limit or max grade.";
            }
            else
            {
                analysis.IsFeasible = true;
                analysis.Summary = $"Will add {totalPointsNeeded} switchback point(s) " +
                                  $"(+{totalAdditionalHDist:F1}m horizontal distance)";
            }

            return analysis;
        }

        /// <summary>
        /// Computes the feasibility tube - valid height range at each point.
        /// </summary>
        private (float[] min, float[] max) ComputeFeasibilityTube(PointGeometry[] geometry, float maxSlope)
        {
            int n = geometry.Length;

            float[] minFromStart = new float[n];
            float[] maxFromStart = new float[n];
            float[] minFromEnd = new float[n];
            float[] maxFromEnd = new float[n];

            // Forward pass - what heights are reachable from start?
            minFromStart[0] = geometry[0].OriginalY;
            maxFromStart[0] = geometry[0].OriginalY;

            for (int i = 1; i < n; i++)
            {
                float hDist = geometry[i - 1].HDistToNext;
                float maxRise = hDist * maxSlope;
                minFromStart[i] = minFromStart[i - 1] - maxRise;
                maxFromStart[i] = maxFromStart[i - 1] + maxRise;
            }

            // Backward pass - what heights can reach the end?
            minFromEnd[n - 1] = geometry[n - 1].OriginalY;
            maxFromEnd[n - 1] = geometry[n - 1].OriginalY;

            for (int i = n - 2; i >= 0; i--)
            {
                float hDist = geometry[i].HDistToNext;
                float maxRise = hDist * maxSlope;
                minFromEnd[i] = minFromEnd[i + 1] - maxRise;
                maxFromEnd[i] = maxFromEnd[i + 1] + maxRise;
            }

            // Intersection
            float[] feasibleMin = new float[n];
            float[] feasibleMax = new float[n];

            for (int i = 0; i < n; i++)
            {
                feasibleMin[i] = Mathf.Max(minFromStart[i], minFromEnd[i]);
                feasibleMax[i] = Mathf.Min(maxFromStart[i], maxFromEnd[i]);
            }

            return (feasibleMin, feasibleMax);
        }

        #endregion

        #region Grade Constraint Enforcement

        /// <summary>
        /// Enforces grade constraints by adjusting heights and optionally adding switchbacks.
        /// </summary>
        public EnforcementResult EnforceGradeConstraints(int maxIterations = 10)
        {
            var curve = Curve;
            if (curve == null || curve.PointCount < 2)
                return EnforcementResult.Failed("No valid curve to process.");

            if (!_enableGradeConstraint)
                return EnforcementResult.Failed("Grade constraint is not enabled.");

            var initialViolations = GetGradeViolations();
            if (initialViolations.Count == 0)
                return EnforcementResult.NoChanges();

            var analysis = AnalyzeGradeFeasibility();
            var details = new List<string>();

            DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.LayerLifecycle,
                $"EnforceGradeConstraints: {initialViolations.Count} violations, " +
                $"requiresSwitchbacks={analysis.RequiresSwitchbacks}, " +
                $"allowSwitchbacks={_allowSwitchbackGeneration}");

            int totalPointsAdded = 0;
            int totalPointsModified = 0;

            // If switchbacks needed and allowed, add them first
            if (analysis.RequiresSwitchbacks && _allowSwitchbackGeneration)
            {
                int pointsToAdd = Mathf.Min(analysis.SuggestedSwitchbacks, _maxSwitchbackPoints);
                details.Add($"Adding up to {pointsToAdd} switchback points...");

                totalPointsAdded = AddSwitchbacksToViolatingSegments(curve, pointsToAdd, details);
                
                if (totalPointsAdded > 0)
                {
                    InvalidateCaches();
                }
            }

            // Now adjust heights for remaining/all violations
            totalPointsModified = AdjustHeightsForGrade(curve, details);

            if (totalPointsModified > 0 || totalPointsAdded > 0)
            {
                InvalidateCaches();

                if (_autoSmoothCurve)
                {
                    AutoSmoothCurvePoints(_autoSmoothTension);
                }
            }

            // Final check
            var remainingViolations = GetGradeViolations();

            // Build result message
            string message;
            bool success = remainingViolations.Count == 0;

            if (success)
            {
                message = $"Successfully enforced {_maxGradePercent}% grade limit.";
                if (totalPointsAdded > 0)
                    message += $" Added {totalPointsAdded} switchback point(s).";
                if (totalPointsModified > 0)
                    message += $" Adjusted {totalPointsModified} point height(s).";
            }
            else
            {
                float worstRemaining = remainingViolations.Max(v => v.ActualGradePercent);
                message = $"{remainingViolations.Count} violation(s) remain (worst: {worstRemaining:F1}%). ";

                if (analysis.RequiresSwitchbacks && !_allowSwitchbackGeneration)
                {
                    message += "Enable switchback generation to fix, or increase max grade.";
                }
                else if (analysis.RequiresSwitchbacks && totalPointsAdded >= _maxSwitchbackPoints)
                {
                    message += $"Reached switchback limit ({_maxSwitchbackPoints}). Increase limit or max grade.";
                }
                else
                {
                    message += "Consider increasing max grade or adjusting path manually.";
                }
            }

            return new EnforcementResult
            {
                Success = success,
                PointsModified = totalPointsModified,
                PointsAdded = totalPointsAdded,
                Message = message,
                Details = details
            };
        }

        /// <summary>
        /// Adds switchback points to violating segments.
        /// Returns number of points added.
        /// </summary>
        private int AddSwitchbacksToViolatingSegments(Curve3D curve, int maxPointsToAdd, List<string> details)
        {
            float maxSlope = _maxGradePercent / 100f;
            int totalAdded = 0;

            for (int iteration = 0; iteration < MAX_ENFORCEMENT_ITERATIONS && totalAdded < maxPointsToAdd; iteration++)
            {
                var violations = GetGradeViolations();
                if (violations.Count == 0) break;

                // Process worst violation first
                var worst = violations.OrderByDescending(v => v.ExcessGrade).First();

                int pointsNeeded = CalculateSwitchbackPointsNeeded(
                    worst.HorizontalDistance, 
                    Mathf.Abs(worst.VerticalDistance), 
                    maxSlope
                );

                // Limit points for this segment
                int pointsToAddHere = Mathf.Min(pointsNeeded, maxPointsToAdd - totalAdded);
                if (pointsToAddHere <= 0) break;

                Vector3 p1 = curve.GetPointPosition(worst.StartPointIndex);
                Vector3 p2 = curve.GetPointPosition(worst.EndPointIndex);

                var newPoints = GenerateSwitchbackPoints(p1, p2, pointsToAddHere, maxSlope);

                if (newPoints.Count > 0)
                {
                    details.Add($"Seg {worst.StartPointIndex}→{worst.EndPointIndex}: " +
                               $"{worst.ActualGradePercent:F1}% → adding {newPoints.Count} points");

                    // Insert in reverse order
                    for (int j = newPoints.Count - 1; j >= 0; j--)
                    {
                        InsertPointWithHandles(curve, worst.StartPointIndex + 1, newPoints[j]);
                        totalAdded++;
                    }

                    InvalidateCaches(); // Refresh for next iteration
                }
                else
                {
                    break; // Can't add more points to this segment
                }
            }

            return totalAdded;
        }

        /// <summary>
        /// Adjusts point heights to satisfy grade constraints.
        /// Uses iterative projection algorithm.
        /// Returns number of points modified.
        /// </summary>
        private int AdjustHeightsForGrade(Curve3D curve, List<string> details)
        {
            int n = curve.PointCount;
            if (n < 2) return 0;

            float maxSlope = _maxGradePercent / 100f;
            var geometry = GatherGeometry(curve);

            // Initialize heights
            float[] heights = new float[n];
            for (int i = 0; i < n; i++)
            {
                heights[i] = geometry[i].OriginalY;
            }

            // Iterative projection
            float lastChange = float.MaxValue;
            for (int iteration = 0; iteration < MAX_ENFORCEMENT_ITERATIONS; iteration++)
            {
                float totalChange = 0f;

                // Forward sweep
                for (int i = 1; i < n; i++)
                {
                    float hDist = geometry[i - 1].HDistToNext;
                    if (hDist < MIN_HORIZONTAL_SEGMENT) continue;

                    float maxAllowed = heights[i - 1] + hDist * maxSlope;
                    float minAllowed = heights[i - 1] - hDist * maxSlope;

                    float oldH = heights[i];
                    heights[i] = Mathf.Clamp(heights[i], minAllowed, maxAllowed);
                    totalChange += Mathf.Abs(heights[i] - oldH);
                }

                // Backward sweep
                for (int i = n - 2; i >= 0; i--)
                {
                    float hDist = geometry[i].HDistToNext;
                    if (hDist < MIN_HORIZONTAL_SEGMENT) continue;

                    float maxAllowed = heights[i + 1] + hDist * maxSlope;
                    float minAllowed = heights[i + 1] - hDist * maxSlope;

                    float oldH = heights[i];
                    heights[i] = Mathf.Clamp(heights[i], minAllowed, maxAllowed);
                    totalChange += Mathf.Abs(heights[i] - oldH);
                }

                if (totalChange < HEIGHT_TOLERANCE * n || 
                    Mathf.Abs(totalChange - lastChange) < HEIGHT_TOLERANCE)
                {
                    break;
                }
                lastChange = totalChange;
            }

            // Relaxation: pull back toward original where possible
            heights = RelaxTowardOriginal(heights, geometry, maxSlope);

            // Apply changes
            int modified = 0;
            for (int i = 0; i < n; i++)
            {
                float diff = Mathf.Abs(heights[i] - geometry[i].OriginalY);
                if (diff > HEIGHT_TOLERANCE)
                {
                    Vector3 pos = geometry[i].Position;
                    float oldY = pos.Y;
                    pos.Y = heights[i];
                    curve.SetPointPosition(i, pos);
                    modified++;

                    if (diff > 0.5f) // Log significant changes
                    {
                        details.Add($"Point {i}: {oldY:F2}m → {heights[i]:F2}m (Δ{heights[i] - oldY:+0.00;-0.00}m)");
                    }
                }
            }

            return modified;
        }

        /// <summary>
        /// Relaxation pass: tries to restore original heights where constraints allow.
        /// </summary>
        private float[] RelaxTowardOriginal(float[] heights, PointGeometry[] geometry, float maxSlope)
        {
            int n = heights.Length;
            float[] result = (float[])heights.Clone();

            for (int pass = 0; pass < 10; pass++)
            {
                bool anyChange = false;

                for (int i = 0; i < n; i++)
                {
                    float target = geometry[i].OriginalY;
                    float current = result[i];

                    if (Mathf.Abs(target - current) < HEIGHT_TOLERANCE) continue;

                    // Try to move toward original
                    float step = (target - current) * 0.3f;
                    float testHeight = current + step;

                    // Validate against neighbors
                    bool valid = true;

                    if (i > 0)
                    {
                        float hDist = geometry[i - 1].HDistToNext;
                        if (hDist >= MIN_HORIZONTAL_SEGMENT)
                        {
                            float maxFromPrev = result[i - 1] + hDist * maxSlope;
                            float minFromPrev = result[i - 1] - hDist * maxSlope;
                            if (testHeight > maxFromPrev + HEIGHT_TOLERANCE || 
                                testHeight < minFromPrev - HEIGHT_TOLERANCE)
                                valid = false;
                        }
                    }

                    if (i < n - 1 && valid)
                    {
                        float hDist = geometry[i].HDistToNext;
                        if (hDist >= MIN_HORIZONTAL_SEGMENT)
                        {
                            float maxToNext = result[i + 1] + hDist * maxSlope;
                            float minToNext = result[i + 1] - hDist * maxSlope;
                            if (testHeight > maxToNext + HEIGHT_TOLERANCE || 
                                testHeight < minToNext - HEIGHT_TOLERANCE)
                                valid = false;
                        }
                    }

                    if (valid)
                    {
                        result[i] = testHeight;
                        anyChange = true;
                    }
                }

                if (!anyChange) break;
            }

            return result;
        }

        /// <summary>
        /// Calculates how many switchback points are needed.
        /// </summary>
        private int CalculateSwitchbackPointsNeeded(float horizontalDist, float verticalDist, float maxSlope)
        {
            if (horizontalDist < MIN_HORIZONTAL_SEGMENT) return 0;

            float currentGrade = verticalDist / horizontalDist;
            if (currentGrade <= maxSlope) return 0;

            float requiredHDist = verticalDist / maxSlope;
            float additionalNeeded = requiredHDist - horizontalDist;

            if (additionalNeeded <= 0) return 0;

            int legsNeeded = Mathf.CeilToInt(additionalNeeded / SWITCHBACK_MIN_LENGTH);
            return Mathf.Max(2, legsNeeded);
        }

        /// <summary>
        /// Generates switchback points between two positions.
        /// </summary>
        private List<Vector3> GenerateSwitchbackPoints(Vector3 start, Vector3 end, int numPoints, float maxSlope)
        {
            var points = new List<Vector3>();

            Vector3 delta = end - start;
            Vector2 horizontalDelta = new Vector2(delta.X, delta.Z);
            float originalHDist = horizontalDelta.Length();
            float verticalDist = end.Y - start.Y;

            if (originalHDist < MIN_HORIZONTAL_SEGMENT || numPoints < 1)
                return points;

            Vector2 forward = horizontalDelta.Normalized();
            Vector2 perpendicular = new Vector2(-forward.Y, forward.X);

            // Calculate switchback width
            float requiredHDist = Mathf.Abs(verticalDist) / maxSlope;
            float extraDistNeeded = Mathf.Max(0, requiredHDist - originalHDist);
            
            float switchbackWidth = Mathf.Sqrt(Mathf.Max(1f, extraDistNeeded * originalHDist / numPoints)) * 0.5f;
            switchbackWidth = Mathf.Clamp(switchbackWidth, 3f, 30f);

            float heightPerSegment = verticalDist / (numPoints + 1);

            for (int i = 1; i <= numPoints; i++)
            {
                float t = (float)i / (numPoints + 1);

                Vector2 baseXZ = new Vector2(
                    Mathf.Lerp(start.X, end.X, t),
                    Mathf.Lerp(start.Z, end.Z, t)
                );

                float perpOffset = ((i % 2 == 1) ? 1f : -1f) * switchbackWidth;
                Vector2 finalXZ = baseXZ + perpendicular * perpOffset;

                float y = start.Y + heightPerSegment * i;

                points.Add(new Vector3(finalXZ.X, y, finalXZ.Y));
            }

            return points;
        }

        /// <summary>
        /// Inserts a point with appropriate handles.
        /// </summary>
        private void InsertPointWithHandles(Curve3D curve, int index, Vector3 position)
        {
            InsertPoint(index, position);

            if (!_autoSmoothCurve && curve.PointCount > 2 && index > 0 && index < curve.PointCount - 1)
            {
                Vector3 prev = curve.GetPointPosition(index - 1);
                Vector3 next = curve.GetPointPosition(index + 1);
                Vector3 tangent = (next - prev).Normalized();

                float handleLen = Mathf.Min(
                    position.DistanceTo(prev),
                    position.DistanceTo(next)
                ) * 0.25f;

                curve.SetPointIn(index, -tangent * handleLen);
                curve.SetPointOut(index, tangent * handleLen);
            }
        }

        #endregion

        #region Downhill Constraint Enforcement

        /// <summary>
        /// Enforces downhill constraints ensuring water always flows downhill.
        /// </summary>
        public EnforcementResult EnforceDownhillConstraints()
        {
            var curve = Curve;
            if (curve == null || curve.PointCount < 2)
                return EnforcementResult.Failed("No valid curve to process.");

            if (!_enableDownhillConstraint)
                return EnforcementResult.Failed("Downhill constraint is not enabled.");

            var initialViolations = GetDownhillViolations();
            if (initialViolations.Count == 0)
                return EnforcementResult.NoChanges();

            DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.LayerLifecycle,
                $"EnforceDownhillConstraints: Starting with {initialViolations.Count} violations");

            bool flowIsForward = IsFlowDirectionForward();
            int n = curve.PointCount;
            var details = new List<string>();
            var geometry = GatherGeometry(curve);

            float maxSlope = _enableGradeConstraint ? _maxGradePercent / 100f : 1.0f;
            float minDescentPerMeter = 0.001f;

            float[] heights = new float[n];
            for (int i = 0; i < n; i++)
            {
                heights[i] = geometry[i].OriginalY;
            }

            int pointsModified = 0;

            if (flowIsForward)
            {
                float currentMax = heights[0];

                for (int i = 1; i < n; i++)
                {
                    float hDist = geometry[i - 1].HDistToNext;

                    float minDescent = Mathf.Max(hDist * minDescentPerMeter, 0.01f);
                    float maxDescent = hDist * maxSlope;

                    float idealHeight = currentMax - minDescent;
                    float minAllowed = currentMax - maxDescent;

                    float targetHeight = heights[i];

                    if (targetHeight > currentMax - HEIGHT_TOLERANCE)
                    {
                        targetHeight = idealHeight;
                    }
                    else if (targetHeight < minAllowed && _enableGradeConstraint)
                    {
                        targetHeight = minAllowed;
                    }

                    if (Mathf.Abs(targetHeight - heights[i]) > HEIGHT_TOLERANCE)
                    {
                        float oldHeight = heights[i];
                        heights[i] = targetHeight;
                        pointsModified++;
                        details.Add($"Point {i}: {oldHeight:F2}m → {targetHeight:F2}m");
                    }

                    currentMax = heights[i];
                }
            }
            else
            {
                float currentMax = heights[n - 1];

                for (int i = n - 2; i >= 0; i--)
                {
                    float hDist = geometry[i].HDistToNext;

                    float minDescent = Mathf.Max(hDist * minDescentPerMeter, 0.01f);
                    float maxDescent = hDist * maxSlope;

                    float idealHeight = currentMax - minDescent;
                    float minAllowed = currentMax - maxDescent;

                    float targetHeight = heights[i];

                    if (targetHeight > currentMax - HEIGHT_TOLERANCE)
                    {
                        targetHeight = idealHeight;
                    }
                    else if (targetHeight < minAllowed && _enableGradeConstraint)
                    {
                        targetHeight = minAllowed;
                    }

                    if (Mathf.Abs(targetHeight - heights[i]) > HEIGHT_TOLERANCE)
                    {
                        float oldHeight = heights[i];
                        heights[i] = targetHeight;
                        pointsModified++;
                        details.Add($"Point {i}: {oldHeight:F2}m → {targetHeight:F2}m");
                    }

                    currentMax = heights[i];
                }
            }

            // Apply changes
            for (int i = 0; i < n; i++)
            {
                if (Mathf.Abs(heights[i] - geometry[i].OriginalY) > HEIGHT_TOLERANCE)
                {
                    Vector3 pos = geometry[i].Position;
                    pos.Y = heights[i];
                    curve.SetPointPosition(i, pos);
                }
            }

            if (pointsModified > 0)
            {
                InvalidateCaches();
            }

            DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.LayerLifecycle,
                $"EnforceDownhillConstraints: Modified {pointsModified} points");

            return EnforcementResult.Modified(pointsModified, 0, details);
        }

        #endregion

        #region Combined Enforcement

        /// <summary>
        /// Enforces all applicable constraints for this path type.
        /// </summary>
        public EnforcementResult EnforceAllConstraints()
        {
            int totalModified = 0;
            int totalAdded = 0;
            var allDetails = new List<string>();
            bool allSuccess = true;

            if (_enableDownhillConstraint)
            {
                var downhillResult = EnforceDownhillConstraints();
                totalModified += downhillResult.PointsModified;
                totalAdded += downhillResult.PointsAdded;
                allSuccess &= downhillResult.Success;

                if (downhillResult.PointsModified > 0 || downhillResult.Details?.Count > 0)
                {
                    allDetails.Add("── Downhill Flow ──");
                    allDetails.Add(downhillResult.Message);
                    if (downhillResult.Details?.Count > 0)
                    {
                        allDetails.AddRange(downhillResult.Details.Take(5));
                        if (downhillResult.Details.Count > 5)
                            allDetails.Add($"  ... and {downhillResult.Details.Count - 5} more");
                    }
                }
            }

            if (_enableGradeConstraint)
            {
                var gradeResult = EnforceGradeConstraints();
                totalModified += gradeResult.PointsModified;
                totalAdded += gradeResult.PointsAdded;
                allSuccess &= gradeResult.Success;

                if (gradeResult.PointsModified > 0 || gradeResult.PointsAdded > 0 || gradeResult.Details?.Count > 0)
                {
                    allDetails.Add("── Grade Limits ──");
                    allDetails.Add(gradeResult.Message);
                    if (gradeResult.Details?.Count > 0)
                    {
                        allDetails.AddRange(gradeResult.Details.Take(5));
                        if (gradeResult.Details.Count > 5)
                            allDetails.Add($"  ... and {gradeResult.Details.Count - 5} more");
                    }
                }
            }

            if (totalModified == 0 && totalAdded == 0)
                return EnforcementResult.NoChanges();

            return new EnforcementResult
            {
                Success = allSuccess,
                PointsModified = totalModified,
                PointsAdded = totalAdded,
                Message = allSuccess
                    ? $"Adjusted {totalModified} point(s)" + (totalAdded > 0 ? $", added {totalAdded} switchback(s)" : "")
                    : $"Partial fix: {totalModified} adjusted, some violations remain",
                Details = allDetails
            };
        }

        #endregion

        #region Utility Methods

        /// <summary>
        /// Gathers geometry data from all curve points.
        /// </summary>
        private PointGeometry[] GatherGeometry(Curve3D curve)
        {
            int n = curve.PointCount;
            var geometry = new PointGeometry[n];

            for (int i = 0; i < n; i++)
            {
                geometry[i] = new PointGeometry
                {
                    Position = curve.GetPointPosition(i),
                    OriginalY = curve.GetPointPosition(i).Y,
                    HandleIn = curve.GetPointIn(i),
                    HandleOut = curve.GetPointOut(i),
                    HDistToNext = 0f
                };

                if (i < n - 1)
                {
                    Vector3 p1 = geometry[i].Position;
                    Vector3 p2 = curve.GetPointPosition(i + 1);
                    geometry[i].HDistToNext = new Vector2(p2.X - p1.X, p2.Z - p1.Z).Length();
                }
            }

            return geometry;
        }

        /// <summary>
        /// Calculates the grade percentage between two points.
        /// </summary>
        public static float CalculateGrade(Vector3 p1, Vector3 p2)
        {
            float horizontalDist = new Vector2(p2.X - p1.X, p2.Z - p1.Z).Length();
            if (horizontalDist < MIN_HORIZONTAL_SEGMENT) return 0f;

            float verticalDist = Mathf.Abs(p2.Y - p1.Y);
            return (verticalDist / horizontalDist) * 100f;
        }

        /// <summary>
        /// Calculates the total elevation change along the curve.
        /// </summary>
        public float GetTotalElevationChange()
        {
            var curve = Curve;
            if (curve == null || curve.PointCount < 2) return 0f;

            float firstY = curve.GetPointPosition(0).Y;
            float lastY = curve.GetPointPosition(curve.PointCount - 1).Y;

            return lastY - firstY;
        }

        /// <summary>
        /// Gets the minimum and maximum elevation along the curve.
        /// </summary>
        public (float min, float max) GetElevationRange()
        {
            var curve = Curve;
            if (curve == null || curve.PointCount == 0)
                return (0f, 0f);

            float min = float.MaxValue;
            float max = float.MinValue;

            for (int i = 0; i < curve.PointCount; i++)
            {
                float y = curve.GetPointPosition(i).Y;
                min = Mathf.Min(min, y);
                max = Mathf.Max(max, y);
            }

            return (min, max);
        }

        /// <summary>
        /// Gets detailed constraint information for UI display.
        /// </summary>
        public string GetDetailedConstraintInfo()
        {
            var lines = new List<string>();
            var curve = Curve;

            if (curve == null || curve.PointCount < 2)
            {
                return "No valid path";
            }

            lines.Add($"Points: {curve.PointCount}");
            lines.Add($"Length: {PathLength:F1}m");

            var (minElev, maxElev) = GetElevationRange();
            lines.Add($"Elevation: {minElev:F1}m to {maxElev:F1}m (Δ{maxElev - minElev:F1}m)");

            if (_enableGradeConstraint)
            {
                var analysis = AnalyzeGradeFeasibility();
                lines.Add($"Average grade: {analysis.RequiredGradePercent:F1}%");
                lines.Add($"Max allowed: {_maxGradePercent:F1}%");
                lines.Add($"Switchbacks: {(_allowSwitchbackGeneration ? $"enabled (max {_maxSwitchbackPoints})" : "disabled")}");

                var violations = GetGradeViolations();
                if (violations.Count > 0)
                {
                    lines.Add($"Violations: {violations.Count}");
                }
            }

            if (_enableDownhillConstraint)
            {
                string flowDir = IsFlowDirectionForward() ? "Start → End" : "End → Start";
                lines.Add($"Flow direction: {flowDir}");

                var violations = GetDownhillViolations();
                if (violations.Count > 0)
                {
                    lines.Add($"Uphill violations: {violations.Count}");
                }
            }

            return string.Join("\n", lines);
        }

        #endregion
    }
}