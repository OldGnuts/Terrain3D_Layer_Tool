// /Masks/HeightRangeMask.cs (Corrected for Action<long>)
using Godot;
using System;
using System.Collections.Generic;
using Terrain3DTools.Core;
using Terrain3DTools.Utils;

namespace Terrain3DTools.Masks
{
    [GlobalClass, Tool]
    public partial class HeightRangeMask : TerrainMask
    {
        #region Private Fields
        private float _minHeight = 0.0f;
        private float _maxHeight = 1.0f;
        private float _falloffRange = 0.1f;
        #endregion

        #region Exported Properties
        [Export(PropertyHint.Range, "-1,1,0.01")]
        public float MinHeight { get => _minHeight; set => SetProperty(ref _minHeight, value); }

        [Export(PropertyHint.Range, "-1,1,0.01")]
        public float MaxHeight { get => _maxHeight; set => SetProperty(ref _maxHeight, value); }

        [Export(PropertyHint.Range, "0,1,0.01")]
        public float FalloffRange { get => _falloffRange; set => SetProperty(ref _falloffRange, value); }

        [Export]
        public bool UseBaseTerrainHeight { get; set; } = true;
        #endregion

        public HeightRangeMask()
        {
            BlendType = MaskBlendType.Add;
        }

        public override MaskRequirements MaskDataRequirements() => UseBaseTerrainHeight ? MaskRequirements.RequiresHeightData : MaskRequirements.None;
        public override bool RequiresBaseHeightData() => UseBaseTerrainHeight;

        public override (Action<long> commands, List<Rid> tempRids, List<string>) CreateApplyCommands(
            Rid targetMaskTexture, int maskWidth, int maskHeight, Rid stitchedHeightmap = new Rid())
        {
            if (UseBaseTerrainHeight)
            {
                if (!stitchedHeightmap.IsValid)
                {
                    GD.PrintErr("[HeightRangeMask] UseBaseTerrainHeight is true, but no valid stitched heightmap was provided.");
                    return (null, new List<Rid>(), new List<string>());
                }
                return CreateHeightRangeShaderCommands(targetMaskTexture, maskWidth, maskHeight, stitchedHeightmap);
            }
            else
            {
                // PATH 2: Self-referential case
                var temporaryTexture = Gpu.CreateTexture2D(
                    (uint)maskWidth, (uint)maskHeight,
                    RenderingDevice.DataFormat.R32Sfloat,
                    RenderingDevice.TextureUsageBits.SamplingBit |
                    RenderingDevice.TextureUsageBits.StorageBit);

                if (!temporaryTexture.IsValid)
                {
                    GD.PrintErr("[HeightRangeMask] Failed to create temporary texture for self-reference copy");
                    return (null, new List<Rid>(), new List<string>());
                }

                var (copyAction, copyTempRids, copyShaderPath) = GpuKernels.CreateCopyTextureCommands(
                    targetMaskTexture, temporaryTexture, maskWidth, maskHeight, "HeightRangeMask");

                var (shaderAction, shaderTempRids, shaderPaths) = CreateHeightRangeShaderCommands(
                    targetMaskTexture, maskWidth, maskHeight, temporaryTexture);

                Action<long> combinedCommands = (computeList) =>
                {
                    copyAction?.Invoke(computeList);
                    Gpu.Rd.ComputeListAddBarrier(computeList);
                    shaderAction?.Invoke(computeList);
                };

                var allTempRids = new List<Rid>();
                allTempRids.AddRange(copyTempRids);
                allTempRids.AddRange(shaderTempRids);
                allTempRids.Add(temporaryTexture);

                var allShaderPaths = new List<string>();
                if (!string.IsNullOrEmpty(copyShaderPath)) allShaderPaths.Add(copyShaderPath);
                allShaderPaths.AddRange(shaderPaths);

                return (combinedCommands, allTempRids, allShaderPaths);
            }
        }

        private (Action<long> commands, List<Rid> tempRids, List<string>) CreateHeightRangeShaderCommands(Rid targetMaskTexture, int maskWidth, int maskHeight, Rid heightSourceTexture)
        {
            var shaderPath = "res://addons/terrain_3d_tools/Shaders/Masks/height_range_mask.glsl";
            var operation = new AsyncComputeOperation(shaderPath);

            operation.BindStorageImage(0, targetMaskTexture);
            operation.BindSamplerWithTexture(1, heightSourceTexture);

            var pcb = GpuUtils.CreatePushConstants()
                .Add((int)BlendType)
                .Add(LayerMix)
                .Add(Invert ? 1 : 0)
                .Add(MinHeight)
                .Add(MaxHeight)
                .Add(Mathf.Max(0.001f, FalloffRange))
                .AddPadding(8)
                .Build();
            operation.SetPushConstants(pcb);

            uint groupsX = (uint)((maskWidth + 7) / 8);
            uint groupsY = (uint)((maskHeight + 7) / 8);

            return (operation.CreateDispatchCommands(groupsX, groupsY), operation.GetTemporaryRids(), new List<string> { shaderPath });
        }
    }
}