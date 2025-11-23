// /Masks/ConcavityMask.cs
using Godot;
using System;
using System.Collections.Generic;
using Terrain3DTools.Core;
using Terrain3DTools.Utils;

namespace Terrain3DTools.Masks
{
    public enum ConcavityMode { Concave, Convex, Both }

    [GlobalClass, Tool]
    public partial class ConcavityMask : TerrainMask
    {
        #region Private Fields
        private ConcavityMode _mode = ConcavityMode.Concave;
        private int _radius = 3;
        private float _strength = 1.0f;
        #endregion

        #region Exported Properties
        [Export(PropertyHint.Enum, "Concave:Selects valleys, Convex:Selects ridges, Both:Selects both")] 
        public ConcavityMode Mode { get => _mode; set => SetProperty(ref _mode, value); }
        
        [Export(PropertyHint.Range, "1,32,1,")] 
        public int Radius { get => _radius; set => SetProperty(ref _radius, value); }

        [Export(PropertyHint.Range, "0.01, 100.0, 0.1")] 
        public float Strength { get => _strength; set => SetProperty(ref _strength, value); }
        
        [Export]
        public bool UseBaseTerrainHeight { get; set; } = true;
        #endregion

        public ConcavityMask()
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
                    GD.PrintErr("[ConcavityMask] UseBaseTerrainHeight is true, but no valid stitched heightmap was provided.");
                    return (null, new List<Rid>(), new List<string> { "" });
                }
                return CreateConcavityShaderCommands(targetMaskTexture, maskWidth, maskHeight, stitchedHeightmap);
            }
            else
            {
                // PATH 2: Complex case, self-referential. We build a multi-step Action<long>.
                
                // 1. Create the temporary texture resource needed for this operation.
                Rid temporaryTexture = Gpu.CreateTexture2D((uint)maskWidth, (uint)maskHeight, RenderingDevice.DataFormat.R32Sfloat, RenderingDevice.TextureUsageBits.StorageBit | RenderingDevice.TextureUsageBits.SamplingBit | RenderingDevice.TextureUsageBits.CanCopyToBit);
                
                // 2. Get the shader command action and its temporary RIDs *before* creating the combined action.
                var (shaderAction, shaderTempRids, shaderPath) = CreateConcavityShaderCommands(targetMaskTexture, maskWidth, maskHeight, temporaryTexture);

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

        private (Action<long> commands, List<Rid> tempRids, List<string>) CreateConcavityShaderCommands(Rid targetMaskTexture, int maskWidth, int maskHeight, Rid heightSourceTexture)
        {
            var shaderPath = "res://addons/terrain_3d_tools/Shaders/Masks/concavity_mask.glsl";
            var operation = new AsyncComputeOperation(shaderPath);

            operation.BindStorageImage(0, targetMaskTexture);
            operation.BindSamplerWithTexture(1, heightSourceTexture);

            var pcb = GpuUtils.CreatePushConstants()
                .Add((int)BlendType)
                .Add(LayerMix)
                .Add(Invert ? 1 : 0)
                .Add((int)Mode)
                .Add(Radius)
                .Add(Strength)
                .AddPadding(8)
                .Build();

            operation.SetPushConstants(pcb);

            uint groupsX = (uint)((maskWidth + 7) / 8);
            uint groupsY = (uint)((maskHeight + 7) / 8);

            return (operation.CreateDispatchCommands(groupsX, groupsY), operation.GetTemporaryRids(), new List<string> { shaderPath });
        }
    }
}

