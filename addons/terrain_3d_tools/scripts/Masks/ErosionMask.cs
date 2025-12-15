using Godot;
using System;
using System.Collections.Generic;
using Terrain3DTools.Core;
using Terrain3DTools.Core.Debug;

namespace Terrain3DTools.Masks
{
    public enum ErosionMaskOutput { ErodedHeight, DepositionMask, FlowMask }

    [GlobalClass, Tool]
    public partial class ErosionMask : TerrainMask
    {
        private const string DEBUG_CLASS_NAME = "ErosionMask";
        public ErosionMask()
        {
            DebugManager.Instance?.RegisterClass(DEBUG_CLASS_NAME);
        }

        #region Droplet Struct
        private struct Droplet
        {
            public Vector2 Position;
            public Vector2 Velocity;
            public float Water;
            public float Sediment;
        }
        #endregion

        #region Exported Properties
        [ExportGroup("Global Flow Carving (Pre-Pass)")]
        [Export] public bool EnableGlobalCarving { get => _enableGlobalCarving; set => SetProperty(ref _enableGlobalCarving, value); }
        [Export(PropertyHint.Range, "1, 20, 1")] public int FlowIterations { get => _flowIterations; set => SetProperty(ref _flowIterations, value); }
        [Export(PropertyHint.Range, "0.0, 2.0, 0.01")] public float CarvingStrength { get => _carvingStrength; set => SetProperty(ref _carvingStrength, value); }
        [Export] public bool EnableFlowBlur { get => _enableFlowBlur; set => SetProperty(ref _enableFlowBlur, value); }
        [Export(PropertyHint.Range, "1, 20, 1")] public int FlowBlurPasses { get => _flowBlurPasses; set => SetProperty(ref _flowBlurPasses, value); }
        [ExportGroup("Hydraulic Erosion Simulation Settings (Detail Pass)")]
        [Export] public bool EnableHydraulicErosion { get => _enableHydraulicErosion; set => SetProperty(ref _enableHydraulicErosion, value); }
        [Export(PropertyHint.Enum)] public ErosionMaskOutput OutputMode { get => _outputMode; set => SetProperty(ref _outputMode, value); }
        [Export(PropertyHint.Range, "1000,2000000,1000")] public int DropletCount { get => _dropletCount; set => SetProperty(ref _dropletCount, value); }
        [Export(PropertyHint.Range, "1,500,1")] public int Iterations { get => _iterations; set => SetProperty(ref _iterations, value); }
        [Export] public int RandomSeed { get => _randomSeed; set => SetProperty(ref _randomSeed, value); }
        [ExportGroup("Hydraulic Erosion Physics Parameters")]
        [Export(PropertyHint.Range, "0.0, 1.0, 0.01")] public float Inertia { get => _inertia; set => SetProperty(ref _inertia, value); }
        [Export(PropertyHint.Range, "0.0, 1.0, 0.001")] public float ErosionStrength { get => _erosionStrength; set => SetProperty(ref _erosionStrength, value); }
        [Export(PropertyHint.Range, "0.0, 1.0, 0.001")] public float DepositionStrength { get => _depositionStrength; set => SetProperty(ref _depositionStrength, value); }
        [Export(PropertyHint.Range, "0.0, 0.2, 0.001")] public float EvaporationRate { get => _evaporationRate; set => SetProperty(ref _evaporationRate, value); }
        [Export(PropertyHint.Range, "1,500,1")] public int MaxLifetime { get => _maxLifetime; set => SetProperty(ref _maxLifetime, value); }
        [Export(PropertyHint.Range, "0, 15, 1")] public int ErosionRadius { get => _erosionRadius; set => SetProperty(ref _erosionRadius, value); }
        [Export(PropertyHint.Range, "0.0, 0.2, 0.001")] public float MaxErosionDepth { get => _maxErosionDepth; set => SetProperty(ref _maxErosionDepth, value); }
        [Export(PropertyHint.Range, "0.0, 50.0, 0.1")] public float Gravity { get => _gravity; set => SetProperty(ref _gravity, value); }
        [Export(PropertyHint.Range, "1.0, 200.0, 1.0")] public float HeightScale { get => _heightScale; set => SetProperty(ref _heightScale, value); }
        [ExportGroup("Thermal Weathering")]
        [Export] public bool EnableThermalWeathering { get => _enableThermalWeathering; set => SetProperty(ref _enableThermalWeathering, value); }
        [Export(PropertyHint.Range, "0.0, 0.5, 0.001")] public float TalusAngle { get => _talusAngle; set => SetProperty(ref _talusAngle, value); }
        [Export(PropertyHint.Range, "0.0, 1.0, 0.01")] public float ThermalStrength { get => _thermalStrength; set => SetProperty(ref _thermalStrength, value); }
        [Export(PropertyHint.Range, "1, 20, 1")] public int ThermalIterations { get => _thermalIterations; set => SetProperty(ref _thermalIterations, value); }
        [ExportGroup("Smoothing (Post-Pass)")]
        [Export] public bool EnableBlur { get => _enableBlur; set => SetProperty(ref _enableBlur, value); }
        [Export(PropertyHint.Range, "1, 10, 1")] public int FinalBlurPasses { get => _finalBlurPasses; set => SetProperty(ref _finalBlurPasses, value); }
        [ExportGroup("Output Remapping (Eroded Height Only)")]
        [Export] public float RemapMin { get => _remapMin; set => SetProperty(ref _remapMin, value); }
        [Export] public float RemapMax { get => _remapMax; set => SetProperty(ref _remapMax, value); }
        [Export] public bool UseBaseTerrainHeight { get => _useBaseTerrainHeight; set => SetProperty(ref _useBaseTerrainHeight, value); }
        #endregion

