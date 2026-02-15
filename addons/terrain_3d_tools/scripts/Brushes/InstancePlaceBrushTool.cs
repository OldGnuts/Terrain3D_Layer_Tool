// /Brushes/InstancePlaceBrushTool.cs
using Godot;
using System.Collections.Generic;
using Terrain3DTools.Core;
using Terrain3DTools.Core.Debug;
using Terrain3DTools.Layers;
using Terrain3DTools.Layers.ManualEdit;

namespace Terrain3DTools.Brushes
{
    /// <summary>
    /// Brush tool for manually placing instances.
    /// Does not use GPU compute - places instances directly into ManualEditBuffer.
    /// </summary>
    public class InstancePlaceBrushTool : IBrushTool
    {
        private const string DEBUG_CLASS_NAME = "InstancePlaceBrushTool";

        private readonly BrushStrokeState _strokeState = new();
        private List<InstanceRecord> _placedThisStroke = new();

        public string ToolName => "Place Instance";
        public bool IsStrokeActive => _strokeState.IsActive;

        public InstancePlaceBrushTool()
        {
            DebugManager.Instance?.RegisterClass(DEBUG_CLASS_NAME);
        }

        public void BeginStroke(ManualEditLayer layer, Vector3 worldPos, BrushSettings settings, BrushFastPathContext fastPath)
        {
            _strokeState.Begin(layer, BrushUndoType.InstancePlacement);
            _strokeState.UpdatePosition(worldPos);
            _placedThisStroke.Clear();

            PlaceInstance(layer, worldPos, settings);
        }

        public void ContinueStroke(ManualEditLayer layer, Vector3 worldPos, BrushSettings settings, BrushFastPathContext fastPath)
        {
            if (!_strokeState.IsActive) return;

            Vector3 lastPos = _strokeState.GetLastPosition();
            float distance = lastPos.DistanceTo(worldPos);
            
            // Use InstanceMinDistance for spacing
            float minSpacing = settings.InstanceMinDistance;

            // Only place if we've moved far enough
            if (distance >= minSpacing)
            {
                PlaceInstance(layer, worldPos, settings);
                _strokeState.UpdatePosition(worldPos);
            }
        }

        public BrushUndoData EndStroke(ManualEditLayer layer)
        {
            var undoData = _strokeState.End($"{ToolName} brush stroke");

            if (undoData != null)
            {
                undoData.PlacedInstances = new List<InstanceRecord>(_placedThisStroke);
            }

            _placedThisStroke.Clear();

            // Trigger pipeline to push instances to Terrain3D
            layer?.ForceDirty();

            return undoData;
        }

        public void CancelStroke()
        {
            // Remove all instances placed this stroke
            var layer = _strokeState.Layer;
            if (layer != null)
            {
                foreach (var record in _placedThisStroke)
                {
                    var buffer = layer.GetEditBuffer(record.RegionCoords);
                    buffer?.RemoveClosestInstance(record.MeshId, record.Transform.Origin, 0.1f);
                }
            }

            _placedThisStroke.Clear();
            _strokeState.Cancel();
        }

        private void PlaceInstance(ManualEditLayer layer, Vector3 worldPos, BrushSettings settings)
        {
            int regionSize = layer.RegionSize;
            if (regionSize <= 0) return;

            Vector2I regionCoords = new Vector2I(
                Mathf.FloorToInt(worldPos.X / regionSize),
                Mathf.FloorToInt(worldPos.Z / regionSize)
            );

            var buffer = layer.GetOrCreateEditBuffer(regionCoords);
            if (buffer == null) return;

            // Create transform with random rotation and scale variation
            var random = new RandomNumberGenerator();
            random.Randomize();

            // Use RandomRotation property
            float rotationY = settings.RandomRotation
                ? random.RandfRange(0f, Mathf.Tau)
                : 0f;

            // Use RandomScaleVariation and InstanceScale properties
            float scale = settings.InstanceScale;
            if (settings.RandomScaleVariation > 0f)
            {
                scale *= random.RandfRange(
                    1f - settings.RandomScaleVariation,
                    1f + settings.RandomScaleVariation
                );
            }

            var transform = new Transform3D(
                Basis.Identity.Rotated(Vector3.Up, rotationY).Scaled(new Vector3(scale, scale, scale)),
                worldPos
            );

            buffer.AddInstance(settings.InstanceMeshId, transform);

            var record = new InstanceRecord
            {
                RegionCoords = regionCoords,
                MeshId = settings.InstanceMeshId,
                Transform = transform
            };
            _placedThisStroke.Add(record);

            _strokeState.MarkRegionAffected(regionCoords, buffer);
            layer.MarkRegionEdited(regionCoords);

            DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.UpdateCycle,
                $"Placed instance at {worldPos} in region {regionCoords}");
        }
    }
}