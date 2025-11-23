using Godot;

namespace Terrain3DTools.Utils
{
    public static class CurveUtils
    {
        public static Curve CreateLinearCurve()
        {
            var curve = new Curve();
            curve.AddPoint(Vector2.Zero);
            curve.AddPoint(Vector2.One);
            return curve;
        }

        public static Curve CreateBellCurve()
        {
            var curve = new Curve();
            curve.AddPoint(new Vector2(0, 0));
            curve.AddPoint(new Vector2(0.5f, 1.0f));
            curve.AddPoint(new Vector2(1, 0));
            return curve;
        }

        public static Curve CreateEaseInOutCurve()
        {
            var curve = new Curve();
            curve.AddPoint(Vector2.Zero, 0, 0, Curve.TangentMode.Linear);
            curve.AddPoint(new Vector2(0.3f, 0.1f), 0, 0, Curve.TangentMode.Linear);
            curve.AddPoint(new Vector2(0.7f, 0.9f), 0, 0, Curve.TangentMode.Linear);
            curve.AddPoint(Vector2.One, 0, 0, Curve.TangentMode.Linear);
            return curve;
        }
    }
}