        #region Private Fields
        private bool _enableHydraulicErosion = true, _enableThermalWeathering = true, _enableGlobalCarving = true, _enableFlowBlur = true, _enableBlur = true, _useBaseTerrainHeight = false;
        private ErosionMaskOutput _outputMode = ErosionMaskOutput.ErodedHeight;
        private int _dropletCount = 65536, _iterations = 50, _randomSeed = 1337, _maxLifetime = 30, _erosionRadius = 1, _thermalIterations = 5, _flowIterations = 10, _flowBlurPasses = 2, _finalBlurPasses = 1;
        private float _inertia = 0.3f, _erosionStrength = 0.1f, _depositionStrength = 0.1f, _evaporationRate = 0.02f, _maxErosionDepth = 0.01f, _gravity = 9.8f, _heightScale = 100.0f, _talusAngle = 0.05f, _thermalStrength = 0.1f, _carvingStrength = 0.5f, _remapMin = -1.0f, _remapMax = 1.0f;
        #endregion

        public override MaskRequirements MaskDataRequirements() => UseBaseTerrainHeight ? MaskRequirements.RequiresHeightData : MaskRequirements.None;
        public override bool RequiresBaseHeightData() => UseBaseTerrainHeight;

        public override (Action<long> commands, List<Rid> tempRids, List<string>) CreateApplyCommands(Rid targetMaskTexture, int maskWidth, int maskHeight, Rid stitchedHeightmap = new Rid())
        {
            Rid heightSource = UseBaseTerrainHeight ? stitchedHeightmap : targetMaskTexture;
            if (!heightSource.IsValid) return (null, new List<Rid>(), new List<string> { "" });

            DebugManager.Instance?.LogMaskConfig(DEBUG_CLASS_NAME,
                $"Setup: {maskWidth}x{maskHeight}, Droplets={DropletCount}, " +
                $"Iterations={Iterations}, GlobalCarving={EnableGlobalCarving}");

            List<string> shaderPaths = new List<string>();
            var tempRidsForTask = new List<Rid>();

            DebugManager.Instance?.StartTimer(DEBUG_CLASS_NAME, DebugCategory.PerformanceTiming, "ErosionMask.Total");

            // Create all temporary resources needed for this run
            var tempHeightmap = Gpu.CreateTexture2D((uint)maskWidth, (uint)maskHeight, RenderingDevice.DataFormat.R32Sfloat, RenderingDevice.TextureUsageBits.StorageBit | RenderingDevice.TextureUsageBits.SamplingBit | RenderingDevice.TextureUsageBits.CanCopyFromBit | RenderingDevice.TextureUsageBits.CanCopyToBit);
            var depositionMap = Gpu.CreateTexture2D((uint)maskWidth, (uint)maskHeight, RenderingDevice.DataFormat.R32Sfloat, RenderingDevice.TextureUsageBits.StorageBit | RenderingDevice.TextureUsageBits.SamplingBit);
            var dropletFlowMap = Gpu.CreateTexture2D((uint)maskWidth, (uint)maskHeight, RenderingDevice.DataFormat.R32Sfloat, RenderingDevice.TextureUsageBits.StorageBit | RenderingDevice.TextureUsageBits.SamplingBit);
            var flowAccumulationMap = Gpu.CreateTexture2D((uint)maskWidth, (uint)maskHeight, RenderingDevice.DataFormat.R32Sfloat, RenderingDevice.TextureUsageBits.StorageBit | RenderingDevice.TextureUsageBits.SamplingBit);
            var blurPingPongGlobal = Gpu.CreateTexture2D((uint)maskWidth, (uint)maskHeight, RenderingDevice.DataFormat.R32Sfloat, RenderingDevice.TextureUsageBits.StorageBit | RenderingDevice.TextureUsageBits.SamplingBit);
            var blurSourceFinal = Gpu.CreateTexture2D((uint)maskWidth, (uint)maskHeight, RenderingDevice.DataFormat.R32Sfloat, RenderingDevice.TextureUsageBits.StorageBit | RenderingDevice.TextureUsageBits.SamplingBit | RenderingDevice.TextureUsageBits.CanCopyToBit | RenderingDevice.TextureUsageBits.CanCopyFromBit);
            var blurPingPongFinal = Gpu.CreateTexture2D((uint)maskWidth, (uint)maskHeight, RenderingDevice.DataFormat.R32Sfloat, RenderingDevice.TextureUsageBits.StorageBit | RenderingDevice.TextureUsageBits.SamplingBit | RenderingDevice.TextureUsageBits.CanCopyToBit);

            var droplets = new Droplet[DropletCount];
            var random = new Random(RandomSeed);
            for (int i = 0; i < DropletCount; i++) droplets[i] = new Droplet { Position = new Vector2((float)random.NextDouble() * maskWidth, (float)random.NextDouble() * maskHeight), Water = 1.0f };
            var dropletBuffer = Gpu.CreateStorageBuffer(droplets);

            tempRidsForTask.AddRange(new[] { tempHeightmap, depositionMap, dropletFlowMap, flowAccumulationMap, blurPingPongGlobal, blurSourceFinal, blurPingPongFinal, dropletBuffer });

            DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.GpuResources,
               $"Allocated {tempRidsForTask.Count} temporary textures");

