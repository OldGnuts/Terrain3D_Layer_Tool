// /Layers/HeightLayer.cs
using Godot;
using System;
using System.Collections.Generic;
using Terrain3DTools.Core;
using Terrain3DTools.Utils;

namespace Terrain3DTools.Layers
{
    [GlobalClass, Tool]
    public partial class HeightLayer : TerrainLayerBase
    {
        public enum HeightOperation { Add, Subtract, Multiply, Overwrite }
        private HeightOperation _operation = HeightOperation.Add;
        private float _strength = 1.0f;

        [Export]
        public HeightOperation Operation
        {
            get => _operation;
            set => SetProperty(ref _operation, value);
        }

        [Export(PropertyHint.Range, "0.0,10.0,0.1")]
        public float Strength
        {
            get => _strength;
            set => SetProperty(ref _strength, value);
        }

        public override void _Ready()
        {
            base._Ready();
            if (string.IsNullOrEmpty(LayerName) || LayerName.StartsWith("New Layer"))
                LayerName = "Height Layer " + IdGenerator.GenerateShortUid();
        }

        public override LayerType GetLayerType() => LayerType.Height;
        public override string LayerTypeName() => "Height Layer";

        /// <summary>
        /// Creates the GPU commands to apply this height layer's effect to a region's heightmap.
        /// This method does not execute the commands; it returns a description of the work to be done.
        /// </summary>
        public override (Action<long> commands, List<Rid> tempRids, List<string>) CreateApplyRegionCommands(
            Vector2I regionCoords,
            RegionData regionData,
            int regionSize,
            Vector2 regionMinWorld,
            Vector2 regionSizeWorld)
        {
            if (!regionData.HeightMap.IsValid)
            {
                GD.PrintErr($"[HeightLayer] Region heightmap is invalid for '{LayerName}'");
                return (null, new List<Rid>(), new List<string> { "" });
            }

            Vector2 maskCenter = new Vector2(GlobalPosition.X, GlobalPosition.Z);
            OverlapResult? overlap = RegionMaskOverlap.GetRegionMaskOverlap(regionCoords, regionSize, maskCenter, Size);

            if (!overlap.HasValue)
            {
                return (null, new List<Rid>(), new List<string> { "" });
            }
            var o = overlap.Value;
            var shaderPath = "res://addons/terrain_3d_tools/Shaders/Layers/HeightLayer.glsl";
            var operation = new AsyncComputeOperation(shaderPath);

            operation.BindStorageImage(0, regionData.HeightMap);
            operation.BindStorageImage(1, layerTextureRID);

            var pcb = GpuUtils.CreatePushConstants()
                .Add(o.RegionMin.X).Add(o.RegionMin.Y)
                .Add(o.RegionMax.X).Add(o.RegionMax.Y)
                .Add(o.MaskMin.X).Add(o.MaskMin.Y)
                .Add(o.MaskMax.X).Add(o.MaskMax.Y)
                .Add((int)Operation)
                .Add(Strength)
                .AddPadding(8);

            operation.SetPushConstants(pcb.Build());

            uint groupsX = (uint)((regionSize + 7) / 8);
            uint groupsY = (uint)((regionSize + 7) / 8);

            return (operation.CreateDispatchCommands(groupsX, groupsY), operation.GetTemporaryRids(), new List<string> { shaderPath });
        }
    }
}