// /Editor/PathLayerInspectorControls.cs
using System;
using System.Collections.Generic;
using Godot;
using Terrain3DTools.Layers;
using Terrain3DTools.Layers.Path;

namespace Terrain3DTools.Editor
{
    /// <summary>
    /// Custom control for drawing the profile cross-section preview.
    /// Renders the actual profile shape using zone HeightCurves.
    /// </summary>
    [Tool]
    public partial class PathProfilePreviewDrawArea : Control
    {
        #region Fields
        private ulong _layerInstanceId;
        private Func<int> _getSelectedZone;
        #endregion

        #region Properties
        private PathLayer Layer
        {
            get
            {
                if (_layerInstanceId == 0) return null;
                var obj = GodotObject.InstanceFromId(_layerInstanceId);
                return obj as PathLayer;
            }
        }
        #endregion

        #region Constructors
        /// <summary>
        /// Required parameterless constructor for Godot.
        /// </summary>
        public PathProfilePreviewDrawArea()
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill;
            SizeFlagsVertical = SizeFlags.ExpandFill;
        }

        public PathProfilePreviewDrawArea(PathLayer layer, Func<int> getSelectedZone) : this()
        {
            _layerInstanceId = layer?.GetInstanceId() ?? 0;
            _getSelectedZone = getSelectedZone;
        }
        #endregion

        #region Public Methods
        public void Initialize(PathLayer layer, Func<int> getSelectedZone)
        {
            _layerInstanceId = layer?.GetInstanceId() ?? 0;
            _getSelectedZone = getSelectedZone;
            QueueRedraw();
        }
        #endregion

        #region Drawing
        public override void _Draw()
        {
            var layer = Layer;
            if (layer?.Profile == null) return;

            var rect = GetRect();
            float width = rect.Size.X;
            float height = rect.Size.Y;
            float centerX = width / 2f;
            float baselineY = height - 25f;

            // Draw background grid
            DrawBackgroundGrid(width, height, centerX, baselineY);

            // Calculate scale factors
            float profileWidth = layer.Profile.HalfWidth;
            if (profileWidth < 0.1f) profileWidth = 10f;

            float maxHeight = CalculateMaxHeight();
            float minHeight = CalculateMinHeight();
            float heightRange = maxHeight - minHeight;
            if (heightRange < 0.1f) heightRange = 5f;

            float scaleX = (width * 0.45f) / profileWidth;
            float scaleY = (height * 0.5f) / heightRange;

            // Draw terrain reference line (existing ground level)
            DrawDashedLine(
                new Vector2(0, baselineY),
                new Vector2(width, baselineY),
                new Color(0.4f, 0.4f, 0.4f, 0.5f),
                1f
            );

            // Draw the profile shape
            DrawProfileShape(centerX, baselineY, scaleX, scaleY, minHeight);

            // Draw zone boundaries and labels
            DrawZoneBoundaries(centerX, baselineY, scaleX, height);

            // Draw centerline
            DrawLine(
                new Vector2(centerX, 10),
                new Vector2(centerX, height - 10),
                new Color(1f, 1f, 1f, 0.3f),
                1f
            );

            // Draw labels
            DrawLabels(centerX, width, height, profileWidth, maxHeight, minHeight);
        }

        private void DrawBackgroundGrid(float width, float height, float centerX, float baselineY)
        {
            var gridColor = new Color(0.2f, 0.2f, 0.2f, 0.5f);

            // Vertical grid lines
            for (int i = -4; i <= 4; i++)
            {
                float x = centerX + (width * 0.1f * i);
                DrawLine(new Vector2(x, 10), new Vector2(x, height - 10), gridColor, 1f);
            }

            // Horizontal grid lines
            for (int i = 0; i < 5; i++)
            {
                float y = 20 + (height - 40) * i / 4f;
                DrawLine(new Vector2(10, y), new Vector2(width - 10, y), gridColor, 1f);
            }
        }

        private float CalculateMaxHeight()
        {
            float maxH = 2f;
            var layer = Layer;
            if (layer?.Profile?.Zones == null) return maxH;

            foreach (var zone in layer.Profile.Zones)
            {
                if (zone != null && zone.Enabled)
                {
                    maxH = Mathf.Max(maxH, zone.HeightOffset + 1f);
                }
            }
            return maxH;
        }

