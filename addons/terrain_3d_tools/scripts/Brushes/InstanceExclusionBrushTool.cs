// /Brushes/InstanceExclusionBrushTool.cs
using Godot;
using System;
using System.Collections.Generic;
using Terrain3DTools.Core;
using Terrain3DTools.Core.Debug;
using Terrain3DTools.Layers;
using Terrain3DTools.Layers.ManualEdit;

namespace Terrain3DTools.Brushes
{
    /// <summary>
    /// Brush tool for painting instance exclusion zones.
    /// Blocks procedural instance placement in painted areas.
    /// Can also erase exclusion (allow instances again).
    /// </summary>
    public class InstanceExclusionBrushTool : IBrushTool
    {
        private const string DEBUG_CLASS_NAME = "InstanceExclusionBrushTool";

        private readonly BrushStrokeState _strokeState = new();

        public string ToolName => "Exclude Instances";
        public bool IsStrokeActive => _strokeState.IsActive;

        public InstanceExclusionBrushTool()
        {
            DebugManager.Instance?.RegisterClass(DEBUG_CLASS_NAME);
        }

        public void BeginStroke(ManualEditLayer layer, Vector3 worldPos, BrushSettings settings)
        {
            _strokeState.Begin(layer, BrushUndoType.InstanceExclusion);
            _strokeState.UpdatePosition(worldPos);

            ApplyBrush(layer, worldPos, settings);
        }

        public void ContinueStroke(ManualEditLayer layer, Vector3 worldPos, BrushSettings settings)
        {
            if (!_strokeState.IsActive) return;

            // Interpolate between last position and current for smooth strokes
            Vector3 lastPos = _strokeState.GetLastPosition();
            float distance = lastPos.DistanceTo(worldPos);
            float stepSize = settings.Size * 0.25f;

            if (distance > stepSize)
            {
                int steps = Mathf.CeilToInt(distance / stepSize);
                for (int i = 1; i <= steps; i++)
                {
                    float t = (float)i / steps;
                    Vector3 interpPos = lastPos.Lerp(worldPos, t);
                    ApplyBrush(layer, interpPos, settings);
                }
            }
            else
            {
                ApplyBrush(layer, worldPos, settings);
            }

            _strokeState.UpdatePosition(worldPos);
        }

        public BrushUndoData EndStroke(ManualEditLayer layer)
        {
            return _strokeState.End("Paint exclusion zone");
        }

        public void CancelStroke()
        {
            _strokeState.Cancel();
        }

        private void ApplyBrush(ManualEditLayer layer, Vector3 worldPos, BrushSettings settings)
        {
            int regionSize = layer.RegionSize;
            if (regionSize <= 0)
            {
                GD.PrintErr("[InstanceExclusionBrushTool] Region size is 0!");
                return;
            }

            // Use ExclusionBrushSize if set, otherwise fall back to general Size
            float brushRadius = (settings.ExclusionBrushSize > 0 ? settings.ExclusionBrushSize : settings.Size) * 0.5f;

            // Find all regions the brush touches
            var affectedRegions = GetAffectedRegions(worldPos, brushRadius, regionSize);

            foreach (var regionCoords in affectedRegions)
            {
                var buffer = layer.GetOrCreateEditBuffer(regionCoords);
                if (buffer == null)
                {
                    GD.PrintErr($"[InstanceExclusionBrushTool] Failed to get/create edit buffer for region {regionCoords}");
                    continue;
                }

                // Ensure exclusion map exists
                var exclusionRid = buffer.GetOrCreateInstanceExclusion();
                if (!exclusionRid.IsValid)
                {
                    GD.PrintErr($"[InstanceExclusionBrushTool] Exclusion RID invalid for region {regionCoords}");
                    continue;
                }

                // Capture before state for undo
                _strokeState.MarkRegionAffected(regionCoords, buffer);

                // Apply brush to this region
                ApplyBrushToRegion(
                    exclusionRid,
                    regionCoords,
                    regionSize,
                    worldPos,
                    brushRadius,
                    settings);

                layer.MarkRegionEdited(regionCoords);
            }
        }

