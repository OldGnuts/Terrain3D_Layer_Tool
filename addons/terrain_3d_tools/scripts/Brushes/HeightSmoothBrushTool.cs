// /Brushes/HeightSmoothBrushTool.cs
using Godot;
using System.Collections.Generic;
using Terrain3DTools.Core;
using Terrain3DTools.Core.Debug;
using Terrain3DTools.Layers;
using Terrain3DTools.Layers.ManualEdit;

namespace Terrain3DTools.Brushes
{
    /// <summary>
    /// Brush tool for smoothing terrain height.
    /// Uses GPU compute with kernel-based averaging and fast path dual-write.
    /// </summary>
    public class HeightSmoothBrushTool : IBrushTool
    {
        private const string DEBUG_CLASS_NAME = "HeightSmoothBrushTool";

        private readonly BrushStrokeState _strokeState = new();

        public string ToolName => "Smooth Height";
        public bool IsStrokeActive => _strokeState.IsActive;

        public HeightSmoothBrushTool()
        {
            DebugManager.Instance?.RegisterClass(DEBUG_CLASS_NAME);
        }

        public void BeginStroke(ManualEditLayer layer, Vector3 worldPos, BrushSettings settings, BrushFastPathContext fastPath)
        {
            _strokeState.Begin(layer, BrushUndoType.Height);
            _strokeState.IsSmooth = true;
            _strokeState.UpdatePosition(worldPos);

            ApplyBrushInternal(layer, worldPos, settings, fastPath);
        }

        public void ContinueStroke(ManualEditLayer layer, Vector3 worldPos, BrushSettings settings, BrushFastPathContext fastPath)
        {
            if (!_strokeState.IsActive) return;

            Vector3 lastPos = _strokeState.GetLastPosition();
            float distance = lastPos.DistanceTo(worldPos);
            float stepSize = settings.Size * 0.25f;

            BrushComputeDispatcher.BeginBatch();

            try
            {
                if (distance > stepSize)
                {
                    int steps = Mathf.CeilToInt(distance / stepSize);
                    for (int i = 1; i <= steps; i++)
                    {
                        float t = (float)i / steps;
                        Vector3 interpPos = lastPos.Lerp(worldPos, t);
                        ApplyBrushInternal(layer, interpPos, settings, fastPath);
                    }
                }
                else
                {
                    ApplyBrushInternal(layer, worldPos, settings, fastPath);
                }
            }
            finally
            {
                BrushComputeDispatcher.EndBatch();
            }

            _strokeState.UpdatePosition(worldPos);
        }

        public BrushUndoData EndStroke(ManualEditLayer layer)
        {
            return _strokeState.End($"{ToolName} brush stroke");
        }

        public void CancelStroke()
        {
            _strokeState.Cancel();
        }

        private void ApplyBrushInternal(ManualEditLayer layer, Vector3 worldPos, BrushSettings settings, BrushFastPathContext fastPath)
        {
            int regionSize = layer.RegionSize;
            if (regionSize <= 0)
            {
                GD.PrintErr("[HeightSmoothBrushTool] Region size is 0!");
                return;
            }

            float brushRadius = settings.Size * 0.5f;
            float strength = settings.Strength;
            int kernelSize = Mathf.Clamp(settings.SmoothKernelSize, 1, 64);

            var affectedRegions = GetAffectedRegions(worldPos, brushRadius, regionSize);

            foreach (var regionCoords in affectedRegions)
            {
                // Get edit buffer (for undo tracking)
                var buffer = layer.GetOrCreateEditBuffer(regionCoords);
                if (buffer == null)
                {
                    GD.PrintErr($"[HeightSmoothBrushTool] Failed to get/create edit buffer for region {regionCoords}");
                    continue;
                }

                var heightDeltaRid = buffer.GetOrCreateHeightDelta();
                if (!heightDeltaRid.IsValid)
                {
                    GD.PrintErr($"[HeightSmoothBrushTool] Height delta RID invalid for region {regionCoords}");
                    continue;
                }

                // Get region data (for display) - REQUIRED
                if (fastPath?.GetRegionData == null)
                {
                    GD.PrintErr($"[HeightSmoothBrushTool] FastPath context not available");
                    continue;
                }

                var regionData = fastPath.GetRegionData(regionCoords);
                if (regionData == null || !regionData.HeightMap.IsValid)
                {
                    GD.PrintErr($"[HeightSmoothBrushTool] No valid HeightMap for region {regionCoords}");
                    continue;
                }

                Rect2I dabBounds = BrushComputeDispatcher.DispatchHeightSmoothBrush(
                    heightDeltaRid,
                    regionData.HeightMap,
                    regionCoords,
                    regionSize,
                    worldPos,
                    brushRadius,
                    strength,
                    kernelSize,
                    settings.GetFalloffTypeInt(),
                    settings.Shape == BrushShape.Circle
                );

                if (dabBounds.Size.X > 0 && dabBounds.Size.Y > 0)
                {
                    _strokeState.MarkRegionAffected(regionCoords, buffer, dabBounds);
                    fastPath.MarkRegionDirty?.Invoke(regionCoords);
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