        private float CalculateMinHeight()
        {
            float minH = 0f;
            var layer = Layer;
            if (layer?.Profile?.Zones == null) return minH;

            foreach (var zone in layer.Profile.Zones)
            {
                if (zone != null && zone.Enabled)
                {
                    minH = Mathf.Min(minH, zone.HeightOffset - 1f);
                }
            }
            return minH;
        }

        private void DrawProfileShape(float centerX, float baselineY, float scaleX, float scaleY, float minHeight)
        {
            var layer = Layer;
            if (layer?.Profile?.Zones == null) return;

            int selectedZone = _getSelectedZone?.Invoke() ?? -1;

            // Build the profile path points for each side
            var rightPoints = new List<Vector2>();
            var leftPoints = new List<Vector2>();

            float currentDist = 0f;
            float previousHeight = 0f;

            // Start at center
            rightPoints.Add(new Vector2(centerX, baselineY));
            leftPoints.Add(new Vector2(centerX, baselineY));

            for (int zoneIdx = 0; zoneIdx < layer.Profile.Zones.Count && zoneIdx < 8; zoneIdx++)
            {
                var zone = layer.Profile.Zones[zoneIdx];
                if (zone == null || !zone.Enabled) continue;

                // Use centralized zone colors
                Color zoneColor = ZoneColors.GetColor(zone.Type, ZoneColors.ColorContext.Preview);
                if (zoneIdx == selectedZone)
                {
                    zoneColor = ZoneColors.GetHighlightedColor(zone.Type, ZoneColors.ColorContext.Preview);
                }

                // Sample the height curve across this zone
                int samples = 16;
                var zoneCurve = zone.HeightCurve;

                for (int s = 0; s <= samples; s++)
                {
                    float t = (float)s / samples;
                    float distInZone = t * zone.Width;
                    float totalDist = currentDist + distInZone;

                    // Get height from curve (curve Y value multiplies height offset)
                    float curveValue = 1f;
                    if (zoneCurve != null && zoneCurve.PointCount >= 2)
                    {
                        zoneCurve.Bake();
                        curveValue = zoneCurve.SampleBaked(t);
                    }

                    // Calculate actual height at this point
                    float heightAtPoint = zone.HeightOffset * curveValue;

                    // Blend with previous zone's end height at start of zone
                    if (s == 0 && zoneIdx > 0)
                    {
                        heightAtPoint = Mathf.Lerp(previousHeight, heightAtPoint, 0.5f);
                    }

                    // Convert to screen coordinates
                    float screenX = totalDist * scaleX;
                    float screenY = baselineY - (heightAtPoint - minHeight) * scaleY;

                    rightPoints.Add(new Vector2(centerX + screenX, screenY));
                    leftPoints.Add(new Vector2(centerX - screenX, screenY));

                    if (s == samples)
                    {
                        previousHeight = heightAtPoint;
                    }
                }

                currentDist += zone.Width;

                // Draw zone fill
                DrawZoneFill(centerX, baselineY, scaleX, scaleY, minHeight, zone, currentDist - zone.Width, zoneColor);
            }

            // Draw the profile outline
            var outlineColor = new Color(0.9f, 0.9f, 0.9f);

            // Right side outline
            for (int i = 0; i < rightPoints.Count - 1; i++)
            {
                DrawLine(rightPoints[i], rightPoints[i + 1], outlineColor, 2f);
            }

            // Left side outline
            for (int i = 0; i < leftPoints.Count - 1; i++)
            {
                DrawLine(leftPoints[i], leftPoints[i + 1], outlineColor, 2f);
            }
        }

