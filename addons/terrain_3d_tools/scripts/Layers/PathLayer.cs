// /Layers/PathLayer.cs
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using Terrain3DTools.Core;
using Terrain3DTools.Utils;

namespace Terrain3DTools.Layers
{
    [GlobalClass, Tool]
    public partial class PathLayer : FeatureLayer
    {
        public override void _Ready()
        {
            base._Ready();
            if (string.IsNullOrEmpty(LayerName) || LayerName.StartsWith("New Layer"))
                LayerName = $"{PathType} Layer " + IdGenerator.GenerateShortUid();

            // Initialize default curves
            if (_carveCurve == null) _carveCurve = CurveUtils.CreateLinearCurve();
            if (_embankmentCurve == null) _embankmentCurve = CurveUtils.CreateBellCurve();

            ModifiesHeight = _carveHeight || _createEmbankments;
            ModifiesTexture = _applyTextures;
        }

        public override void _EnterTree()
        {
            base._EnterTree();
            if (IsInstanceValid(_carveCurve)) _carveCurve.Changed += OnCurveChanged;
            if (IsInstanceValid(_embankmentCurve)) _embankmentCurve.Changed += OnCurveChanged;
        }

        public override void _ExitTree()
        {
            base._ExitTree();
            if (IsInstanceValid(_carveCurve)) _carveCurve.Changed -= OnCurveChanged;
            if (IsInstanceValid(_embankmentCurve)) _embankmentCurve.Changed -= OnCurveChanged;
        }

        public override void _Notification(int what)
        {
            base._Notification(what);

            if (what == (int)NotificationPredelete)
            {
                Gpu.FreeRid(layerHeightDataRID);
                layerHeightDataRID = new Rid();
            }
        }

        public override void PrepareMaskResources(bool isInteractive)
        {
            base.PrepareMaskResources(isInteractive);

            if (isInteractive && SizeHasChanged)
            {
                return;
            }

            int maskWidth = Size.X;
            int maskHeight = Size.Y;

            // Create/refresh height data texture
            if (!layerHeightDataRID.IsValid || SizeHasChanged)
            {
                if (layerHeightDataRID.IsValid)
                    Gpu.FreeRid(layerHeightDataRID);

                layerHeightDataRID = Gpu.CreateTexture2D(
                    (uint)maskWidth, (uint)maskHeight,
                    RenderingDevice.DataFormat.R32Sfloat,
                    RenderingDevice.TextureUsageBits.StorageBit |
                    RenderingDevice.TextureUsageBits.SamplingBit |
                    RenderingDevice.TextureUsageBits.CanCopyFromBit
                );
            }
        }

        public override string LayerTypeName() => $"{PathType} Path Layer";

        private void OnCurveChanged()
        {
            ForceDirty();
        }

        private void ApplyPathTypeDefaults()
        {
            switch (_pathType)
            {
                case PathType.Road:
                    _pathWidth = 6.0f;
                    _carveHeight = true;
                    _carveStrength = 0.2f;
                    _terrainConformance = 0.1f; // Slight blend with terrain
                    _createEmbankments = true;
                    _embankmentHeight = 0.5f;
                    _applyTextures = true;
                    _textureMode = PathTextureMode.CenterEmbankment;
                    break;

                case PathType.Path:
                    _pathWidth = 2.0f;
                    _carveHeight = true;
                    _carveStrength = 0.1f;
                    _terrainConformance = 0.3f; // More blend with terrain
                    _createEmbankments = false;
                    _applyTextures = true;
                    _textureMode = PathTextureMode.SingleTexture;
                    break;

                case PathType.River:
                    _pathWidth = 8.0f;
                    _carveHeight = true;
                    _carveStrength = .5f;
                    _riverDepth = 3.0f;
                    _terrainConformance = 0.0f; // No blend - hard carve
                    _createEmbankments = true;
                    _embankmentHeight = 1.0f;
                    _applyTextures = true;
                    _textureMode = PathTextureMode.CenterEmbankment;
                    break;

                case PathType.Trench:
                    _pathWidth = 4.0f;
                    _carveHeight = true;
                    _carveStrength = .5f;
                    _terrainConformance = 0.0f; // No blend - hard carve
                    _createEmbankments = true;
                    _embankmentHeight = 2.0f;
                    break;

                case PathType.Ridge:
                    _pathWidth = 3.0f;
                    _carveHeight = true;
                    _carveStrength = -1.5f; // Negative for raising
                    _terrainConformance = 0.2f; // Slight blend
                    _createEmbankments = false;
                    break;
            }
        }
        public override (Action<long> commands, List<Rid> tempRids, List<string>) CreateApplyHeightCommands(
    Vector2I regionCoords,
    RegionData regionData,
    int regionSize,
    Vector2 regionMinWorld,
    Vector2 regionSizeWorld)
        {
            if (!ModifiesHeight || !layerTextureRID.IsValid || !regionData.HeightMap.IsValid)
            {
                return (null, new List<Rid>(), new List<string>());
            }

            Vector2 maskCenter = new Vector2(GlobalPosition.X, GlobalPosition.Z);
            OverlapResult? overlap = RegionMaskOverlap.GetRegionMaskOverlap(
                regionCoords, regionSize, maskCenter, Size);

            if (!overlap.HasValue)
            {
                return (null, new List<Rid>(), new List<string>());
            }

            var o = overlap.Value;
            var shaderPath = "res://addons/terrain_3d_tools/Shaders/Layers/PathHeightApplication.glsl";
            var operation = new AsyncComputeOperation(shaderPath);

            operation.BindStorageImage(0, regionData.HeightMap);
            operation.BindSamplerWithTexture(1, layerTextureRID);       // Influence mask
            operation.BindSamplerWithTexture(2, layerHeightDataRID);    // Height data

            // IMPORTANT: Normalize embankment height by world height scale
            float normalizedEmbankmentHeight = _embankmentHeight / _worldHeightScale;

            // DEBUG: Print what we're sending
            if (_debugMode)
            {
                GD.Print($"[PathLayer] Apply Height Commands for region {regionCoords}:");
                GD.Print($"  Carve Strength: {_carveStrength}");
                GD.Print($"  Path Elevation: {_pathElevation}");
                GD.Print($"  Terrain Conformance: {_terrainConformance}");
                GD.Print($"  Embankment Height (raw): {_embankmentHeight}");
                GD.Print($"  Embankment Height (normalized): {normalizedEmbankmentHeight}");
                GD.Print($"  World Height Scale: {_worldHeightScale}");
            }

            var pushConstants = GpuUtils.CreatePushConstants()
                .Add(o.RegionMin.X).Add(o.RegionMin.Y)
                .Add(o.RegionMax.X).Add(o.RegionMax.Y)
                .Add(o.MaskMin.X).Add(o.MaskMin.Y)
                .Add(o.MaskMax.X).Add(o.MaskMax.Y)
                .Add(Size.X).Add(Size.Y)
                .Add(_carveStrength)
                .Add(_pathElevation)
                .Add(_terrainConformance)
                .Add(normalizedEmbankmentHeight)  // CHANGED: Use normalized value
                .AddPadding(8)
                .Build();

            operation.SetPushConstants(pushConstants);

            uint groupsX = (uint)((regionSize + 7) / 8);
            uint groupsY = (uint)((regionSize + 7) / 8);

            return (operation.CreateDispatchCommands(groupsX, groupsY),
                    operation.GetTemporaryRids(),
                    new List<string> { shaderPath });
        }

