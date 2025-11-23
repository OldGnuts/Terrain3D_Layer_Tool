// /Layers/PathLayerMaskGeneration.cs
using Godot;
using System;
using System.Collections.Generic;
using Terrain3DTools.Core;
using Terrain3DTools.Utils;

namespace Terrain3DTools.Layers
{
    public partial class PathLayer : FeatureLayer
    {
        /// <summary>
        /// Creates GPU commands to rasterize the entire path into the layer's mask texture.
        /// This is called during the mask generation phase, similar to other layer types.
        /// The mask stores path information across the entire layer bounds.
        /// </summary>
        public (Action<long> commands, List<Rid> tempRids, List<string> shaderPaths) CreatePathMaskCommands()
        {
            if (Path3D?.Curve == null)
            {
                ErrorPrint("Path3D or Curve is null during mask generation");
                return (null, new List<Rid>(), new List<string>());
            }

            if (!layerTextureRID.IsValid)
            {
                ErrorPrint("layerTextureRID is invalid during mask generation");
                return (null, new List<Rid>(), new List<string>());
            }

            // Generate path segments in layer-local space
            var pathSegments = GeneratePathSegmentsInLayerSpace();
            if (pathSegments.Length == 0)
            {
                WarningPrint("No path segments generated for mask");
                return (null, new List<Rid>(), new List<string>());
            }

            int segmentCount = pathSegments.Length / (sizeof(float) * 10);

            if (_debugMode)
            {
                // DEBUG: Print embankment settings
                GD.Print($"[PathLayer {LayerName}] Embankment Debug:");
                GD.Print($"  CreateEmbankments: {_createEmbankments}");
                GD.Print($"  EmbankmentWidth: {_embankmentWidth}");
                GD.Print($"  EmbankmentHeight: {_embankmentHeight}");
                GD.Print($"  EmbankmentFalloff: {_embankmentFalloff}");
                GD.Print($"  PathWidth: {_pathWidth}");
                GD.Print($"  Segment count: {segmentCount}");
            }
            var shaderPath = "res://addons/terrain_3d_tools/Shaders/Layers/PathMaskGeneration.glsl";
            var operation = new AsyncComputeOperation(shaderPath);

            // Bind output texture
            operation.BindStorageImage(0, layerTextureRID);

            // Bind path segment data
            operation.BindTemporaryStorageBuffer(1, pathSegments);

            // Bind carve curve data
            var carveCurveData = GenerateCurveData(_carveCurve, 256);
            operation.BindTemporaryStorageBuffer(2, carveCurveData);

            // Bind embankment curve data
            var embankmentCurveData = GenerateCurveData(_embankmentCurve, 256);
            operation.BindTemporaryStorageBuffer(3, embankmentCurveData);

            // Push constants for mask generation
            var pushConstants = GpuUtils.CreatePushConstants()
                .Add(segmentCount)
                .Add(_pathWidth)
                .Add(_carveStrength)
                .Add(_pathElevation)
                .Add(_terrainConformance)
                .Add(_createEmbankments ? 1 : 0)  // DEBUG: Verify this conversion
                .Add(_embankmentWidth)
                .Add(_embankmentHeight)
                .Add(_embankmentFalloff)
                .Add(_riverDepth)
                .Add(_roadCamber)
                .Add((int)_textureMode)
                .Add((uint)_centerTextureId)
                .Add((uint)_embankmentTextureId)
                .Add((float)Size.X)
                .Add((float)Size.Y)
                .Build();

            if (_debugMode)
            {
                GD.Print($"  Push constants size: {pushConstants.Length} bytes (expected: 64)");
            }
            operation.SetPushConstants(pushConstants);

            uint groupsX = (uint)((Size.X + 7) / 8);
            uint groupsY = (uint)((Size.Y + 7) / 8);

            return (operation.CreateDispatchCommands(groupsX, groupsY),
                    operation.GetTemporaryRids(),
                    new List<string> { shaderPath });
        }

