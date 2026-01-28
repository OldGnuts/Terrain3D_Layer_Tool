// /Brushes/TextureBrushTool.cs
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
    /// Brush tool for painting textures.
    /// Modifies the manual edit texture map which is then applied on top of
    /// composited texture data during the pipeline.
    /// </summary>
    public class TextureBrushTool : IBrushTool
    {
        private const string DEBUG_CLASS_NAME = "TextureBrushTool";

        private readonly BrushStrokeState _strokeState = new();

        public string ToolName => "Paint Texture";
        public bool IsStrokeActive => _strokeState.IsActive;

        public TextureBrushTool()
        {
            DebugManager.Instance?.RegisterClass(DEBUG_CLASS_NAME);
        }

        public void BeginStroke(ManualEditLayer layer, Vector3 worldPos, BrushSettings settings)
        {
            _strokeState.Begin(layer, BrushUndoType.Texture);
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
            return _strokeState.End("Paint texture brush stroke");
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
                GD.PrintErr("[TextureBrushTool] Region size is 0!");
                return;
            }

            float brushRadius = settings.Size * 0.5f;

            // Find all regions the brush touches
            var affectedRegions = GetAffectedRegions(worldPos, brushRadius, regionSize);

            foreach (var regionCoords in affectedRegions)
            {
                var buffer = layer.GetOrCreateEditBuffer(regionCoords);
                if (buffer == null)
                {
                    GD.PrintErr($"[TextureBrushTool] Failed to get/create edit buffer for region {regionCoords}");
                    continue;
                }

                // Ensure texture edit map exists
                var textureEditRid = buffer.GetOrCreateTextureEdit();
                if (!textureEditRid.IsValid)
                {
                    GD.PrintErr($"[TextureBrushTool] Texture edit RID invalid for region {regionCoords}");
                    continue;
                }

                // Capture before state for undo
                _strokeState.MarkRegionAffected(regionCoords, buffer);

                // Apply brush to this region
                ApplyBrushToRegion(
                    textureEditRid,
                    regionCoords,
                    regionSize,
                    worldPos,
                    brushRadius,
                    settings);

                layer.MarkRegionEdited(regionCoords);
            }
        }

        private void ApplyBrushToRegion(
            Rid textureEditRid,
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
                currentData = Gpu.Rd.TextureGetData(textureEditRid, 0);
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[TextureBrushTool] Failed to read texture data: {ex.Message}");
                return;
            }

            if (currentData == null || currentData.Length == 0)
            {
                GD.PrintErr("[TextureBrushTool] Current data is null or empty");
                return;
            }

            uint[] editValues = BytesToUints(currentData);
            bool modified = false;

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

                    // Apply based on strength and falloff
                    float effectiveStrength = settings.Strength * falloff;
                    if (effectiveStrength < 0.01f) continue;

                    int idx = pz * regionSize + px;
                    if (idx >= 0 && idx < editValues.Length)
                    {
                        editValues[idx] = ApplyTextureEdit(
                            editValues[idx],
                            settings,
                            effectiveStrength);
                        modified = true;
                    }
                }
            }

            if (modified)
            {
                byte[] newData = UintsToBytes(editValues);
                try
                {
                    Gpu.Rd.TextureUpdate(textureEditRid, 0, newData);
                }
                catch (Exception ex)
                {
                    GD.PrintErr($"[TextureBrushTool] Failed to update texture: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Applies a texture edit to a single pixel's edit data.
        /// </summary>
        private uint ApplyTextureEdit(uint currentEdit, BrushSettings settings, float effectiveStrength)
        {
            // Decode current edit state
            ManualEditBuffer.DecodeTextureEdit(
                currentEdit,
                out uint currentBaseId, out bool currentBaseActive,
                out uint currentOverlayId, out bool currentOverlayActive,
                out uint currentBlend, out bool currentBlendActive);

            uint newBaseId = currentBaseId;
            bool newBaseActive = currentBaseActive;
            uint newOverlayId = currentOverlayId;
            bool newOverlayActive = currentOverlayActive;
            uint newBlend = currentBlend;
            bool newBlendActive = currentBlendActive;

            switch (settings.TextureMode)
            {
                case TextureBrushMode.PaintOverlay:
                    // Set overlay texture ID
                    newOverlayId = (uint)settings.TextureId;
                    newOverlayActive = true;

                    // Adjust blend toward overlay (255)
                    newBlendActive = true;
                    if (settings.AccumulateBlend)
                    {
                        int blendDelta = Mathf.RoundToInt(settings.BlendStep * effectiveStrength);
                        newBlend = (uint)Mathf.Min(255, (int)currentBlend + blendDelta);
                    }
                    else
                    {
                        // Lerp toward target based on strength
                        newBlend = (uint)Mathf.RoundToInt(Mathf.Lerp(currentBlend, settings.TargetBlend, effectiveStrength));
                    }
                    break;

                case TextureBrushMode.PaintBase:
                    // Set base texture ID
                    newBaseId = (uint)settings.TextureId;
                    newBaseActive = true;

                    // Adjust blend toward base (0)
                    newBlendActive = true;
                    if (settings.AccumulateBlend)
                    {
                        int blendDelta = Mathf.RoundToInt(settings.BlendStep * effectiveStrength);
                        newBlend = (uint)Mathf.Max(0, (int)currentBlend - blendDelta);
                    }
                    else
                    {
                        // Lerp toward 0 based on strength
                        newBlend = (uint)Mathf.RoundToInt(Mathf.Lerp(currentBlend, 0, effectiveStrength));
                    }
                    break;

                case TextureBrushMode.AdjustBlend:
                    // Only modify blend, not texture IDs
                    newBlendActive = true;
                    if (settings.AccumulateBlend)
                    {
                        // Use TargetBlend as direction: > 128 = increase, < 128 = decrease
                        int direction = settings.TargetBlend > 128 ? 1 : -1;
                        int blendDelta = Mathf.RoundToInt(settings.BlendStep * effectiveStrength) * direction;
                        newBlend = (uint)Mathf.Clamp((int)currentBlend + blendDelta, 0, 255);
                    }
                    else
                    {
                        newBlend = (uint)Mathf.RoundToInt(Mathf.Lerp(currentBlend, settings.TargetBlend, effectiveStrength));
                    }
                    break;

                case TextureBrushMode.FullReplace:
                    // Replace both textures
                    newBaseId = (uint)settings.SecondaryTextureId;
                    newBaseActive = true;
                    newOverlayId = (uint)settings.TextureId;
                    newOverlayActive = true;
                    newBlendActive = true;
                    newBlend = (uint)Mathf.RoundToInt(Mathf.Lerp(currentBlend, settings.TargetBlend, effectiveStrength));
                    break;
            }

            // Encode and return
            return ManualEditBuffer.EncodeTextureEdit(
                newBaseId, newBaseActive,
                newOverlayId, newOverlayActive,
                newBlend, newBlendActive);
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

        #region Byte/Uint Conversion

        private uint[] BytesToUints(byte[] bytes)
        {
            uint[] uints = new uint[bytes.Length / 4];
            Buffer.BlockCopy(bytes, 0, uints, 0, bytes.Length);
            return uints;
        }

        private byte[] UintsToBytes(uint[] uints)
        {
            byte[] bytes = new byte[uints.Length * 4];
            Buffer.BlockCopy(uints, 0, bytes, 0, bytes.Length);
            return bytes;
        }

        #endregion
    }
}