        public override (Action<long> commands, List<Rid> tempRids, List<string>) CreateApplyTextureCommands(
            Vector2I regionCoords,
            RegionData regionData,
            int regionSize,
            Vector2 regionMinWorld,
            Vector2 regionSizeWorld)
        {
            if (!ModifiesTexture || !layerTextureRID.IsValid || !regionData.ControlMap.IsValid)
            {
                return (null, new List<Rid>(), new List<string>());
            }

            Vector2 maskCenter = new Vector2(GlobalPosition.X, GlobalPosition.Z);
            OverlapResult? overlap = RegionMaskOverlap.GetRegionMaskOverlap(
                regionCoords, regionSize, maskCenter, Size);

            if (!overlap.HasValue)
            {
                return (null, new List<Rid>(), new List<string>());
            }

            var o = overlap.Value;
            var shaderPath = "res://addons/terrain_3d_tools/Shaders/Layers/PathTextureApplication.glsl";
            var operation = new AsyncComputeOperation(shaderPath);

            operation.BindStorageImage(0, regionData.ControlMap);
            operation.BindSamplerWithTexture(1, layerTextureRID);

            var pushConstants = GpuUtils.CreatePushConstants()
                .Add(o.RegionMin.X).Add(o.RegionMin.Y)
                .Add(o.MaskMin.X).Add(o.MaskMin.Y)
                .Add(Size.X).Add(Size.Y)
                .Add((uint)_centerTextureId)
                .Add((uint)_embankmentTextureId)
                .Add(TextureInfluence)
                .AddPadding(12)
                .Build();

            operation.SetPushConstants(pushConstants);

            uint groupsX = (uint)((regionSize + 7) / 8);
            uint groupsY = (uint)((regionSize + 7) / 8);

            return (operation.CreateDispatchCommands(groupsX, groupsY),
                    operation.GetTemporaryRids(),
                    new List<string> { shaderPath });
        }

        public override (Action<long> commands, List<Rid> tempRids, List<string>) CreateApplyRegionCommands(
            Vector2I regionCoords,
            RegionData regionData,
            int regionSize,
            Vector2 regionMinWorld,
            Vector2 regionSizeWorld)
        {
            // Simplified: just combine height and texture commands
            var allCommands = new List<Action<long>>();
            var allTempRids = new List<Rid>();
            var allShaderPaths = new List<string>();

            if (ModifiesHeight)
            {
                var (heightCmd, heightRids, heightShaders) = CreateApplyHeightCommands(
                    regionCoords, regionData, regionSize, regionMinWorld, regionSizeWorld);
                if (heightCmd != null)
                {
                    allCommands.Add(heightCmd);
                    allTempRids.AddRange(heightRids);
                    allShaderPaths.AddRange(heightShaders);
                }
            }

            if (ModifiesTexture)
            {
                var (textureCmd, textureRids, textureShaders) = CreateApplyTextureCommands(
                    regionCoords, regionData, regionSize, regionMinWorld, regionSizeWorld);
                if (textureCmd != null)
                {
                    allCommands.Add(textureCmd);
                    allTempRids.AddRange(textureRids);
                    allShaderPaths.AddRange(textureShaders);
                }
            }

            if (allCommands.Count == 0)
            {
                return (null, new List<Rid>(), new List<string>());
            }

            Action<long> combinedCommands = (computeList) =>
            {
                foreach (var cmd in allCommands)
                {
                    cmd?.Invoke(computeList);
                }
            };

            return (combinedCommands, allTempRids, allShaderPaths);
        }
    }
}