        /// <summary>
        /// Generates path segments in layer-local space (0,0 to Size.X, Size.Y)
        /// Each segment stores: startX, startY, startHeight, endX, endY, endHeight, width, flowDirection
        /// </summary>
        private byte[] GeneratePathSegmentsInLayerSpace()
        {
            if (Path3D?.Curve == null) return new byte[0];

            var curve = Path3D.Curve;
            var segments = new List<float>();

            // Layer bounds in world space
            Vector2 layerCenter = new Vector2(GlobalPosition.X, GlobalPosition.Z);
            Vector2 layerMin = layerCenter - new Vector2(Size.X, Size.Y) * 0.5f;

            int samples = _adaptiveResolution ? CalculateAdaptiveSamples(curve) : _pathResolution;

            DebugPrint($"Generating path mask segments:");
            DebugPrint($"  Layer center: {layerCenter}");
            DebugPrint($"  Layer min: {layerMin}");
            DebugPrint($"  Layer size: {Size}");
            DebugPrint($"  Samples: {samples}");

            for (int i = 0; i < samples - 1; i++)
            {
                float t1 = (float)i / (samples - 1);
                float t2 = (float)(i + 1) / (samples - 1);

                var worldPos1 = Path3D.ToGlobal(curve.SampleBaked(t1 * curve.GetBakedLength()));
                var worldPos2 = Path3D.ToGlobal(curve.SampleBaked(t2 * curve.GetBakedLength()));

                // Convert to layer-local space
                var layerPos1 = WorldToLayerSpace(new Vector2(worldPos1.X, worldPos1.Z), layerMin);
                var layerPos2 = WorldToLayerSpace(new Vector2(worldPos2.X, worldPos2.Z), layerMin);

                // Debug first few segments
                if (i < 3)
                {
                    DebugPrint($"  Segment {i}: layer({layerPos1.X:F1}, {layerPos1.Y:F1}) -> ({layerPos2.X:F1}, {layerPos2.Y:F1})");
                }

                // IMPORTANT: Match GLSL struct layout with padding
                segments.Add(layerPos1.X);                      // start_pos.x
                segments.Add(layerPos1.Y);                      // start_pos.y
                segments.Add(worldPos1.Y / _worldHeightScale);  // start_height
                segments.Add(0.0f);                             // _padding1 (ADDED)

                segments.Add(layerPos2.X);                      // end_pos.x
                segments.Add(layerPos2.Y);                      // end_pos.y
                segments.Add(worldPos2.Y / _worldHeightScale);  // end_height
                segments.Add(1.0f);                             // width_mult

                segments.Add(CalculateFlowDirection(layerPos1, layerPos2)); // flow_direction
                segments.Add(0.0f);                             // _padding2 (ADDED)
            }

            int segmentCount = segments.Count / 10; // Now 10 floats per segment (was 8)
            DebugPrint($"Generated {segmentCount} segments ({segments.Count} floats total)");

            return GpuUtils.FloatArrayToBytes(segments.ToArray());
        }
        private Vector2 WorldToLayerSpace(Vector2 worldPos, Vector2 layerMin)
        {
            return worldPos - layerMin;
        }

        /// <summary>
        /// Creates GPU commands to rasterize path HEIGHT data into a separate texture.
        /// This runs independently of the mask pipeline.
        /// </summary>
        public (Action<long> commands, List<Rid> tempRids, List<string> shaderPaths) CreatePathHeightDataCommands()
        {
            if (Path3D?.Curve == null || !layerHeightDataRID.IsValid)
            {
                return (null, new List<Rid>(), new List<string>());
            }

            var pathSegments = GeneratePathSegmentsInLayerSpace();
            if (pathSegments.Length == 0)
            {
                return (null, new List<Rid>(), new List<string>());
            }

            int segmentCount = pathSegments.Length / (sizeof(float) * 10);

            var shaderPath = "res://addons/terrain_3d_tools/Shaders/Layers/PathHeightData.glsl";
            var operation = new AsyncComputeOperation(shaderPath);

            operation.BindStorageImage(0, layerHeightDataRID);
            operation.BindTemporaryStorageBuffer(1, pathSegments);

            var pushConstants = GpuUtils.CreatePushConstants()
                .Add(segmentCount)
                .Add(_pathWidth)
                .Add((float)Size.X)
                .Add((float)Size.Y)
                .Build();

            operation.SetPushConstants(pushConstants);

            uint groupsX = (uint)((Size.X + 7) / 8);
            uint groupsY = (uint)((Size.Y + 7) / 8);

            return (operation.CreateDispatchCommands(groupsX, groupsY),
                    operation.GetTemporaryRids(),
                    new List<string> { shaderPath });
        }
    }
}