            Action<long> combinedCommands = (computeList) =>
            {
                uint groupsX = (uint)((maskWidth + 7) / 8);
                uint groupsY = (uint)((maskHeight + 7) / 8);
                uint blurGroupsX = (uint)Math.Ceiling(maskWidth / 256.0);

                // Clear accumulating textures before use
                DebugManager.Instance?.StartTimer(DEBUG_CLASS_NAME, DebugCategory.MaskPasses, "Clear");

                var clearOp = new AsyncComputeOperation("res://addons/terrain_3d_tools/Shaders/Masks/clear.glsl");
                clearOp.BindStorageImage(0, flowAccumulationMap);
                clearOp.CreateDispatchCommands(groupsX, groupsY)?.Invoke(computeList);
                Gpu.Rd.ComputeListAddBarrier(computeList);

                DebugManager.Instance?.EndTimer(DEBUG_CLASS_NAME, DebugCategory.MaskPasses, "Clear");

                Gpu.AddCopyTextureCommand(computeList, heightSource, tempHeightmap, (uint)maskWidth, (uint)maskHeight);
                Gpu.Rd.ComputeListAddBarrier(computeList);

                if (EnableGlobalCarving)
                {
                    DebugManager.Instance?.StartTimer(DEBUG_CLASS_NAME, DebugCategory.MaskPasses, "GlobalCarving");

                    for (int i = 0; i < FlowIterations; i++)
                    {
                        var flowOp = new AsyncComputeOperation("res://addons/terrain_3d_tools/Shaders/Masks/flow_accumulation.glsl");
                        flowOp.BindStorageImage(0, tempHeightmap);
                        flowOp.BindStorageImage(1, flowAccumulationMap);
                        flowOp.CreateDispatchCommands(groupsX, groupsY)?.Invoke(computeList);
                        Gpu.Rd.ComputeListAddBarrier(computeList);
                    }

                    Rid finalFlowMapRid = flowAccumulationMap;
                    if (EnableFlowBlur)
                    {
                        DebugManager.Instance?.StartTimer(DEBUG_CLASS_NAME, DebugCategory.MaskPasses, "FlowBlur");

                        Rid readTex = flowAccumulationMap, writeTex = blurPingPongGlobal;
                        for (int i = 0; i < FlowBlurPasses * 2; i++)
                        {
                            var blurOp = new AsyncComputeOperation("res://addons/terrain_3d_tools/Shaders/Masks/gaussian_blur.glsl");
                            blurOp.BindStorageImage(0, writeTex);
                            blurOp.BindSamplerWithTexture(1, readTex);
                            blurOp.SetPushConstants(GpuUtils.CreatePushConstants().Add((i % 2 == 0) ? new Vector2I(1, 0) : new Vector2I(0, 1)).AddPadding(8).Build());
                            blurOp.CreateDispatchCommands(blurGroupsX, (uint)maskHeight)?.Invoke(computeList);
                            Gpu.Rd.ComputeListAddBarrier(computeList);
                            (readTex, writeTex) = (writeTex, readTex);
                        }
                        finalFlowMapRid = readTex;

                        DebugManager.Instance?.EndTimer(DEBUG_CLASS_NAME, DebugCategory.MaskPasses, "FlowBlur");
                    }

                    var carveOp = new AsyncComputeOperation("res://addons/terrain_3d_tools/Shaders/Masks/channel_carve.glsl");
                    carveOp.BindStorageImage(0, tempHeightmap);
                    carveOp.BindStorageImage(1, finalFlowMapRid);
                    carveOp.SetPushConstants(GpuUtils.CreatePushConstants().Add(CarvingStrength).AddPadding(12).Build());
                    carveOp.CreateDispatchCommands(groupsX, groupsY)?.Invoke(computeList);
                    Gpu.Rd.ComputeListAddBarrier(computeList);

                    DebugManager.Instance?.EndTimer(DEBUG_CLASS_NAME, DebugCategory.MaskPasses, "GlobalCarving");
                }

                if (EnableHydraulicErosion)
                {
                    // Clear maps for this pass
                    DebugManager.Instance?.StartTimer(DEBUG_CLASS_NAME, DebugCategory.MaskPasses, "HydraulicErosion");

                    var clearOpHydraulic = new AsyncComputeOperation("res://addons/terrain_3d_tools/Shaders/Masks/clear.glsl");
                    clearOpHydraulic.BindStorageImage(0, depositionMap);
                    clearOpHydraulic.CreateDispatchCommands(groupsX, groupsY)?.Invoke(computeList);
                    clearOpHydraulic.BindStorageImage(0, dropletFlowMap);
                    clearOpHydraulic.CreateDispatchCommands(groupsX, groupsY)?.Invoke(computeList);
                    Gpu.Rd.ComputeListAddBarrier(computeList);

                    uint dropletGroups = (uint)Math.Ceiling(DropletCount / 64.0);

                    DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.PerformanceMetrics,
                        $"Simulating {DropletCount} droplets in {dropletGroups} groups");

                    for (int i = 0; i < Iterations; i++)
                    {
                        DebugManager.Instance?.LogMaskPass(DEBUG_CLASS_NAME, "ErosionIteration", i, Iterations);

                        var erosionOp = new AsyncComputeOperation("res://addons/terrain_3d_tools/Shaders/Masks/erosion_simulation.glsl");
                        erosionOp.BindStorageImage(0, tempHeightmap);
                        erosionOp.BindStorageImage(1, depositionMap);
                        erosionOp.BindStorageImage(2, dropletFlowMap);
                        erosionOp.BindStorageBuffer(3, dropletBuffer);
                        erosionOp.SetPushConstants(GpuUtils.CreatePushConstants().Add(Inertia).Add(ErosionStrength).Add(DepositionStrength).Add(EvaporationRate).Add(MaxLifetime).Add(maskWidth).Add(maskHeight).Add(i + RandomSeed).Add(Gravity).Add(HeightScale).Add(MaxErosionDepth).Add(ErosionRadius).Build());
                        erosionOp.CreateDispatchCommands(dropletGroups)?.Invoke(computeList);
                        Gpu.Rd.ComputeListAddBarrier(computeList);
                    }
                    DebugManager.Instance?.EndTimer(DEBUG_CLASS_NAME, DebugCategory.MaskPasses, "HydraulicErosion");
                }   

