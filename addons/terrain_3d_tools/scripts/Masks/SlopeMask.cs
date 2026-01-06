// /Masks/SlopeMask.cs
using Godot;
using System;
using System.Collections.Generic;
using Terrain3DTools.Core;
using Terrain3DTools.Utils;

namespace Terrain3DTools.Masks
{
    [GlobalClass, Tool]
    public partial class SlopeMask : TerrainMask
    {
        #region Private Fields
        private float _minSlope = 20f;
        private float _maxSlope = 40f;
        private float _minSlopeFalloff = 5f;
        private float _maxSlopeFalloff = 5f;
        #endregion

        #region Exported Properties
        [Export(PropertyHint.Range, "0,90,0.1")] 
        public float MinSlope { get => _minSlope; set => SetProperty(ref _minSlope, value); }
        
        [Export(PropertyHint.Range, "0,90,0.1")] 
        public float MaxSlope { get => _maxSlope; set => SetProperty(ref _maxSlope, value); }
        
        [Export(PropertyHint.Range, "0,45,0.1")]
        public float MinSlopeFalloff { get => _minSlopeFalloff; set => SetProperty(ref _minSlopeFalloff, value); }

        [Export(PropertyHint.Range, "0,45,0.1")]
        public float MaxSlopeFalloff { get => _maxSlopeFalloff; set => SetProperty(ref _maxSlopeFalloff, value); }

        [Export]
        public bool UseBaseTerrainHeight { get; set; } = true;
        
        private const float WorldHeightScale = 128f;
        private const float WorldVertexSpacing = 1.0f;
        #endregion

        public SlopeMask()
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
                    GD.PrintErr("[SlopeMask] UseBaseTerrainHeight is true, but no valid stitched heightmap was provided.");
                    return (null, new List<Rid>(), new List<string>());
                }
                return CreateSlopeShaderCommands(targetMaskTexture, maskWidth, maskHeight, stitchedHeightmap, WorldHeightScale, WorldVertexSpacing);
            }
            else
            {
                // PATH 2: Self-referential case - use compute shader copy
                
                // 1. Create temporary texture
                var temporaryTexture = Gpu.CreateTexture2D(
                    (uint)maskWidth, (uint)maskHeight,
                    RenderingDevice.DataFormat.R32Sfloat,
                    RenderingDevice.TextureUsageBits.SamplingBit |
                    RenderingDevice.TextureUsageBits.StorageBit);

                if (!temporaryTexture.IsValid)
                {
                    GD.PrintErr("[SlopeMask] Failed to create temporary texture for self-reference copy");
                    return (null, new List<Rid>(), new List<string>());
                }

                // 2. Create copy commands (compute shader based)
                var (copyAction, copyTempRids, copyShaderPath) = GpuKernels.CreateCopyTextureCommands(
                    targetMaskTexture, temporaryTexture, maskWidth, maskHeight, "SlopeMask");

                // 3. Create slope shader commands (scale/spacing are 1.0 when operating on self)
                var (shaderAction, shaderTempRids, shaderPaths) = CreateSlopeShaderCommands(
                    targetMaskTexture, maskWidth, maskHeight, temporaryTexture, 1.0f, 1.0f);

                // 4. Combine commands
                Action<long> combinedCommands = (computeList) =>
                {
                    copyAction?.Invoke(computeList);
                    Gpu.Rd.ComputeListAddBarrier(computeList);
                    shaderAction?.Invoke(computeList);
                };

                // 5. Collect all temp RIDs
                var allTempRids = new List<Rid>();
                allTempRids.AddRange(copyTempRids);
                allTempRids.AddRange(shaderTempRids);
                allTempRids.Add(temporaryTexture);

                // 6. Collect all shader paths
                var allShaderPaths = new List<string>();
                if (!string.IsNullOrEmpty(copyShaderPath)) allShaderPaths.Add(copyShaderPath);
                allShaderPaths.AddRange(shaderPaths);

                return (combinedCommands, allTempRids, allShaderPaths);
            }
        }

        private (Action<long> commands, List<Rid> tempRids, List<string>) CreateSlopeShaderCommands(
            Rid targetMaskTexture, int maskWidth, int maskHeight, Rid heightSourceTexture, 
            float heightScale, float vertexSpacing)
        {
            var shaderPath = "res://addons/terrain_3d_tools/Shaders/Masks/slope_mask.glsl";
            var operation = new AsyncComputeOperation(shaderPath);
            
            operation.BindStorageImage(0, targetMaskTexture);
            operation.BindSamplerWithTexture(1, heightSourceTexture);
            operation.SetPushConstants(BuildPushConstants(heightScale, vertexSpacing));

            uint groupsX = (uint)((maskWidth + 7) / 8);
            uint groupsY = (uint)((maskHeight + 7) / 8);

            return (operation.CreateDispatchCommands(groupsX, groupsY), operation.GetTemporaryRids(), new List<string> { shaderPath });
        }
        
        private byte[] BuildPushConstants(float heightScale, float vertexSpacing)
        {
            return GpuUtils.CreatePushConstants()
                .Add((int)BlendType)
                .Add(LayerMix)
                .Add(Invert ? 1 : 0)
                .Add(MinSlope)
                .Add(MaxSlope)
                .Add(MinSlopeFalloff)
                .Add(MaxSlopeFalloff)
                .Add(heightScale)
                .Add(vertexSpacing)
                .AddPadding(12)
                .Build();
        }
    }
}