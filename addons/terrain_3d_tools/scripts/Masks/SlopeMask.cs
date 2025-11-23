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
        
        public override (Action<long> commands, List<Rid> tempRids, List<string>) CreateApplyCommands(Rid targetMaskTexture, int maskWidth, int maskHeight, Rid stitchedHeightmap = new Rid())
        {
            if (UseBaseTerrainHeight)
            {
                // PATH 1: Simple case, just returns the shader command.
                if (!stitchedHeightmap.IsValid)
                {
                    GD.Print("[SlopeMask] UseBaseTerrainHeight is true, but no valid stitched heightmap was provided.");
                    return (null, new List<Rid>(), new List<string> { "" });
                }
                return CreateSlopeShaderCommands(targetMaskTexture, maskWidth, maskHeight, stitchedHeightmap, WorldHeightScale, WorldVertexSpacing);
            }
            else
            {
                // PATH 2: Complex self-referential case. We build a multi-step Action<long>.

                // 1. Create the temporary texture resource.
                var temporaryTexture = Gpu.CreateTexture2D((uint)maskWidth, (uint)maskHeight, RenderingDevice.DataFormat.R32Sfloat, RenderingDevice.TextureUsageBits.StorageBit | RenderingDevice.TextureUsageBits.SamplingBit | RenderingDevice.TextureUsageBits.CanCopyToBit);
                
                // 2. Get the shader command action and its RIDs. When operating on itself, scale/spacing are 1.0.
                var (shaderAction, shaderTempRids, shaderPath) = CreateSlopeShaderCommands(targetMaskTexture, maskWidth, maskHeight, temporaryTexture, 1.0f, 1.0f);

                // 3. Define the combined sequence.
                Action<long> combinedCommands = (computeList) => {
                    // Step A: Add the copy command to the provided compute list.
                    Gpu.AddCopyTextureCommand(computeList, targetMaskTexture, temporaryTexture, (uint)maskWidth, (uint)maskHeight);

                    // Step B: Add a barrier to ensure the copy finishes.
                    Gpu.Rd.ComputeListAddBarrier(computeList);

                    // Step C: Invoke the shader action, passing the compute list down.
                    shaderAction?.Invoke(computeList);
                };

                // 4. The task must own ALL temporary RIDs.
                var allTempRidsForTask = new List<Rid> { temporaryTexture };
                allTempRidsForTask.AddRange(shaderTempRids);

                return (combinedCommands, allTempRidsForTask,  shaderPath );
            }
        }

        private (Action<long> commands, List<Rid> tempRids, List<string> ) CreateSlopeShaderCommands(Rid targetMaskTexture, int maskWidth, int maskHeight, Rid heightSourceTexture, float heightScale, float vertexSpacing)
        {
            var shaderPath = "res://addons/terrain_3d_tools/Shaders/Masks/slope_mask.glsl";
            var operation = new AsyncComputeOperation(shaderPath);
            
            operation.BindStorageImage(0, targetMaskTexture);
            operation.BindSamplerWithTexture(1, heightSourceTexture);
            operation.SetPushConstants(BuildPushConstants(heightScale, vertexSpacing));

            uint groupsX = (uint)((maskWidth + 7) / 8);
            uint groupsY = (uint)((maskHeight + 7) / 8);

            // This now correctly returns an Action<long>
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
