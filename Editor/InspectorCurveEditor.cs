using Godot;

namespace Terrain3DTools.Editor
{
    /// <summary>
    /// Interactive curve editor control for the popup window.
    /// </summary>
    [Tool]
    public partial class InspectorCurveEditor : Control
    {
        private Curve _curve;
        private int _selectedPoint = -1;
        private int _hoveredPoint = -1;
        private bool _dragging = false;
        private bool _draggingInHandle = false;
        private bool _draggingOutHandle = false;
        private const float POINT_RADIUS = 6f;
        private const float HANDLE_RADIUS = 4f;
        private const float PADDING = 30f;

        // Required parameterless constructor for Godot
        public InspectorCurveEditor()
        {
            _curve = new Curve();
            FocusMode = FocusModeEnum.All;
            MouseFilter = MouseFilterEnum.Stop;
        }

        public InspectorCurveEditor(Curve curve) : this()
        {
            _curve = curve ?? new Curve();
        }

        public void SetCurve(Curve curve)
        {
            _curve = curve ?? new Curve();
            _selectedPoint = -1;
            QueueRedraw();
        }

        public Curve GetCurve() => _curve;

        public override void _Draw()
        {
            var rect = GetRect();
            float w = rect.Size.X;
            float h = rect.Size.Y;
            float graphW = w - PADDING * 2;
            float graphH = h - PADDING * 2;

            // Background
            DrawRect(new Rect2(0, 0, w, h), new Color(0.12f, 0.12f, 0.12f));

            // Graph area background
            DrawRect(new Rect2(PADDING, PADDING, graphW, graphH), new Color(0.18f, 0.18f, 0.18f));

            // Grid lines
            var gridColor = new Color(0.25f, 0.25f, 0.25f);
            var gridColorMajor = new Color(0.35f, 0.35f, 0.35f);

            // Vertical grid (0.25 intervals)
            for (int i = 0; i <= 4; i++)
            {
                float x = PADDING + (graphW * i / 4f);
                DrawLine(new Vector2(x, PADDING), new Vector2(x, PADDING + graphH),
                    i == 0 || i == 4 ? gridColorMajor : gridColor, 1f);

                // Label
                string label = (i * 0.25f).ToString("F2");
                DrawString(ThemeDB.FallbackFont, new Vector2(x - 10, h - 5), label,
                    HorizontalAlignment.Center, -1, 10, new Color(0.5f, 0.5f, 0.5f));
            }

            // Horizontal grid (0.25 intervals)
            for (int i = 0; i <= 4; i++)
            {
                float y = PADDING + (graphH * i / 4f);
                DrawLine(new Vector2(PADDING, y), new Vector2(PADDING + graphW, y),
                    i == 0 || i == 4 ? gridColorMajor : gridColor, 1f);

                // Label
                string label = (1f - i * 0.25f).ToString("F2");
                DrawString(ThemeDB.FallbackFont, new Vector2(5, y + 4), label,
                    HorizontalAlignment.Left, -1, 10, new Color(0.5f, 0.5f, 0.5f));
            }

            // Border
            DrawRect(new Rect2(PADDING, PADDING, graphW, graphH), new Color(0.4f, 0.4f, 0.4f), false, 1f);

            if (_curve == null) return;

            _curve.Bake();

            // Draw curve line
            var curveColor = new Color(0.4f, 0.8f, 1.0f);
            int samples = (int)graphW;
            Vector2? lastPoint = null;

            for (int i = 0; i <= samples; i++)
            {
                float t = (float)i / samples;
                float y = _curve.SampleBaked(t);

                Vector2 screenPos = CurveToScreen(new Vector2(t, y), graphW, graphH);

                if (lastPoint.HasValue)
                {
                    DrawLine(lastPoint.Value, screenPos, curveColor, 2f);
                }
                lastPoint = screenPos;
            }

            // Draw control points and handles
            for (int i = 0; i < _curve.PointCount; i++)
            {
                Vector2 pos = _curve.GetPointPosition(i);
                Vector2 screenPos = CurveToScreen(pos, graphW, graphH);

                // Draw tangent handles for selected point
                if (i == _selectedPoint)
                {
                    float inTangent = _curve.GetPointLeftTangent(i);
                    float outTangent = _curve.GetPointRightTangent(i);

                    // Calculate handle positions (tangent is dy/dx)
                    float handleLength = 30f;

                    // In handle (left)
                    if (i > 0)
                    {
                        Vector2 inHandleDir = new Vector2(-1, inTangent).Normalized();
                        Vector2 inHandlePos = screenPos + inHandleDir * handleLength;

                        DrawLine(screenPos, inHandlePos, new Color(1f, 0.5f, 0.2f, 0.8f), 1f);
                        DrawCircle(inHandlePos, HANDLE_RADIUS, new Color(1f, 0.5f, 0.2f));
                    }

                    // Out handle (right)
                    if (i < _curve.PointCount - 1)
                    {
                        Vector2 outHandleDir = new Vector2(1, -outTangent).Normalized();
                        Vector2 outHandlePos = screenPos + outHandleDir * handleLength;

                        DrawLine(screenPos, outHandlePos, new Color(0.2f, 1f, 0.5f, 0.8f), 1f);
                        DrawCircle(outHandlePos, HANDLE_RADIUS, new Color(0.2f, 1f, 0.5f));
                    }
                }

                // Draw point
                Color pointColor;
                float radius = POINT_RADIUS;

                if (i == _selectedPoint)
                {
                    pointColor = Colors.White;
                    radius = POINT_RADIUS + 2;
                }
                else if (i == _hoveredPoint)
                {
                    pointColor = new Color(0.8f, 0.9f, 1.0f);
                    radius = POINT_RADIUS + 1;
                }
                else
                {
                    pointColor = new Color(0.6f, 0.8f, 1.0f);
                }

                DrawCircle(screenPos, radius, pointColor);
                DrawCircle(screenPos, radius, new Color(0.2f, 0.2f, 0.2f), false, 1.5f);
            }

            // Instructions
            string instructions = "Click to add point | Right-click to remove | Drag to move";
            DrawString(ThemeDB.FallbackFont, new Vector2(PADDING, PADDING - 8), instructions,
                HorizontalAlignment.Left, -1, 10, new Color(0.5f, 0.5f, 0.5f));
        }