                if (EnableThermalWeathering)
                {
                    DebugManager.Instance?.StartTimer(DEBUG_CLASS_NAME, DebugCategory.MaskPasses, "ThermalWeathering");

                    for (int i = 0; i < ThermalIterations; i++)
                    {
                        var thermalOp = new AsyncComputeOperation("res://addons/terrain_3d_tools/Shaders/Masks/thermal_erosion.glsl");
                        thermalOp.BindStorageImage(0, tempHeightmap);
                        thermalOp.SetPushConstants(GpuUtils.CreatePushConstants().Add(TalusAngle).Add(ThermalStrength).Add(maskWidth).Add(maskHeight).Build());
                        thermalOp.CreateDispatchCommands(groupsX, groupsY)?.Invoke(computeList);
                        Gpu.Rd.ComputeListAddBarrier(computeList);
                    }
                    
                    DebugManager.Instance?.EndTimer(DEBUG_CLASS_NAME, DebugCategory.MaskPasses, "ThermalWeathering");
                }

                if (EnableBlur && FinalBlurPasses > 0)
                {
                    DebugManager.Instance?.StartTimer(DEBUG_CLASS_NAME, DebugCategory.MaskPasses, "FinalBlur");

                    Gpu.AddCopyTextureCommand(computeList, tempHeightmap, blurSourceFinal, (uint)maskWidth, (uint)maskHeight);
                    Gpu.Rd.ComputeListAddBarrier(computeList);
                    Rid readTex = blurSourceFinal, writeTex = blurPingPongFinal;
                    for (int i = 0; i < FinalBlurPasses * 2; i++)
                    {
                        var blurOp = new AsyncComputeOperation("res://addons/terrain_3d_tools/Shaders/Masks/gaussian_blur.glsl");
                        blurOp.BindStorageImage(0, writeTex);
                        blurOp.BindSamplerWithTexture(1, readTex);
                        blurOp.SetPushConstants(GpuUtils.CreatePushConstants().Add((i % 2 == 0) ? new Vector2I(1, 0) : new Vector2I(0, 1)).AddPadding(8).Build());
                        blurOp.CreateDispatchCommands(blurGroupsX, (uint)maskHeight)?.Invoke(computeList);
                        Gpu.Rd.ComputeListAddBarrier(computeList);
                        (readTex, writeTex) = (writeTex, readTex);
                    }

                    Gpu.AddCopyTextureCommand(computeList, readTex, tempHeightmap, (uint)maskWidth, (uint)maskHeight);
                    Gpu.Rd.ComputeListAddBarrier(computeList);

                    DebugManager.Instance?.EndTimer(DEBUG_CLASS_NAME, DebugCategory.MaskPasses, "FinalBlur");
                }

