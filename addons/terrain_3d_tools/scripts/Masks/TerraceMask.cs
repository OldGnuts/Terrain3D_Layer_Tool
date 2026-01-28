// /Masks/TerraceMask.cs (Corrected for Action<long>)
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


        public override (Action<long> commands, List<Rid> tempRids, List<string>) CreateApplyCommands(
    Rid targetMaskTexture, int maskWidth, int maskHeight, Rid stitchedHeightmap = new Rid())
        {
            if (UseBaseTerrainHeight)
            {
                if (!stitchedHeightmap.IsValid)
                {
                    GD.PrintErr("[TerraceMask] UseBaseTerrainHeight is true, but no valid stitched heightmap was provided.");
                    return (null, new List<Rid>(), new List<string>());
                }
                return CreateTerraceShaderCommands(targetMaskTexture, maskWidth, maskHeight, stitchedHeightmap);
            }
            else
            {
                // PATH 2: Self-referential case - copy target to temp, then use temp as height source

                // 1. Create temporary texture
                var temporaryTexture = Gpu.CreateTexture2D(
                    (uint)maskWidth, (uint)maskHeight,
                    RenderingDevice.DataFormat.R32Sfloat,
                    RenderingDevice.TextureUsageBits.SamplingBit |
                    RenderingDevice.TextureUsageBits.StorageBit);

                if (!temporaryTexture.IsValid)
                {
                    GD.PrintErr("[TerraceMask] Failed to create temporary texture for self-reference copy");
                    return (null, new List<Rid>(), new List<string>());
                }

                // 2. Create copy commands (compute shader based)
                var (copyAction, copyTempRids, copyShaderPath) = GpuKernels.CreateCopyTextureCommands(
                    targetMaskTexture, temporaryTexture, maskWidth, maskHeight, "TerraceMask");

                // 3. Create terrace shader commands
                var (shaderAction, shaderTempRids, shaderPaths) = CreateTerraceShaderCommands(
                    targetMaskTexture, maskWidth, maskHeight, temporaryTexture);

                // 4. Combine commands
                Action<long> combinedCommands = (computeList) =>
                {
                    // First: copy current mask state to temp texture
                    copyAction?.Invoke(computeList);

                    // Barrier between copy and terrace shader
                    Gpu.Rd.ComputeListAddBarrier(computeList);

                    // Then: run terrace shader using temp as height source
                    shaderAction?.Invoke(computeList);
                };

                // 5. Collect all temp RIDs - temporaryTexture must be included
                var allTempRids = new List<Rid>();
                allTempRids.AddRange(copyTempRids);      // Copy shader uniform sets
                allTempRids.AddRange(shaderTempRids);    // Terrace shader uniform sets
                allTempRids.Add(temporaryTexture);       // The temp texture we created

                // 6. Collect all shader paths
                var allShaderPaths = new List<string>();
                if (!string.IsNullOrEmpty(copyShaderPath)) allShaderPaths.Add(copyShaderPath);
                allShaderPaths.AddRange(shaderPaths);

                return (combinedCommands, allTempRids, allShaderPaths);
            }
        }

        private (Action<long> commands, List<Rid> tempRids, List<string>) CreateTerraceShaderCommands(
            Rid targetMaskTexture, int maskWidth, int maskHeight, Rid heightSourceTexture)
        {
            var shaderPath = "res://addons/terrain_3d_tools/Shaders/Masks/terrace_mask.glsl";
            var operation = new AsyncComputeOperation(shaderPath);

            operation.BindStorageImage(0, targetMaskTexture);
            operation.BindSamplerWithTexture(1, heightSourceTexture);
            operation.SetPushConstants(BuildPushConstants());

            uint groupsX = (uint)((maskWidth + 7) / 8);
            uint groupsY = (uint)((maskHeight + 7) / 8);

            return (operation.CreateDispatchCommands(groupsX, groupsY), operation.GetTemporaryRids(), new List<string> { shaderPath });
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