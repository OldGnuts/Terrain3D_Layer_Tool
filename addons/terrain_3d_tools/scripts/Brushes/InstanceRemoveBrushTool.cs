// /Brushes/InstanceEraseBrushTool.cs
using Godot;
using Terrain3DTools.Layers;

namespace Terrain3DTools.Brushes
{
    /// <summary>
    /// Brush tool for removing manually placed instances.
    /// </summary>
    public class InstanceEraseBrushTool : IBrushTool
    {
        private readonly BrushStrokeState _strokeState = new();
        
        public string ToolName => "Erase Instance";
        public bool IsStrokeActive => _strokeState.IsActive;
        
        public void BeginStroke(ManualEditLayer layer, Vector3 worldPos, BrushSettings settings)
        {
            _strokeState.Begin(layer, BrushUndoType.InstancePlacement);
            _strokeState.UpdatePosition(worldPos);
            // TODO: Implement instance removal logic
        }
        
        public void ContinueStroke(ManualEditLayer layer, Vector3 worldPos, BrushSettings settings)
        {
            // Instance removal typically only happens on click, not drag
        }
        
        public BrushUndoData EndStroke(ManualEditLayer layer)
        {
            return _strokeState.End("Erase instance");
        }
        
        public void CancelStroke()
        {
            _strokeState.Cancel();
        }
    }
}