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
        
        [Export(PropertyHint.Range, "1,32,1")] 
        public int Radius { get => _radius; set => SetProperty(ref _radius, value); }

        [Export(PropertyHint.Range, "0.01,100.0,0.1")] 
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

        public override (Action<long> commands, List<Rid> tempRids, List<string>) CreateApplyCommands(
            Rid targetMaskTexture, int maskWidth, int maskHeight, Rid stitchedHeightmap = new Rid())
        {
            if (UseBaseTerrainHeight)
            {
                if (!stitchedHeightmap.IsValid)
                {
                    GD.PrintErr("[ConcavityMask] UseBaseTerrainHeight is true, but no valid stitched heightmap was provided.");
                    return (null, new List<Rid>(), new List<string>());
                }
                return CreateConcavityShaderCommands(targetMaskTexture, maskWidth, maskHeight, stitchedHeightmap);
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
                    GD.PrintErr("[ConcavityMask] Failed to create temporary texture for self-reference copy");
                    return (null, new List<Rid>(), new List<string>());
                }

                // 2. Create copy commands (compute shader based)
                var (copyAction, copyTempRids, copyShaderPath) = GpuKernels.CreateCopyTextureCommands(
                    targetMaskTexture, temporaryTexture, maskWidth, maskHeight, "ConcavityMask");

                // 3. Create concavity shader commands
                var (shaderAction, shaderTempRids, shaderPaths) = CreateConcavityShaderCommands(
                    targetMaskTexture, maskWidth, maskHeight, temporaryTexture);

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

        private (Action<long> commands, List<Rid> tempRids, List<string>) CreateConcavityShaderCommands(
            Rid targetMaskTexture, int maskWidth, int maskHeight, Rid heightSourceTexture)
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