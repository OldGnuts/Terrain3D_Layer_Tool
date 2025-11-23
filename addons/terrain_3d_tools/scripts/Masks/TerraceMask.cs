// /Masks/TerraceMask.cs 
using Godot;
using System;
using System.Collections.Generic;
using Terrain3DTools.Core;
using Terrain3DTools.Utils;

namespace Terrain3DTools.Masks
{
    public enum TerraceMaskMode { OutputTerracedHeight, MaskTreads, MaskRisers }

    [GlobalClass, Tool]
    public partial class TerraceMask : TerrainMask
    {
        #region Private Fields
        private TerraceMaskMode _mode = TerraceMaskMode.OutputTerracedHeight;
        private int _terraceCount = 10;
        private float _sharpness = 0.8f;
        #endregion

        #region Exported Properties
        [Export(PropertyHint.Enum, "OutputTerracedHeight:For HeightLayers, MaskTreads/Risers:For TextureLayers")]
        public TerraceMaskMode Mode { get => _mode; set => SetProperty(ref _mode, value); }

        [Export(PropertyHint.Range, "1,256,1")]
        public int TerraceCount { get => _terraceCount; set => SetProperty(ref _terraceCount, value); }

        [Export(PropertyHint.Range, "0.0, 1.0, 0.01")]
        public float Sharpness { get => _sharpness; set => SetProperty(ref _sharpness, value); }

        [Export]
        public bool UseBaseTerrainHeight { get; set; } = false;
        #endregion
        
        public TerraceMask()
        {
            BlendType = MaskBlendType.Mix;
            LayerMix = 0.5f;
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
                    GD.PrintErr("[TerraceMask] UseBaseTerrainHeight is true, but no valid stitched heightmap was provided.");
                    return (null, new List<Rid>(), new List<string> { "" });
                }
                return CreateTerraceShaderCommands(targetMaskTexture, maskWidth, maskHeight, stitchedHeightmap);
            }
            else
            {
                // PATH 2: Complex self-referential case. We build a multi-step Action<long>.
                
                // 1. Create the temporary texture resource.
                var temporaryTexture = Gpu.CreateTexture2D((uint)maskWidth, (uint)maskHeight, RenderingDevice.DataFormat.R32Sfloat, RenderingDevice.TextureUsageBits.SamplingBit | RenderingDevice.TextureUsageBits.CanCopyToBit | RenderingDevice.TextureUsageBits.StorageBit);
                
                // 2. Get the shader command action and its RIDs before creating the combined action.
                var (shaderAction, shaderTempRids, shaderPath) = CreateTerraceShaderCommands(targetMaskTexture, maskWidth, maskHeight, temporaryTexture);

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

                return (combinedCommands, allTempRidsForTask, shaderPath);
            }
        }

        private (Action<long> commands, List<Rid> tempRids, List<string>) CreateTerraceShaderCommands(Rid targetMaskTexture, int maskWidth, int maskHeight, Rid heightSourceTexture)
        {
            var shaderPath = "res://addons/terrain_3d_tools/Shaders/Masks/terrace_mask.glsl";
            var operation = new AsyncComputeOperation(shaderPath);
            
            operation.BindStorageImage(0, targetMaskTexture);
            operation.BindSamplerWithTexture(1, heightSourceTexture);
            operation.SetPushConstants(BuildPushConstants());

            uint groupsX = (uint)((maskWidth + 7) / 8);
            uint groupsY = (uint)((maskHeight + 7) / 8);

            return (operation.CreateDispatchCommands(groupsX, groupsY), operation.GetTemporaryRids(), new List<string> { shaderPath} );
        }

        private byte[] BuildPushConstants()
        {
            return GpuUtils.CreatePushConstants()
                .Add((int)BlendType)
                .Add(LayerMix)
                .Add(Invert ? 1 : 0)
                .Add((int)Mode)
                .Add(TerraceCount)
                .Add(Sharpness)
                .AddPadding(8)
                .Build();
        }
    }
}
