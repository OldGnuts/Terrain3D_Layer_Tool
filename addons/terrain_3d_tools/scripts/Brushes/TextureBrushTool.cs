// /Brushes/TextureBrushTool.cs
using Godot;
using System.Collections.Generic;
using Terrain3DTools.Core;
using Terrain3DTools.Core.Debug;
using Terrain3DTools.Layers;
using Terrain3DTools.Layers.ManualEdit;

namespace Terrain3DTools.Brushes
{
    /// <summary>
    /// Brush tool for painting terrain textures.
    /// Uses GPU compute for efficient texture modification with fast path dual-write.
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

        public void BeginStroke(ManualEditLayer layer, Vector3 worldPos, BrushSettings settings, BrushFastPathContext fastPath)
        {
            _strokeState.Begin(layer, BrushUndoType.Texture);
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
                GD.PrintErr("[TextureBrushTool] Region size is 0!");
                return;
            }

            float brushRadius = settings.Size * 0.5f;
            float strength = settings.Strength;

            var affectedRegions = GetAffectedRegions(worldPos, brushRadius, regionSize);

            foreach (var regionCoords in affectedRegions)
            {
                // Get edit buffer (for undo tracking)
                var buffer = layer.GetOrCreateEditBuffer(regionCoords);
                if (buffer == null)
                {
                    GD.PrintErr($"[TextureBrushTool] Failed to get/create edit buffer for region {regionCoords}");
                    continue;
                }

                var textureEditRid = buffer.GetOrCreateTextureEdit();
                if (!textureEditRid.IsValid)
                {
                    GD.PrintErr($"[TextureBrushTool] Texture edit RID invalid for region {regionCoords}");
                    continue;
                }

                // Get region data (for display) - REQUIRED
                if (fastPath?.GetRegionData == null)
                {
                    GD.PrintErr($"[TextureBrushTool] FastPath context not available");
                    continue;
                }

                var regionData = fastPath.GetRegionData(regionCoords);
                if (regionData == null || !regionData.ControlMap.IsValid)
                {
                    GD.PrintErr($"[TextureBrushTool] No valid ControlMap for region {regionCoords}");
                    continue;
                }

                // Map TextureBrushMode to shader mode int
                int textureMode = settings.TextureMode switch
                {
                    TextureBrushMode.PaintOverlay => 0,    // MODE_OVERLAY
                    TextureBrushMode.PaintBase => 1,       // MODE_BASE
                    TextureBrushMode.AdjustBlend => 2,     // MODE_ADJUST_BLEND
                    TextureBrushMode.FullReplace => 3,     // MODE_FULL_REPLACE
                    _ => 0
                };

                // Determine primary and secondary texture IDs based on mode
                int primaryTextureId;
                int secondaryTextureId;

                switch (settings.TextureMode)
                {
                    case TextureBrushMode.PaintOverlay:
                        primaryTextureId = settings.TextureId;
                        secondaryTextureId = 0;
                        break;
                    case TextureBrushMode.PaintBase:
                        primaryTextureId = settings.TextureId;
                        secondaryTextureId = 0;
                        break;
                    case TextureBrushMode.AdjustBlend:
                        primaryTextureId = 0;
                        secondaryTextureId = 0;
                        break;
                    case TextureBrushMode.FullReplace:
                        primaryTextureId = settings.TextureId;
                        secondaryTextureId = settings.SecondaryTextureId;
                        break;
                    default:
                        primaryTextureId = settings.TextureId;
                        secondaryTextureId = settings.SecondaryTextureId;
                        break;
                }

                // Use the unified dispatcher with threshold settings
                Rect2I dabBounds = BrushComputeDispatcher.DispatchTextureBrushUnified(
                    textureEditRid,
                    regionData.ControlMap,
                    regionCoords,
                    regionSize,
                    worldPos,
                    brushRadius,
                    strength,
                    settings.GetFalloffTypeInt(),
                    settings.Shape == BrushShape.Circle,
                    textureMode,
                    primaryTextureId,
                    secondaryTextureId,
                    settings.TargetBlend,
                    settings.BlendStep,
                    settings.OverlayMinVisibleBlend,
                    settings.BaseMaxVisibleBlend,
                    settings.BaseOverrideThreshold,
                    settings.OverlayOverrideThreshold,
                    settings.BlendReductionRate
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