// /Brushes/InstancePlaceBrushTool.cs
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
    /// Brush tool for placing individual mesh instances.
    /// Places instances on click, with optional scatter mode for drag placement.
    /// </summary>
    public class InstancePlaceBrushTool : IBrushTool
    {
        private const string DEBUG_CLASS_NAME = "InstancePlaceBrushTool";

        private readonly BrushStrokeState _strokeState = new();
        
        // Track placed instances this stroke for undo
        private List<PlacedInstanceRecord> _instancesPlacedThisStroke = new();
        
        // For scatter mode - track last placement position to avoid clustering
        private Vector3 _lastPlacementPos;
        private float _minPlacementDistance;

        public string ToolName => "Place Instance";
        public bool IsStrokeActive => _strokeState.IsActive;

        public InstancePlaceBrushTool()
        {
            DebugManager.Instance?.RegisterClass(DEBUG_CLASS_NAME);
        }

        public void BeginStroke(ManualEditLayer layer, Vector3 worldPos, BrushSettings settings)
        {
            _strokeState.Begin(layer, BrushUndoType.InstancePlacement);
            _strokeState.UpdatePosition(worldPos);
            _instancesPlacedThisStroke.Clear();
            _lastPlacementPos = worldPos;
            _minPlacementDistance = settings.InstanceMinDistance;

            // Place instance(s) at initial click position
            PlaceInstances(layer, worldPos, settings);
        }

        public void ContinueStroke(ManualEditLayer layer, Vector3 worldPos, BrushSettings settings)
        {
            if (!_strokeState.IsActive) return;

            // Only continue placing if scatter mode is enabled
            if (!settings.InstanceScatterMode) return;

            // Check minimum distance from last placement
            float distFromLast = worldPos.DistanceTo(_lastPlacementPos);
            if (distFromLast < _minPlacementDistance) return;

            PlaceInstances(layer, worldPos, settings);
            _lastPlacementPos = worldPos;
            _strokeState.UpdatePosition(worldPos);
        }

        public BrushUndoData EndStroke(ManualEditLayer layer)
        {
            var undoData = _strokeState.End("Place instance(s)");
            
            // Attach placed instance records to undo data for proper undo
            if (undoData != null && _instancesPlacedThisStroke.Count > 0)
            {
                undoData.PlacedInstances = new List<PlacedInstanceRecord>(_instancesPlacedThisStroke);
            }
            
            _instancesPlacedThisStroke.Clear();
            return undoData;
        }

        public void CancelStroke()
        {
            // Remove all instances placed this stroke
            if (_strokeState.IsActive && _strokeState.Layer != null)
            {
                foreach (var record in _instancesPlacedThisStroke)
                {
                    var buffer = _strokeState.Layer.GetEditBuffer(record.RegionCoords);
                    buffer?.RemoveInstance(record.MeshId, record.IndexInBuffer);
                }
            }
            
            _instancesPlacedThisStroke.Clear();
            _strokeState.Cancel();
        }

        private void PlaceInstances(ManualEditLayer layer, Vector3 worldPos, BrushSettings settings)
        {
            int regionSize = layer.RegionSize;
            if (regionSize <= 0)
            {
                GD.PrintErr("[InstancePlaceBrushTool] Region size is 0!");
                return;
            }

            int meshId = settings.InstanceMeshId;
            
            if (settings.InstanceScatterMode && settings.InstanceScatterCount > 1)
            {
                // Scatter multiple instances within brush radius
                PlaceScatteredInstances(layer, worldPos, settings, meshId, regionSize);
            }
            else
            {
                // Place single instance at position
                PlaceSingleInstance(layer, worldPos, settings, meshId, regionSize);
            }
        }

        private void PlaceSingleInstance(
            ManualEditLayer layer, 
            Vector3 worldPos, 
            BrushSettings settings,
            int meshId,
            int regionSize)
        {
            // Calculate transform
            Transform3D transform = CalculateInstanceTransform(worldPos, settings);
            
            // Determine which region this instance belongs to
            Vector2I regionCoords = new Vector2I(
                Mathf.FloorToInt(worldPos.X / regionSize),
                Mathf.FloorToInt(worldPos.Z / regionSize)
            );

            // Get or create buffer for this region
            var buffer = layer.GetOrCreateEditBuffer(regionCoords);
            if (buffer == null)
            {
                GD.PrintErr($"[InstancePlaceBrushTool] Failed to get/create buffer for region {regionCoords}");
                return;
            }

            // Mark region as affected for undo
            _strokeState.MarkRegionAffected(regionCoords, buffer);

            // Add instance to buffer
            buffer.AddInstance(meshId, transform);
            
            // Track for undo
            int indexInBuffer = buffer.GetInstances(meshId).Count - 1;
            _instancesPlacedThisStroke.Add(new PlacedInstanceRecord
            {
                RegionCoords = regionCoords,
                MeshId = meshId,
                IndexInBuffer = indexInBuffer,
                Transform = transform
            });

            layer.MarkRegionEdited(regionCoords);

            DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.UpdateCycle,
                $"Placed instance mesh {meshId} at {worldPos} in region {regionCoords}");
        }

        private void PlaceScatteredInstances(
            ManualEditLayer layer,
            Vector3 centerPos,
            BrushSettings settings,
            int meshId,
            int regionSize)
        {
            float brushRadius = settings.Size * 0.5f;
            int count = settings.InstanceScatterCount;
            var random = new RandomNumberGenerator();
            random.Randomize();

            // Track positions to avoid overlap
            var placedPositions = new List<Vector3>();
            float minSpacing = settings.InstanceMinDistance;

            int attempts = 0;
            int maxAttempts = count * 10; // Prevent infinite loops

            while (placedPositions.Count < count && attempts < maxAttempts)
            {
                attempts++;

                // Random position within brush radius
                float angle = random.RandfRange(0, Mathf.Tau);
                float distance = random.RandfRange(0, brushRadius);
                
                // Use square root for uniform distribution
                if (settings.Shape == BrushShape.Circle)
                {
                    distance = Mathf.Sqrt(random.RandfRange(0, 1)) * brushRadius;
                }

                Vector3 offset = new Vector3(
                    Mathf.Cos(angle) * distance,
                    0,
                    Mathf.Sin(angle) * distance
                );
                Vector3 instancePos = centerPos + offset;

                // Check minimum spacing
                bool tooClose = false;
                foreach (var existing in placedPositions)
                {
                    if (instancePos.DistanceTo(existing) < minSpacing)
                    {
                        tooClose = true;
                        break;
                    }
                }

                if (tooClose) continue;

                // Get terrain height at this position
                float? terrainHeight = GetTerrainHeight(instancePos);
                if (terrainHeight.HasValue)
                {
                    instancePos.Y = terrainHeight.Value;
                }

                // Check slope if slope limit is enabled
                if (settings.InstanceSlopeLimitEnabled)
                {
                    float slope = GetTerrainSlope(instancePos);
                    if (slope > settings.InstanceMaxSlope)
                    {
                        continue; // Skip this position
                    }
                }

                // Calculate transform for this instance
                Transform3D transform = CalculateInstanceTransform(instancePos, settings);

                // Determine region
                Vector2I regionCoords = new Vector2I(
                    Mathf.FloorToInt(instancePos.X / regionSize),
                    Mathf.FloorToInt(instancePos.Z / regionSize)
                );

                // Get or create buffer
                var buffer = layer.GetOrCreateEditBuffer(regionCoords);
                if (buffer == null) continue;

                // Mark region as affected
                _strokeState.MarkRegionAffected(regionCoords, buffer);

                // Add instance
                buffer.AddInstance(meshId, transform);

                // Track for undo
                int indexInBuffer = buffer.GetInstances(meshId).Count - 1;
                _instancesPlacedThisStroke.Add(new PlacedInstanceRecord
                {
                    RegionCoords = regionCoords,
                    MeshId = meshId,
                    IndexInBuffer = indexInBuffer,
                    Transform = transform
                });

                placedPositions.Add(instancePos);
                layer.MarkRegionEdited(regionCoords);
            }

            DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.UpdateCycle,
                $"Scattered {placedPositions.Count} instances of mesh {meshId} around {centerPos}");
        }

        private Transform3D CalculateInstanceTransform(Vector3 position, BrushSettings settings)
        {
            var random = new RandomNumberGenerator();
            random.Randomize();

            // Base scale with variation
            float baseScale = settings.InstanceScale;
            float scaleVariation = settings.RandomScaleVariation;
            float scale = baseScale * (1.0f + random.RandfRange(-scaleVariation, scaleVariation));

            // Rotation
            float rotationY = 0f;
            if (settings.RandomRotation)
            {
                rotationY = random.RandfRange(0, Mathf.Tau);
            }

            // Additional random tilt if enabled
            float tiltX = 0f;
            float tiltZ = 0f;
            if (settings.InstanceRandomTilt > 0)
            {
                float maxTilt = Mathf.DegToRad(settings.InstanceRandomTilt);
                tiltX = random.RandfRange(-maxTilt, maxTilt);
                tiltZ = random.RandfRange(-maxTilt, maxTilt);
            }

            // Align to terrain normal if enabled
            Vector3 up = Vector3.Up;
            if (settings.InstanceAlignToNormal)
            {
                Vector3? normal = GetTerrainNormal(position);
                if (normal.HasValue)
                {
                    up = normal.Value;
                }
            }

            // Build transform
            Basis basis = Basis.Identity;
            
            // Apply Y rotation first
            basis = basis.Rotated(Vector3.Up, rotationY);
            
            // Apply tilt
            if (tiltX != 0 || tiltZ != 0)
            {
                basis = basis.Rotated(Vector3.Right, tiltX);
                basis = basis.Rotated(Vector3.Forward, tiltZ);
            }
            
            // Align to terrain normal
            if (settings.InstanceAlignToNormal && up != Vector3.Up)
            {
                // Create rotation from Up to terrain normal
                Vector3 axis = Vector3.Up.Cross(up);
                if (axis.LengthSquared() > 0.0001f)
                {
                    axis = axis.Normalized();
                    float angle = Vector3.Up.AngleTo(up);
                    Basis alignBasis = new Basis(axis, angle);
                    basis = alignBasis * basis;
                }
            }
            
            // Apply scale
            basis = basis.Scaled(new Vector3(scale, scale, scale));

            // Apply vertical offset
            Vector3 finalPosition = position + Vector3.Up * settings.InstanceVerticalOffset;

            return new Transform3D(basis, finalPosition);
        }

        #region Terrain Queries

        private float? GetTerrainHeight(Vector3 position)
        {
            try
            {
                // Use the existing terrain height query system
                return TerrainHeightQuery.QueryHeight(new Vector2(position.X, position.Z));
            }
            catch
            {
                GD.PrintErr("[InstancePlaceBrushTool] Failed to query terrain height");
                return null;
            }
        }

        private Vector3? GetTerrainNormal(Vector3 position)
        {
            try
            {
                // Sample heights around position to calculate normal
                float sampleDistance = 1.0f;
                
                float? hCenter = GetTerrainHeight(position);
                float? hRight = GetTerrainHeight(position + Vector3.Right * sampleDistance);
                float? hForward = GetTerrainHeight(position + Vector3.Forward * sampleDistance);

                if (!hCenter.HasValue || !hRight.HasValue || !hForward.HasValue)
                    return null;

                Vector3 p0 = new Vector3(position.X, hCenter.Value, position.Z);
                Vector3 p1 = new Vector3(position.X + sampleDistance, hRight.Value, position.Z);
                Vector3 p2 = new Vector3(position.X, hForward.Value, position.Z + sampleDistance);

                Vector3 v1 = p1 - p0;
                Vector3 v2 = p2 - p0;
                
                return v2.Cross(v1).Normalized();
            }
            catch
            {
                return null;
            }
        }

        private float GetTerrainSlope(Vector3 position)
        {
            Vector3? normal = GetTerrainNormal(position);
            if (!normal.HasValue) return 0f;

            // Slope in degrees from vertical
            float dot = normal.Value.Dot(Vector3.Up);
            return Mathf.RadToDeg(Mathf.Acos(Mathf.Clamp(dot, -1f, 1f)));
        }

        #endregion
    }

    /// <summary>
    /// Record of a placed instance for undo tracking.
    /// </summary>
    public struct PlacedInstanceRecord
    {
        public Vector2I RegionCoords;
        public int MeshId;
        public int IndexInBuffer;
        public Transform3D Transform;
    }
}