        private void DrawZoneFill(float centerX, float baselineY, float scaleX, float scaleY,
            float minHeight, ProfileZone zone, float zoneStartDist, Color zoneColor)
        {
            int samples = 16;
            var zoneCurve = zone.HeightCurve;

            var polygonPoints = new List<Vector2>();

            // Build right side polygon (from baseline up, then back down)
            float startX = centerX + zoneStartDist * scaleX;
            float endX = centerX + (zoneStartDist + zone.Width) * scaleX;

            // Start at baseline
            polygonPoints.Add(new Vector2(startX, baselineY));

            // Add curve points
            for (int s = 0; s <= samples; s++)
            {
                float t = (float)s / samples;
                float distInZone = t * zone.Width;
                float totalDist = zoneStartDist + distInZone;

                float curveValue = 1f;
                if (zoneCurve != null && zoneCurve.PointCount >= 2)
                {
                    zoneCurve.Bake();
                    curveValue = zoneCurve.SampleBaked(t);
                }

                float heightAtPoint = zone.HeightOffset * curveValue;
                float screenX = centerX + totalDist * scaleX;
                float screenY = baselineY - (heightAtPoint - minHeight) * scaleY;

                polygonPoints.Add(new Vector2(screenX, screenY));
            }

            // Back to baseline
            polygonPoints.Add(new Vector2(endX, baselineY));

            // Draw filled polygon for right side
            if (polygonPoints.Count >= 3)
            {
                DrawColoredPolygon(polygonPoints.ToArray(), zoneColor with { A = 0.4f });
            }

            // Mirror for left side
            var leftPolygon = new List<Vector2>();
            foreach (var pt in polygonPoints)
            {
                leftPolygon.Add(new Vector2(2 * centerX - pt.X, pt.Y));
            }

            if (leftPolygon.Count >= 3)
            {
                DrawColoredPolygon(leftPolygon.ToArray(), zoneColor with { A = 0.4f });
            }
        }

        private void DrawZoneBoundaries(float centerX, float baselineY, float scaleX, float height)
        {
            var layer = Layer;
            if (layer?.Profile?.Zones == null) return;

            int selectedZone = _getSelectedZone?.Invoke() ?? -1;
            float currentDist = 0f;

            for (int i = 0; i < layer.Profile.Zones.Count; i++)
            {
                var zone = layer.Profile.Zones[i];
                if (zone == null || !zone.Enabled) continue;

                currentDist += zone.Width;
                float boundaryX = currentDist * scaleX;

                // Draw boundary line
                var boundaryColor = (i == selectedZone) ? Colors.White : new Color(0.5f, 0.5f, 0.5f, 0.5f);
                float lineWidth = (i == selectedZone) ? 2f : 1f;

                // Right side boundary
                DrawLine(
                    new Vector2(centerX + boundaryX, 15),
                    new Vector2(centerX + boundaryX, height - 15),
                    boundaryColor,
                    lineWidth
                );

                // Left side boundary (mirrored)
                DrawLine(
                    new Vector2(centerX - boundaryX, 15),
                    new Vector2(centerX - boundaryX, height - 15),
                    boundaryColor,
                    lineWidth
                );
            }
        }

        private void DrawLabels(float centerX, float width, float height, float profileWidth, float maxHeight, float minHeight)
        {
            var font = ThemeDB.FallbackFont;
            var labelColor = new Color(0.7f, 0.7f, 0.7f);
            int fontSize = 10;

            // Center label
            DrawString(font, new Vector2(centerX - 15, height - 5), "Center",
                HorizontalAlignment.Center, -1, fontSize, labelColor);

            // Width labels
            DrawString(font, new Vector2(10, height - 5), $"-{profileWidth:F1}m",
                HorizontalAlignment.Left, -1, fontSize, labelColor);
            DrawString(font, new Vector2(width - 50, height - 5), $"+{profileWidth:F1}m",
                HorizontalAlignment.Left, -1, fontSize, labelColor);

            // Height labels
            if (maxHeight > 0.1f)
            {
                DrawString(font, new Vector2(5, 20), $"+{maxHeight:F1}m",
                    HorizontalAlignment.Left, -1, fontSize, labelColor);
            }
            if (minHeight < -0.1f)
            {
                DrawString(font, new Vector2(5, height - 30), $"{minHeight:F1}m",
                    HorizontalAlignment.Left, -1, fontSize, labelColor);
            }
        }

        private void DrawDashedLine(Vector2 from, Vector2 to, Color color, float width)
        {
            float dashLength = 8f;
            float gapLength = 4f;

            Vector2 direction = (to - from).Normalized();
            float totalLength = from.DistanceTo(to);
            float currentDist = 0f;

            while (currentDist < totalLength)
            {
                float dashEnd = Mathf.Min(currentDist + dashLength, totalLength);
                Vector2 dashStart = from + direction * currentDist;
                Vector2 dashEndPt = from + direction * dashEnd;

                DrawLine(dashStart, dashEndPt, color, width);

                currentDist += dashLength + gapLength;
            }
        }
        #endregion
    }
}