        private void ApplyBrushToRegion(
            Rid exclusionRid,
            Vector2I regionCoords,
            int regionSize,
            Vector3 worldPos,
            float brushRadius,
            BrushSettings settings)
        {
            // Calculate region bounds in world space
            float regionMinX = regionCoords.X * regionSize;
            float regionMinZ = regionCoords.Y * regionSize;

            // Calculate brush bounds in pixel space for this region
            int minPx = Mathf.Max(0, Mathf.FloorToInt(worldPos.X - brushRadius - regionMinX));
            int maxPx = Mathf.Min(regionSize - 1, Mathf.CeilToInt(worldPos.X + brushRadius - regionMinX));
            int minPz = Mathf.Max(0, Mathf.FloorToInt(worldPos.Z - brushRadius - regionMinZ));
            int maxPz = Mathf.Min(regionSize - 1, Mathf.CeilToInt(worldPos.Z + brushRadius - regionMinZ));

            if (minPx > maxPx || minPz > maxPz) return;

            // Read current data
            byte[] currentData = null;
            try
            {
                currentData = Gpu.Rd.TextureGetData(exclusionRid, 0);
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[InstanceExclusionBrushTool] Failed to read texture data: {ex.Message}");
                return;
            }

            if (currentData == null || currentData.Length == 0)
            {
                GD.PrintErr("[InstanceExclusionBrushTool] Current data is null or empty");
                return;
            }

            float[] exclusionValues = BytesToFloats(currentData);
            bool modified = false;

            // Determine if we're adding or removing exclusion
            // ExclusionMode: true = add exclusion (block instances), false = remove exclusion (allow instances)
            bool addingExclusion = settings.ExclusionMode;
            float targetValue = addingExclusion ? 1.0f : 0.0f;

            // Apply brush
            for (int pz = minPz; pz <= maxPz; pz++)
            {
                for (int px = minPx; px <= maxPx; px++)
                {
                    // World position of this pixel
                    float worldX = regionMinX + px + 0.5f;
                    float worldZ = regionMinZ + pz + 0.5f;

                    // Distance from brush center
                    float dx = worldX - worldPos.X;
                    float dz = worldZ - worldPos.Z;
                    float distance = Mathf.Sqrt(dx * dx + dz * dz);

                    // Check brush shape
                    bool inBrush = settings.Shape == BrushShape.Circle
                        ? distance <= brushRadius
                        : Mathf.Abs(dx) <= brushRadius && Mathf.Abs(dz) <= brushRadius;

                    if (!inBrush) continue;

                    // Calculate falloff
                    float normalizedDist = distance / brushRadius;
                    float falloff = settings.CalculateFalloff(normalizedDist);

                    // Calculate effective strength
                    float effectiveStrength = settings.Strength * falloff;
                    if (effectiveStrength < 0.01f) continue;

                    int idx = pz * regionSize + px;
                    if (idx >= 0 && idx < exclusionValues.Length)
                    {
                        float currentValue = exclusionValues[idx];
                        float newValue;

                        if (settings.ExclusionAccumulate)
                        {
                            // Accumulate toward target
                            if (addingExclusion)
                            {
                                newValue = Mathf.Min(1.0f, currentValue + effectiveStrength * 0.2f);
                            }
                            else
                            {
                                newValue = Mathf.Max(0.0f, currentValue - effectiveStrength * 0.2f);
                            }
                        }
                        else
                        {
                            // Direct lerp to target
                            newValue = Mathf.Lerp(currentValue, targetValue, effectiveStrength);
                        }

                        exclusionValues[idx] = Mathf.Clamp(newValue, 0.0f, 1.0f);
                        modified = true;
                    }
                }
            }

            if (modified)
            {
                byte[] newData = FloatsToBytes(exclusionValues);
                try
                {
                    Gpu.Rd.TextureUpdate(exclusionRid, 0, newData);
                }
                catch (Exception ex)
                {
                    GD.PrintErr($"[InstanceExclusionBrushTool] Failed to update texture: {ex.Message}");
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

        #region Byte/Float Conversion

        private float[] BytesToFloats(byte[] bytes)
        {
            float[] floats = new float[bytes.Length / 4];
            Buffer.BlockCopy(bytes, 0, floats, 0, bytes.Length);
            return floats;
        }

        private byte[] FloatsToBytes(float[] floats)
        {
            byte[] bytes = new byte[floats.Length * 4];
            Buffer.BlockCopy(floats, 0, bytes, 0, bytes.Length);
            return bytes;
        }

        #endregion
    }
}