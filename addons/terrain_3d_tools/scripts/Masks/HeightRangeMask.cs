// /Masks/HeightRangeMask.cs 
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
        
        public override (Action<long> commands, List<Rid> tempRids, List<string>) CreateApplyCommands(Rid targetMaskTexture, int maskWidth, int maskHeight, Rid stitchedHeightmap = new Rid())
        {
            if (UseBaseTerrainHeight)
            {
                // PATH 1: Simple case, just returns the shader command.
                if (!stitchedHeightmap.IsValid)
                {
                    GD.PrintErr("[HeightRangeMask] UseBaseTerrainHeight is true, but no valid stitched heightmap was provided.");
                    return (null, new List<Rid>(), new List<string> { "" });
                }
                return CreateHeightRangeShaderCommands(targetMaskTexture, maskWidth, maskHeight, stitchedHeightmap);
            }
            else
            {
                // PATH 2: Complex self-referential case. We build a multi-step Action<long>.
                
                // 1. Create the temporary texture resource.
                var temporaryTexture = Gpu.CreateTexture2D((uint)maskWidth, (uint)maskHeight, RenderingDevice.DataFormat.R32Sfloat, RenderingDevice.TextureUsageBits.StorageBit | RenderingDevice.TextureUsageBits.SamplingBit | RenderingDevice.TextureUsageBits.CanCopyToBit);
                
                // 2. Get the shader command action and its temporary RIDs *before* creating the combined action.
                var (shaderAction, shaderTempRids, shaderPath) = CreateHeightRangeShaderCommands(targetMaskTexture, maskWidth, maskHeight, temporaryTexture);

                // 3. Define the combined sequence. This lambda accepts the compute list from the task manager.
                Action<long> combinedCommands = (computeList) => {
                    // Step A: Add the copy command to the provided compute list.
                    Gpu.AddCopyTextureCommand(computeList, targetMaskTexture, temporaryTexture, (uint)maskWidth, (uint)maskHeight);

                    // Step B: Add a barrier to ensure the copy finishes before the shader runs.
                    Gpu.Rd.ComputeListAddBarrier(computeList);

                    // Step C: Invoke the shader action, passing the compute list down to it.
                    shaderAction?.Invoke(computeList);
                };

                // 4. The task must own ALL temporary RIDs from this entire operation.
                var allTempRidsForTask = new List<Rid> { temporaryTexture };
                allTempRidsForTask.AddRange(shaderTempRids);

                return (combinedCommands, allTempRidsForTask, shaderPath);
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