        private Vector2 CurveToScreen(Vector2 curvePos, float graphW, float graphH)
        {
            return new Vector2(
                PADDING + curvePos.X * graphW,
                PADDING + (1f - curvePos.Y) * graphH
            );
        }

        private Vector2 ScreenToCurve(Vector2 screenPos, float graphW, float graphH)
        {
            return new Vector2(
                Mathf.Clamp((screenPos.X - PADDING) / graphW, 0f, 1f),
                Mathf.Clamp(1f - (screenPos.Y - PADDING) / graphH, 0f, 1f)
            );
        }

        public override void _GuiInput(InputEvent @event)
        {
            var rect = GetRect();
            float graphW = rect.Size.X - PADDING * 2;
            float graphH = rect.Size.Y - PADDING * 2;

            if (@event is InputEventMouseMotion motionEvent)
            {
                Vector2 mousePos = motionEvent.Position;

                if (_dragging && _selectedPoint >= 0)
                {
                    // Move the selected point
                    Vector2 curvePos = ScreenToCurve(mousePos, graphW, graphH);
                    Vector2 currentPos = _curve.GetPointPosition(_selectedPoint);

                    // Determine X constraints
                    float targetX = curvePos.X;

                    // First and last points stay at X=0 and X=1
                    if (_selectedPoint == 0)
                    {
                        targetX = 0f;
                    }
                    else if (_selectedPoint == _curve.PointCount - 1)
                    {
                        targetX = 1f;
                    }
                    else
                    {
                        // Constrain X to stay between neighbors
                        float prevX = _curve.GetPointPosition(_selectedPoint - 1).X;
                        float nextX = _curve.GetPointPosition(_selectedPoint + 1).X;
                        targetX = Mathf.Clamp(curvePos.X, prevX + 0.01f, nextX - 0.01f);
                    }

                    curvePos.X = targetX;

                    // Check if X position needs to change (interior points only)
                    bool needsRepositioning = _selectedPoint > 0 &&
                                              _selectedPoint < _curve.PointCount - 1 &&
                                              Mathf.Abs(curvePos.X - currentPos.X) > 0.001f;

                    if (needsRepositioning)
                    {
                        // Save tangent info before removing
                        float leftTangent = _curve.GetPointLeftTangent(_selectedPoint);
                        float rightTangent = _curve.GetPointRightTangent(_selectedPoint);

                        // Remove old point
                        _curve.RemovePoint(_selectedPoint);

                        // Add at new position - Curve auto-sorts by X and returns new index
                        _selectedPoint = _curve.AddPoint(curvePos, leftTangent, rightTangent);
                    }
                    else
                    {
                        // Just update Y value
                        _curve.SetPointValue(_selectedPoint, curvePos.Y);
                    }

                    QueueRedraw();
                    AcceptEvent();
                }
                else if (_draggingInHandle && _selectedPoint >= 0)
                {
                    // Adjust in tangent
                    Vector2 pointScreen = CurveToScreen(_curve.GetPointPosition(_selectedPoint), graphW, graphH);
                    Vector2 delta = mousePos - pointScreen;

                    if (delta.X < -5f) // Only if dragging left
                    {
                        float tangent = -delta.Y / Mathf.Abs(delta.X);
                        _curve.SetPointLeftTangent(_selectedPoint, tangent);
                        QueueRedraw();
                    }
                    AcceptEvent();
                }
                else if (_draggingOutHandle && _selectedPoint >= 0)
                {
                    // Adjust out tangent
                    Vector2 pointScreen = CurveToScreen(_curve.GetPointPosition(_selectedPoint), graphW, graphH);
                    Vector2 delta = mousePos - pointScreen;

                    if (delta.X > 5f) // Only if dragging right
                    {
                        float tangent = -delta.Y / Mathf.Abs(delta.X);
                        _curve.SetPointRightTangent(_selectedPoint, tangent);
                        QueueRedraw();
                    }
                    AcceptEvent();
                }
                else
                {
                    // Update hover state
                    int newHovered = GetPointAtPosition(mousePos, graphW, graphH);
                    if (newHovered != _hoveredPoint)
                    {
                        _hoveredPoint = newHovered;
                        QueueRedraw();
                    }
                }
            }
            else if (@event is InputEventMouseButton buttonEvent)
            {
                Vector2 mousePos = buttonEvent.Position;

                if (buttonEvent.ButtonIndex == MouseButton.Left)
                {
                    if (buttonEvent.Pressed)
                    {
                        int clickedPoint = GetPointAtPosition(mousePos, graphW, graphH);

                        if (clickedPoint >= 0)
                        {
                            // Check if clicking on a handle
                            if (_selectedPoint == clickedPoint)
                            {
                                // Check tangent handles
                                Vector2 pointScreen = CurveToScreen(_curve.GetPointPosition(_selectedPoint), graphW, graphH);

                                // In handle
                                if (_selectedPoint > 0)
                                {
                                    float inTangent = _curve.GetPointLeftTangent(_selectedPoint);
                                    Vector2 inHandleDir = new Vector2(-1, inTangent).Normalized();
                                    Vector2 inHandlePos = pointScreen + inHandleDir * 30f;

                                    if (mousePos.DistanceTo(inHandlePos) < HANDLE_RADIUS * 2)
                                    {
                                        _draggingInHandle = true;
                                        AcceptEvent();
                                        return;
                                    }
                                }

                                // Out handle
                                if (_selectedPoint < _curve.PointCount - 1)
                                {
                                    float outTangent = _curve.GetPointRightTangent(_selectedPoint);
                                    Vector2 outHandleDir = new Vector2(1, -outTangent).Normalized();
                                    Vector2 outHandlePos = pointScreen + outHandleDir * 30f;

                                    if (mousePos.DistanceTo(outHandlePos) < HANDLE_RADIUS * 2)
                                    {
                                        _draggingOutHandle = true;
                                        AcceptEvent();
                                        return;
                                    }
                                }
                            }

                            // Select and start dragging the point
                            _selectedPoint = clickedPoint;
                            _dragging = true;
                            QueueRedraw();
                        }
                        else
                        {
                            // Add new point
                            Vector2 curvePos = ScreenToCurve(mousePos, graphW, graphH);

                            // Don't add at exact X=0 or X=1 if those points exist
                            bool tooCloseToStart = curvePos.X < 0.01f &&
                                                   _curve.PointCount > 0 &&
                                                   _curve.GetPointPosition(0).X < 0.01f;
                            bool tooCloseToEnd = curvePos.X > 0.99f &&
                                                 _curve.PointCount > 0 &&
                                                 _curve.GetPointPosition(_curve.PointCount - 1).X > 0.99f;

                            if (!tooCloseToStart && !tooCloseToEnd)
                            {
                                // AddPoint returns the index where the point was inserted
                                _selectedPoint = _curve.AddPoint(curvePos, 0, 0);
                                QueueRedraw();
                            }
                        }
                        AcceptEvent();
                    }
                    else
                    {
                        // Release
                        _dragging = false;
                        _draggingInHandle = false;
                        _draggingOutHandle = false;
                    }
                }
                else if (buttonEvent.ButtonIndex == MouseButton.Right && buttonEvent.Pressed)
                {
                    // Remove point (but keep at least 2)
                    int clickedPoint = GetPointAtPosition(mousePos, graphW, graphH);

                    if (clickedPoint >= 0 && _curve.PointCount > 2)
                    {
                        // Don't remove first or last point
                        if (clickedPoint > 0 && clickedPoint < _curve.PointCount - 1)
                        {
                            _curve.RemovePoint(clickedPoint);
                            _selectedPoint = -1;
                            QueueRedraw();
                        }
                    }
                    AcceptEvent();
                }
            }
        }

        private int GetPointAtPosition(Vector2 screenPos, float graphW, float graphH)
        {
            for (int i = 0; i < _curve.PointCount; i++)
            {
                Vector2 pointScreen = CurveToScreen(_curve.GetPointPosition(i), graphW, graphH);

                if (screenPos.DistanceTo(pointScreen) < POINT_RADIUS + 4)
                {
                    return i;
                }
            }
            return -1;
        }
    }
}