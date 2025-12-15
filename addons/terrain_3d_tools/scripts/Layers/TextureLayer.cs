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
        #region Private Fields
        private int _textureIndex = 0;
        private Godot.Collections.Array<int> _excludedTextureIds = new();
        #endregion

        #region Properties
        [ExportGroup("Texture Properties")]
        
        [Export(PropertyHint.Range, "0,31")]
        public int TextureIndex 
        { 
            get => _textureIndex; 
            set => SetProperty(ref _textureIndex, Mathf.Clamp(value, 0, 31)); 
        }

        [Export]
        public Godot.Collections.Array<int> ExcludedTextureIds
        {
            get => _excludedTextureIds;
            set
            {
                _excludedTextureIds = value ?? new Godot.Collections.Array<int>();
                ForceDirty();
            }
        }
        #endregion

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
        public override (Action<long> commands, List<Rid> tempRids, List<string>) CreateApplyRegionCommands(
            Vector2I regionCoords,
            RegionData regionData,
            int regionSize,
            Vector2 regionMinWorld,
            Vector2 regionSizeWorld)
        {
            if (!layerTextureRID.IsValid || !regionData.ControlMap.IsValid)
            {
                GD.PrintErr($"[TextureLayer] CreateApplyRegionCommands called on '{LayerName}' but a required texture is invalid.");
                return (null, new List<Rid>(), new List<string>());
            }

            Vector2 maskCenter = new Vector2(GlobalPosition.X, GlobalPosition.Z);
            OverlapResult? overlap = RegionMaskOverlap.GetRegionMaskOverlap(regionCoords, regionSize, maskCenter, Size);
            if (!overlap.HasValue)
            {
                return (null, new List<Rid>(), new List<string>());
            }
            var o = overlap.Value;

            var shaderPath = "res://addons/terrain_3d_tools/Shaders/Layers/TextureLayer.glsl";
            var operation = new AsyncComputeOperation(shaderPath);
            var tempRids = new List<Rid>();

            // Binding 0: Control map (read/write)
            operation.BindStorageImage(0, regionData.ControlMap);
            
            // Binding 1: Layer mask sampler
            operation.BindSamplerWithTexture(1, layerTextureRID);

            // Binding 2: Exclusion list buffer
            Rid exclusionBufferRid = CreateExclusionBuffer();
            if (exclusionBufferRid.IsValid)
            {
                operation.BindStorageBuffer(2, exclusionBufferRid);
                tempRids.Add(exclusionBufferRid);
            }

            // Push constants
            var pcb = GpuUtils.CreatePushConstants()
                .Add(o.RegionMin.X).Add(o.RegionMin.Y)      // region_min_px
                .Add(o.MaskMin.X).Add(o.MaskMin.Y)          // mask_min_px
                .Add(Size.X).Add(Size.Y)                     // layer_size_px
                .Add((uint)TextureIndex)                     // texture_id
                .Add((uint)_excludedTextureIds.Count)        // exclusion_count
                .Build();

            operation.SetPushConstants(pcb);

            uint groupsX = (uint)((regionSize + 7) / 8);
            uint groupsY = (uint)((regionSize + 7) / 8);

            tempRids.AddRange(operation.GetTemporaryRids());

            return (operation.CreateDispatchCommands(groupsX, groupsY), tempRids, new List<string> { shaderPath });
        }

        /// <summary>
        /// Creates a GPU buffer containing the excluded texture IDs.
        /// </summary>
        private Rid CreateExclusionBuffer()
        {
            // Always create buffer with at least 1 element (shader expects valid buffer)
            int count = Math.Max(1, _excludedTextureIds.Count);
            var data = new uint[count];
            
            for (int i = 0; i < _excludedTextureIds.Count; i++)
            {
                data[i] = (uint)Mathf.Clamp(_excludedTextureIds[i], 0, 31);
            }

            // Convert to bytes
            byte[] bytes = new byte[count * sizeof(uint)];
            Buffer.BlockCopy(data, 0, bytes, 0, bytes.Length);

            return Gpu.Rd.StorageBufferCreate((uint)bytes.Length, bytes);
        }
    }
}