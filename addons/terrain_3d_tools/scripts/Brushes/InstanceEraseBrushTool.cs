// /Brushes/InstanceEraseBrushTool.cs
using Godot;
using System.Collections.Generic;
using Terrain3DTools.Core;
using Terrain3DTools.Core.Debug;
using Terrain3DTools.Layers;
using Terrain3DTools.Layers.ManualEdit;

namespace Terrain3DTools.Brushes
{
    /// <summary>
    /// Brush tool for erasing manually placed instances.
    /// Does not use GPU compute - removes instances directly from ManualEditBuffer.
    /// </summary>
    public class InstanceEraseBrushTool : IBrushTool
    {
        private const string DEBUG_CLASS_NAME = "InstanceEraseBrushTool";

        private readonly BrushStrokeState _strokeState = new();
        private List<InstanceRecord> _removedThisStroke = new();

        public string ToolName => "Erase Instance";
        public bool IsStrokeActive => _strokeState.IsActive;

        public InstanceEraseBrushTool()
        {
            DebugManager.Instance?.RegisterClass(DEBUG_CLASS_NAME);
        }

        public void BeginStroke(ManualEditLayer layer, Vector3 worldPos, BrushSettings settings, BrushFastPathContext fastPath)
        {
            _strokeState.Begin(layer, BrushUndoType.InstancePlacement);
            _strokeState.UpdatePosition(worldPos);
            _removedThisStroke.Clear();

            EraseInstances(layer, worldPos, settings);
        }

        public void ContinueStroke(ManualEditLayer layer, Vector3 worldPos, BrushSettings settings, BrushFastPathContext fastPath)
        {
            if (!_strokeState.IsActive) return;

            EraseInstances(layer, worldPos, settings);
            _strokeState.UpdatePosition(worldPos);
        }

        public BrushUndoData EndStroke(ManualEditLayer layer)
        {
            var undoData = _strokeState.End($"{ToolName} brush stroke");

            if (undoData != null)
            {
                undoData.RemovedInstances = new List<InstanceRecord>(_removedThisStroke);
            }

            _removedThisStroke.Clear();

            // Trigger pipeline to update instances in Terrain3D
            layer?.ForceDirty();

            return undoData;
        }

        public void CancelStroke()
        {
            // Re-add all removed instances
            var layer = _strokeState.Layer;
            if (layer != null)
            {
                foreach (var record in _removedThisStroke)
                {
                    var buffer = layer.GetOrCreateEditBuffer(record.RegionCoords);
                    buffer?.AddInstance(record.MeshId, record.Transform);
                }
            }

            _removedThisStroke.Clear();
            _strokeState.Cancel();
        }

        private void EraseInstances(ManualEditLayer layer, Vector3 worldPos, BrushSettings settings)
        {
            int regionSize = layer.RegionSize;
            if (regionSize <= 0) return;

            float brushRadius = settings.Size * 0.5f;

            var affectedRegions = GetAffectedRegions(worldPos, brushRadius, regionSize);

            foreach (var regionCoords in affectedRegions)
            {
                var buffer = layer.GetEditBuffer(regionCoords);
                if (buffer == null) continue;

                // Check all mesh types or just the selected one
                // Use EraseAllMeshTypes property (needs to be added to BrushSettings)
                var meshIdsToCheck = settings.EraseAllMeshTypes
                    ? new List<int>(buffer.PlacedInstances.Keys)
                    : new List<int> { settings.InstanceMeshId };

                foreach (var meshId in meshIdsToCheck)
                {
                    // Keep removing until no more instances within radius
                    while (true)
                    {
                        var removed = buffer.RemoveClosestInstance(meshId, worldPos, brushRadius);
                        if (removed == null) break;

                        var record = new InstanceRecord
                        {
                            RegionCoords = regionCoords,
                            MeshId = meshId,
                            Transform = removed.Value
                        };
                        _removedThisStroke.Add(record);

                        _strokeState.MarkRegionAffected(regionCoords, buffer);

                        DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.UpdateCycle,
                            $"Removed instance at {removed.Value.Origin} in region {regionCoords}");
                    }
                }
            }
        }

        private HashSet<Vector2I> GetAffectedRegions(Vector3 worldPos, float brushRadius, int regionSize)
        {
            var regions = new HashSet<Vector2I>();

            float minX = worldPos.X - brushRadius;
            float maxX = worldPos.X + brushRadius;
            float minZ = worldPos.Z - brushRadius;
            float maxZ = worldPos.Z + brushRadius;

            int minRegionX = Mathf.FloorToInt(minX / regionSize);
            int maxRegionX = Mathf.FloorToInt(maxX / regionSize);
            int minRegionZ = Mathf.FloorToInt(minZ / regionSize);
            int maxRegionZ = Mathf.FloorToInt(maxZ / regionSize);

            for (int rz = minRegionZ; rz <= maxRegionZ; rz++)
            {
                for (int rx = minRegionX; rx <= maxRegionX; rx++)
                {
                    regions.Add(new Vector2I(rx, rz));
                }
            }

            return regions;
        }
    }
}