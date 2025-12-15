// /Masks/NoiseMask.cs (Corrected for Action<long>)
using Godot;
using System;
using System.Collections.Generic;
using Terrain3DTools.Core;
using Terrain3DTools.Utils;

namespace Terrain3DTools.Masks
{
    /// <summary>
    /// A procedural noise mask that generates influence patterns using various noise algorithms.
    /// </summary>
    [GlobalClass, Tool]
    public partial class NoiseMask : TerrainMask
    {
        // ... (Properties and fields remain the same) ...
        #region Private Fields & Exported Properties
        private int _noiseType = 0;
        private int _seed = 1337;
        private int _octaves = 4;
        private float _frequency = 0.01f;
        private float _amplitude = 100f;
        private float _lacunarity = 2.0f;
        private float _gain = 0.5f;
        private float _ridgeOffset = 1.0f;
        private float _ridgeGain = 2.0f;

        [Export(PropertyHint.Enum, "Value,Perlin,Simplex,Ridge")]
        public int NoiseType { get => _noiseType; set => SetProperty(ref _noiseType, value); }
        [Export] public int Seed { get => _seed; set => SetProperty(ref _seed, value); }
        [Export(PropertyHint.Range, "1,12,1")]
        public int Octaves { get => _octaves; set => SetProperty(ref _octaves, value); }
        [Export(PropertyHint.Range, "0.001,2.0,0.001")]
        public float Frequency { get => _frequency; set => SetProperty(ref _frequency, value); }
        [Export(PropertyHint.Range, "0.0,100.0,0.01")]
        public float Amplitude { get => _amplitude; set => SetProperty(ref _amplitude, value); }
        [Export(PropertyHint.Range, "1.0,5.0,0.1")]
        public float Lacunarity { get => _lacunarity; set => SetProperty(ref _lacunarity, value); }
        [Export(PropertyHint.Range, "0.0,1.0,0.01")]
        public float Gain { get => _gain; set => SetProperty(ref _gain, value); }
        [Export(PropertyHint.Range, "0.0,2.0,0.01")]
        public float RidgeOffset { get => _ridgeOffset; set => SetProperty(ref _ridgeOffset, value); }
        [Export(PropertyHint.Range, "0.0,5.0,0.01")]
        public float RidgeGain { get => _ridgeGain; set => SetProperty(ref _ridgeGain, value); }
        #endregion

        public override MaskRequirements MaskDataRequirements() => MaskRequirements.None;
        
        /// <summary>
        /// Creates the GPU commands to apply a procedural noise effect to the target mask.
        /// </summary>
        // --- START OF CORRECTION ---
        public override (Action<long> commands, List<Rid> tempRids, List<string>) CreateApplyCommands(Rid targetMaskTexture, int maskWidth, int maskHeight, Rid stitchedHeightmap = new Rid())
        {
            var shaderPath = "res://addons/terrain_3d_tools/Shaders/Masks/noise.glsl";
            var op = new AsyncComputeOperation(shaderPath);
            
            op.BindStorageImage(0, targetMaskTexture);

            var pushConstants = GpuUtils.CreatePushConstants()
                .Add(NoiseType)
                .Add(Seed)
                .Add(Invert ? 1 : 0)
                .Add(Octaves)
                .Add(Frequency)
                .Add(Amplitude)
                .Add(Lacunarity)
                .Add(Gain)
                .Add(RidgeOffset)
                .Add(RidgeGain)
                .Add(LayerMix)
                .Add((int)BlendType)
                .Build();

            op.SetPushConstants(pushConstants);

            uint groupsX = (uint)((maskWidth + 7) / 8);
            uint groupsY = (uint)((maskHeight + 7) / 8);

            // This return is now valid.
            return (op.CreateDispatchCommands(groupsX, groupsY), op.GetTemporaryRids(), new List<string> { shaderPath });
        }
    }
}