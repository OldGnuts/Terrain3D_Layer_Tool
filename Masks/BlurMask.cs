// /Masks/BlurMask.cs
using Godot;
using System;
using System.Collections.Generic;
using Terrain3DTools.Core;
using Terrain3DTools.Core.Debug;

namespace Terrain3DTools.Masks
{
    /// <summary>
    /// A mask that applies Gaussian blur to smooth the terrain or mask values.
    /// Uses separable (horizontal + vertical) blur passes for efficiency.
    /// </summary>
    [GlobalClass, Tool]
    public partial class BlurMask : TerrainMask
    {
        private const string DEBUG_CLASS_NAME = "BlurMask";

        #region Private Fields
        private int _passes = 1;
        private float _sampleDistance = 1.0f;
        #endregion

        #region Exported Properties
        /// <summary>
        /// Number of blur passes. Each pass applies horizontal then vertical blur.
        /// More passes = stronger blur effect.
        /// </summary>
        [Export(PropertyHint.Range, "1,20,1")]
        public int Passes
        {
            get => _passes;
            set => SetProperty(ref _passes, Mathf.Clamp(value, 1, 20));
        }

        /// <summary>
        /// Sample distance multiplier. Higher values spread samples further apart,
        /// creating a wider blur effect without additional passes.
        /// </summary>
        [Export(PropertyHint.Range, "0.5,10.0,0.1")]
        public float SampleDistance
        {
            get => _sampleDistance;
            set => SetProperty(ref _sampleDistance, Mathf.Clamp(value, 0.5f, 10.0f));
        }

        /// <summary>
        /// When true, uses the stitched heightmap as input (for height-based smoothing).
        /// When false, operates on the current mask texture (self-referential).
        /// </summary>
        [Export]
        public bool UseBaseTerrainHeight { get; set; } = false;
        #endregion

        public BlurMask()
        {
            BlendType = MaskBlendType.Mix;
            LayerMix = 1.0f;
            DebugManager.Instance?.RegisterClass(DEBUG_CLASS_NAME);
        }

        public override MaskRequirements MaskDataRequirements() => 
            UseBaseTerrainHeight ? MaskRequirements.RequiresHeightData : MaskRequirements.None;
        
        public override bool RequiresBaseHeightData() => UseBaseTerrainHeight;

