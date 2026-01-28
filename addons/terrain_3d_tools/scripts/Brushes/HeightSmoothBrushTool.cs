// /Brushes/HeightSmoothBrushTool.cs
using Godot;
using Terrain3DTools.Layers;

namespace Terrain3DTools.Brushes
{
    /// <summary>
    /// Brush tool for smoothing terrain height.
    /// Averages height values in the brush area.
    /// </summary>
    public class HeightSmoothBrushTool : IBrushTool
    {
        private readonly BrushStrokeState _strokeState = new();
        
        public string ToolName => "Smooth Height";
        public bool IsStrokeActive => _strokeState.IsActive;
        
        public void BeginStroke(ManualEditLayer layer, Vector3 worldPos, BrushSettings settings)
        {
            _strokeState.Begin(layer, BrushUndoType.Height);
            _strokeState.UpdatePosition(worldPos);
            // TODO: Implement smoothing logic
        }
        
        public void ContinueStroke(ManualEditLayer layer, Vector3 worldPos, BrushSettings settings)
        {
            if (!_strokeState.IsActive) return;
            _strokeState.UpdatePosition(worldPos);
            // TODO: Implement smoothing logic
        }
        
        public BrushUndoData EndStroke(ManualEditLayer layer)
        {
            return _strokeState.End("Smooth height brush stroke");
        }
        
        public void CancelStroke()
        {
            _strokeState.Cancel();
        }
    }
}