                DebugManager.Instance?.StartTimer(DEBUG_CLASS_NAME, DebugCategory.MaskBlending, "FinalBlend");

                Rid finalSourceTexture;
                switch (OutputMode)
                {
                    case ErosionMaskOutput.DepositionMask: finalSourceTexture = depositionMap; break;
                    case ErosionMaskOutput.FlowMask: finalSourceTexture = dropletFlowMap; break;
                    default: finalSourceTexture = tempHeightmap; break;
                }
                var blendOp = new AsyncComputeOperation("res://addons/terrain_3d_tools/Shaders/Masks/erosion_blend.glsl");
                blendOp.BindStorageImage(0, targetMaskTexture);
                blendOp.BindSamplerWithTexture(1, finalSourceTexture);
                blendOp.SetPushConstants(GpuUtils.CreatePushConstants().Add((int)BlendType).Add(LayerMix).Add(Invert).Add((int)OutputMode).Add(RemapMin).Add(RemapMax).AddPadding(8).Build());
                blendOp.CreateDispatchCommands(groupsX, groupsY)?.Invoke(computeList);
                Gpu.Rd.ComputeListAddBarrier(computeList);

                DebugManager.Instance?.EndTimer(DEBUG_CLASS_NAME, DebugCategory.MaskBlending, "FinalBlend");

                // END OVERALL TIMER
                DebugManager.Instance?.EndTimer(DEBUG_CLASS_NAME, DebugCategory.PerformanceTiming, "ErosionMask.Total");
            };

            return (combinedCommands, tempRidsForTask, shaderPaths);
        }
    }
}