        public override (Action<long> commands, List<Rid> tempRids, List<string>) CreateApplyCommands(
            Rid targetMaskTexture, int maskWidth, int maskHeight, Rid stitchedHeightmap = new Rid())
        {
            // Determine input source
            Rid inputSource;
            if (UseBaseTerrainHeight)
            {
                if (!stitchedHeightmap.IsValid)
                {
                    GD.PrintErr("[BlurMask] UseBaseTerrainHeight is true, but no valid stitched heightmap was provided.");
                    return (null, new List<Rid>(), new List<string>());
                }
                inputSource = stitchedHeightmap;
            }
            else
            {
                inputSource = targetMaskTexture;
            }

            var shaderPaths = new List<string>();
            var operationRids = new List<Rid>();
            var ownerRids = new List<Rid>();

            // Create ping-pong textures for separable blur
            var pingTexture = Gpu.CreateTexture2D(
                (uint)maskWidth, (uint)maskHeight,
                RenderingDevice.DataFormat.R32Sfloat,
                RenderingDevice.TextureUsageBits.StorageBit |
                RenderingDevice.TextureUsageBits.SamplingBit);

            var pongTexture = Gpu.CreateTexture2D(
                (uint)maskWidth, (uint)maskHeight,
                RenderingDevice.DataFormat.R32Sfloat,
                RenderingDevice.TextureUsageBits.StorageBit |
                RenderingDevice.TextureUsageBits.SamplingBit);

            if (!pingTexture.IsValid || !pongTexture.IsValid)
            {
                GD.PrintErr("[BlurMask] Failed to create ping-pong textures");
                if (pingTexture.IsValid) Gpu.FreeRid(pingTexture);
                if (pongTexture.IsValid) Gpu.FreeRid(pongTexture);
                return (null, new List<Rid>(), new List<string>());
            }

            ownerRids.Add(pingTexture);
            ownerRids.Add(pongTexture);

            uint groupsX = (uint)((maskWidth + 7) / 8);
            uint groupsY = (uint)((maskHeight + 7) / 8);

            string blurShaderPath = "res://addons/terrain_3d_tools/Shaders/Masks/gaussian_blur.glsl";

            // Initial copy: input -> pingTexture
            var (copyCmd, copyTempRids, copyShaderPath) = GpuKernels.CreateCopyTextureCommands(
                inputSource, pingTexture, maskWidth, maskHeight, DEBUG_CLASS_NAME);
            operationRids.AddRange(copyTempRids);
            if (!string.IsNullOrEmpty(copyShaderPath)) shaderPaths.Add(copyShaderPath);

            // Setup blur operations
            // Horizontal: ping -> pong
            var blurHOp = new AsyncComputeOperation(blurShaderPath);
            blurHOp.BindStorageImage(0, pongTexture);
            blurHOp.BindSamplerWithTexture(1, pingTexture);
            blurHOp.SetPushConstants(BuildBlurPushConstants(1, 0, SampleDistance));
            blurHOp.CreateDispatchCommands(1, 1, 1);

            Rid blurHPipeline = blurHOp.Pipeline;
            var blurHRids = blurHOp.GetTemporaryRids();
            Rid blurHUniformSet = blurHRids.Count > 0 ? blurHRids[blurHRids.Count - 1] : new Rid();
            operationRids.AddRange(blurHRids);
            shaderPaths.Add(blurShaderPath);

            // Vertical: pong -> ping
            var blurVOp = new AsyncComputeOperation(blurShaderPath);
            blurVOp.BindStorageImage(0, pingTexture);
            blurVOp.BindSamplerWithTexture(1, pongTexture);
            blurVOp.SetPushConstants(BuildBlurPushConstants(0, 1, SampleDistance));
            blurVOp.CreateDispatchCommands(1, 1, 1);

            Rid blurVPipeline = blurVOp.Pipeline;
            var blurVRids = blurVOp.GetTemporaryRids();
            Rid blurVUniformSet = blurVRids.Count > 0 ? blurVRids[blurVRids.Count - 1] : new Rid();
            operationRids.AddRange(blurVRids);

            // Final copy/blend: ping -> target
            var blendOp = new AsyncComputeOperation("res://addons/terrain_3d_tools/Shaders/Masks/blur_blend.glsl");
            blendOp.BindStorageImage(0, targetMaskTexture);
            blendOp.BindSamplerWithTexture(1, pingTexture);
            blendOp.SetPushConstants(GpuUtils.CreatePushConstants()
                .Add((int)BlendType)
                .Add(LayerMix)
                .Add(Invert ? 1 : 0)
                .Add(0) // padding
                .Build());
            Action<long> blendCmd = blendOp.CreateDispatchCommands(groupsX, groupsY);
            operationRids.AddRange(blendOp.GetTemporaryRids());
            shaderPaths.Add("res://addons/terrain_3d_tools/Shaders/Masks/blur_blend.glsl");

            // Capture parameters
            int capturedPasses = Passes;
            float capturedSampleDistance = SampleDistance;

            // Build execution lambda
            Action<long> combinedCommands = (computeList) =>
            {
                // 1. Copy input to ping texture
                copyCmd?.Invoke(computeList);
                Gpu.Rd.ComputeListAddBarrier(computeList);

                // 2. Execute blur passes
                if (blurHPipeline.IsValid && blurHUniformSet.IsValid &&
                    blurVPipeline.IsValid && blurVUniformSet.IsValid)
                {
                    byte[] hPushConstants = BuildBlurPushConstants(1, 0, capturedSampleDistance);
                    byte[] vPushConstants = BuildBlurPushConstants(0, 1, capturedSampleDistance);

                    for (int p = 0; p < capturedPasses; p++)
                    {
                        // Horizontal: ping -> pong
                        Gpu.AddDispatchToComputeList(computeList, blurHPipeline, blurHUniformSet, 
                            hPushConstants, groupsX, groupsY, 1);
                        Gpu.Rd.ComputeListAddBarrier(computeList);

                        // Vertical: pong -> ping
                        Gpu.AddDispatchToComputeList(computeList, blurVPipeline, blurVUniformSet, 
                            vPushConstants, groupsX, groupsY, 1);
                        Gpu.Rd.ComputeListAddBarrier(computeList);
                    }
                }

                // 3. Blend result to target
                blendCmd?.Invoke(computeList);
            };

            // Build cleanup list
            var finalCleanupList = new List<Rid>(operationRids);
            finalCleanupList.AddRange(ownerRids);

            return (combinedCommands, finalCleanupList, shaderPaths);
        }

        /// <summary>
        /// Builds 16-byte aligned push constants for the blur shader.
        /// </summary>
        private static byte[] BuildBlurPushConstants(int dirX, int dirY, float sampleDistance)
        {
            return GpuUtils.CreatePushConstants()
                .Add(dirX)              // 4 bytes
                .Add(dirY)              // 4 bytes
                .Add(sampleDistance)    // 4 bytes
                .Add(0.0f)              // 4 bytes padding (16-byte alignment)
                .Build();
        }
    }
}