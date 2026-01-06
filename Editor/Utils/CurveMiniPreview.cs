// /Editor/Utils/CurveMiniPreview.cs
using Godot;

namespace Terrain3DTools.Editor.Utils
{
    /// <summary>
    /// Small inline preview control for displaying a Curve in the inspector.
    /// </summary>
    [Tool]
    public partial class CurveMiniPreview : Control
    {
        #region Constants
        private const float PADDING = 4f;
        #endregion

        #region Fields
        private Curve _curve;
        #endregion

        #region Constructors
        public CurveMiniPreview()
        {
            CustomMinimumSize = new Vector2(100, 40);
            SizeFlagsHorizontal = SizeFlags.ExpandFill;
        }

        public CurveMiniPreview(Curve curve) : this()
        {
            _curve = curve;
        }
        #endregion

        #region Public Methods
        public void SetCurve(Curve curve)
        {
            _curve = curve;
            QueueRedraw();
        }

        public Curve GetCurve() => _curve;
        #endregion

        #region Drawing
        public override void _Draw()
        {
            var rect = GetRect();
            float w = rect.Size.X;
            float h = rect.Size.Y;
            float graphW = w - PADDING * 2;
            float graphH = h - PADDING * 2;

            // Background
            DrawRect(new Rect2(0, 0, w, h), new Color(0.15f, 0.15f, 0.15f));

            // Border
            DrawRect(new Rect2(PADDING, PADDING, graphW, graphH), new Color(0.25f, 0.25f, 0.25f), false, 1f);

            if (_curve == null || _curve.PointCount < 2)
            {
                // Draw diagonal line for empty/invalid curve
                DrawLine(
                    new Vector2(PADDING, PADDING + graphH),
                    new Vector2(PADDING + graphW, PADDING),
                    new Color(0.4f, 0.4f, 0.4f),
                    1f
                );
                return;
            }

            _curve.Bake();

            // Draw curve
            var curveColor = new Color(0.4f, 0.8f, 1.0f);
            int samples = Mathf.Max(16, (int)graphW / 2);
            Vector2? lastPoint = null;

            for (int i = 0; i <= samples; i++)
            {
                float t = (float)i / samples;
                float y = _curve.SampleBaked(t);

                // Clamp y to reasonable range for display
                y = Mathf.Clamp(y, -0.5f, 1.5f);

                Vector2 screenPos = new Vector2(
                    PADDING + t * graphW,
                    PADDING + (1f - y) * graphH
                );

                // Clamp to visible area
                screenPos.Y = Mathf.Clamp(screenPos.Y, PADDING, PADDING + graphH);

                if (lastPoint.HasValue)
                {
                    DrawLine(lastPoint.Value, screenPos, curveColor, 1.5f);
                }
                lastPoint = screenPos;
            }

            // Draw control points as small dots
            for (int i = 0; i < _curve.PointCount; i++)
            {
                Vector2 pos = _curve.GetPointPosition(i);
                float clampedY = Mathf.Clamp(pos.Y, -0.5f, 1.5f);

                Vector2 screenPos = new Vector2(
                    PADDING + pos.X * graphW,
                    PADDING + (1f - clampedY) * graphH
                );

                screenPos.Y = Mathf.Clamp(screenPos.Y, PADDING, PADDING + graphH);

                DrawCircle(screenPos, 3f, new Color(0.8f, 0.9f, 1.0f));
            }
        }
        #endregion
    }
}