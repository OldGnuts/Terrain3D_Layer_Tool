// /Layers/TextureLayer.cs
using Godot;
using System;
using System.Collections.Generic;
using Terrain3DTools.Core;
using Terrain3DTools.Utils;

namespace Terrain3DTools.Layers
{
    [GlobalClass, Tool]
    public partial class TextureLayer : TerrainLayerBase
    {
        [ExportGroup("Painting Properties")]
        [Export(PropertyHint.Range, "0,31,1")] public int BaseTextureID { get; set; } = 0;

        public override LayerType GetLayerType() => LayerType.Texture;
        public override string LayerTypeName() => "Texture Layer";

        public override void _Ready()
        {
            base._Ready();
            if (string.IsNullOrEmpty(LayerName) || LayerName.StartsWith("New Layer"))
                LayerName = "Texture Layer " + IdGenerator.GenerateShortUid();
        }

        public override void MarkPositionDirty()
        {
            if (DoesAnyMaskRequireHeightData())
            {
                ForceDirty();
            }
            base.MarkPositionDirty();
        }

        /// <summary>
        /// Creates the GPU commands to apply this texture layer's effect to a region's control map.
        /// </summary>
        // --- START OF CORRECTION ---
        public override (Action<long> commands, List<Rid> tempRids, List<string>) CreateApplyRegionCommands(
            Vector2I regionCoords,
            RegionData regionData,
            int regionSize,
            Vector2 regionMinWorld,
            Vector2 regionSizeWorld)
        // --- END OF CORRECTION ---
        {
            if (!layerTextureRID.IsValid || !regionData.ControlMap.IsValid)
            {
                GD.PrintErr($"[TextureLayer] CreateApplyRegionCommands called on '{LayerName}' but a required texture is invalid.");
                return (null, new List<Rid>(), new List<string> { "" });
            }

            Vector2 maskCenter = new Vector2(GlobalPosition.X, GlobalPosition.Z);
            OverlapResult? overlap = RegionMaskOverlap.GetRegionMaskOverlap(regionCoords, regionSize, maskCenter, Size);
            if (!overlap.HasValue)
            {
                return (null, new List<Rid>(), new List<string> { "" });
            }
            var o = overlap.Value;

            // --- CORRECTION: Removed 'using' statement ---
            var shaderPath = "res://addons/terrain_3d_tools/Shaders/Layers/TextureLayer.glsl";
            var operation = new AsyncComputeOperation(shaderPath);

            operation.BindStorageImage(0, regionData.ControlMap);
            operation.BindSamplerWithTexture(1, layerTextureRID);

            var pcb = GpuUtils.CreatePushConstants()
                .Add(o.RegionMin.X).Add(o.RegionMin.Y)
                .Add(o.MaskMin.X).Add(o.MaskMin.Y)
                .Add(Size.X).Add(Size.Y)
                .Add((uint)BaseTextureID)
                .AddPadding(4)
                .Build();

            operation.SetPushConstants(pcb);

            uint groupsX = (uint)((regionSize + 7) / 8);
            uint groupsY = (uint)((regionSize + 7) / 8);

            return (operation.CreateDispatchCommands(groupsX, groupsY), operation.GetTemporaryRids(), new List<string> { shaderPath });
        }
    }
}