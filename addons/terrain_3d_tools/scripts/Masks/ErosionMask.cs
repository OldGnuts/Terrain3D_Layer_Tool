// /Masks/ErosionMask.cs
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using Terrain3DTools.Core;
using Terrain3DTools.Core.Debug;

namespace Terrain3DTools.Masks
{
    public enum ErosionMaskOutput
    {
        ErodedHeight,
        DepositionMask,
        FlowMask
    }

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

        public override (Action<long> commands, List<Rid> tempRids, List<string>) CreateApplyCommands(
            Rid targetMaskTexture, int maskWidth, int maskHeight, Rid stitchedHeightmap = new Rid())
        {
            Rid heightSource = UseBaseTerrainHeight ? stitchedHeightmap : targetMaskTexture;
            if (!heightSource.IsValid)
            {
                DebugManager.Instance?.LogWarning(DEBUG_CLASS_NAME, "Erosion skipped: Invalid height source.");
                return (null, new List<Rid>(), new List<string>());
            }

            DebugManager.Instance?.StartTimer(DEBUG_CLASS_NAME, DebugCategory.PerformanceTiming, "ErosionMask.Prepare");

            var shaderPaths = new List<string>();
            var operationRids = new List<Rid>();
            var ownerRids = new List<Rid>();

            // --- 1. Resource Allocation ---
            var tempHeightmap = Gpu.CreateTexture2D(
                (uint)maskWidth, (uint)maskHeight,
                RenderingDevice.DataFormat.R32Sfloat,
                RenderingDevice.TextureUsageBits.StorageBit |
                RenderingDevice.TextureUsageBits.SamplingBit |
                RenderingDevice.TextureUsageBits.CanCopyFromBit |
                RenderingDevice.TextureUsageBits.CanCopyToBit);

            var depositionMap = Gpu.CreateTexture2D(
                (uint)maskWidth, (uint)maskHeight,
                RenderingDevice.DataFormat.R32Sfloat,
                RenderingDevice.TextureUsageBits.StorageBit |
                RenderingDevice.TextureUsageBits.SamplingBit);

            var dropletFlowMap = Gpu.CreateTexture2D(
                (uint)maskWidth, (uint)maskHeight,
                RenderingDevice.DataFormat.R32Sfloat,
                RenderingDevice.TextureUsageBits.StorageBit |
                RenderingDevice.TextureUsageBits.SamplingBit);

            var flowAccumulationMap = Gpu.CreateTexture2D(
                (uint)maskWidth, (uint)maskHeight,
                RenderingDevice.DataFormat.R32Sfloat,
                RenderingDevice.TextureUsageBits.StorageBit |
                RenderingDevice.TextureUsageBits.SamplingBit);

            // Blur ping-pong texture (needed for separable blur)
            var blurPingPong = Gpu.CreateTexture2D(
                (uint)maskWidth, (uint)maskHeight,
                RenderingDevice.DataFormat.R32Sfloat,
                RenderingDevice.TextureUsageBits.StorageBit |
                RenderingDevice.TextureUsageBits.SamplingBit);

            // Droplet buffer
            var droplets = new Droplet[DropletCount];
            var random = new Random(RandomSeed);
            for (int i = 0; i < DropletCount; i++)
            {
                droplets[i] = new Droplet
                {
                    Position = new Vector2((float)random.NextDouble() * maskWidth, (float)random.NextDouble() * maskHeight),
                    Water = 1.0f
                };
            }
            var dropletBuffer = Gpu.CreateStorageBuffer(droplets);

            ownerRids.AddRange(new[] { tempHeightmap, depositionMap, dropletFlowMap, flowAccumulationMap, blurPingPong, dropletBuffer });

            uint groupsX = (uint)((maskWidth + 7) / 8);
            uint groupsY = (uint)((maskHeight + 7) / 8);

            // Blur dispatch sizes (shader uses local_size_x = 256, local_size_y = 1)
            uint blurGroupsX = (uint)((maskWidth + 255) / 256);
            uint blurGroupsY = (uint)maskHeight;

            // --- 2. Operation Setup ---

            // A. INITIAL COPY
            var (copyCmd, copyTempRids, copyShaderPath) = GpuKernels.CreateCopyTextureCommands(
                heightSource, tempHeightmap, maskWidth, maskHeight, DEBUG_CLASS_NAME);
            operationRids.AddRange(copyTempRids);
            if (!string.IsNullOrEmpty(copyShaderPath)) shaderPaths.Add(copyShaderPath);

            // B. CLEAR OPERATIONS (NO push constants)
            var clearDepositionOp = new AsyncComputeOperation("res://addons/terrain_3d_tools/Shaders/Masks/clear.glsl");
            clearDepositionOp.BindStorageImage(0, depositionMap);
            // NO SetPushConstants - clear.glsl doesn't use them
            Action<long> clearDepositionCmd = clearDepositionOp.CreateDispatchCommands(groupsX, groupsY);
            operationRids.AddRange(clearDepositionOp.GetTemporaryRids());
            shaderPaths.Add("res://addons/terrain_3d_tools/Shaders/Masks/clear.glsl");

            var clearFlowOp = new AsyncComputeOperation("res://addons/terrain_3d_tools/Shaders/Masks/clear.glsl");
            clearFlowOp.BindStorageImage(0, dropletFlowMap);
            // NO SetPushConstants
            Action<long> clearFlowCmd = clearFlowOp.CreateDispatchCommands(groupsX, groupsY);
            operationRids.AddRange(clearFlowOp.GetTemporaryRids());

            var clearFlowAccumOp = new AsyncComputeOperation("res://addons/terrain_3d_tools/Shaders/Masks/clear.glsl");
            clearFlowAccumOp.BindStorageImage(0, flowAccumulationMap);
            // NO SetPushConstants
            Action<long> clearFlowAccumCmd = clearFlowAccumOp.CreateDispatchCommands(groupsX, groupsY);
            operationRids.AddRange(clearFlowAccumOp.GetTemporaryRids());

            // C. GLOBAL CARVING SETUP
            Rid flowPipeline = new Rid();
            Rid flowUniformSet = new Rid();
            Action<long> carveCmd = null;

            if (EnableGlobalCarving)
            {
                // Flow accumulation (NO push constants)
                var flowOp = new AsyncComputeOperation("res://addons/terrain_3d_tools/Shaders/Masks/flow_accumulation.glsl");
                flowOp.BindStorageImage(0, tempHeightmap);
                flowOp.BindStorageImage(1, flowAccumulationMap);
                // NO SetPushConstants - flow_accumulation.glsl doesn't use them
                flowOp.CreateDispatchCommands(1, 1, 1);

                flowPipeline = flowOp.Pipeline;
                var flowRids = flowOp.GetTemporaryRids();
                if (flowRids.Count > 0) flowUniformSet = flowRids[flowRids.Count - 1];
                operationRids.AddRange(flowRids);
                shaderPaths.Add("res://addons/terrain_3d_tools/Shaders/Masks/flow_accumulation.glsl");

                // Channel carve (needs push constants - 16 byte aligned)
                var carveOp = new AsyncComputeOperation("res://addons/terrain_3d_tools/Shaders/Masks/channel_carve.glsl");
                carveOp.BindStorageImage(0, tempHeightmap);
                carveOp.BindStorageImage(1, flowAccumulationMap);
                carveOp.SetPushConstants(GpuUtils.CreatePushConstants()
                    .Add(CarvingStrength)   // 4 bytes
                    .AddPadding(12)         // 12 bytes padding = 16 bytes total
                    .Build());
                carveCmd = carveOp.CreateDispatchCommands(groupsX, groupsY);
                operationRids.AddRange(carveOp.GetTemporaryRids());
                shaderPaths.Add("res://addons/terrain_3d_tools/Shaders/Masks/channel_carve.glsl");
            }

            // D. BLUR SETUP (separable: horizontal then vertical)
            Rid blurHPipeline = new Rid();
            Rid blurHUniformSet = new Rid();
            Rid blurVPipeline = new Rid();
            Rid blurVUniformSet = new Rid();

            if (EnableFlowBlur || EnableBlur)
            {
                // Horizontal blur: read from tempHeightmap, write to blurPingPong
                var blurHOp = new AsyncComputeOperation("res://addons/terrain_3d_tools/Shaders/Masks/gaussian_blur.glsl");
                blurHOp.BindStorageImage(0, blurPingPong);
                blurHOp.BindSamplerWithTexture(1, tempHeightmap);
                blurHOp.SetPushConstants(GpuUtils.CreatePushConstants()
                    .Add(1)         // direction_x (int) - 4 bytes
                    .Add(0)         // direction_y (int) - 4 bytes
                    .Add(1.0f)      // sample_distance (float) - 4 bytes
                    .Add(0.0f)      // padding - 4 bytes = 16 bytes total
                    .Build());
                blurHOp.CreateDispatchCommands(1, 1, 1);

                blurHPipeline = blurHOp.Pipeline;
                var blurHRids = blurHOp.GetTemporaryRids();
                if (blurHRids.Count > 0) blurHUniformSet = blurHRids[blurHRids.Count - 1];
                operationRids.AddRange(blurHRids);
                shaderPaths.Add("res://addons/terrain_3d_tools/Shaders/Masks/gaussian_blur.glsl");

                // Vertical blur: read from blurPingPong, write to tempHeightmap
                var blurVOp = new AsyncComputeOperation("res://addons/terrain_3d_tools/Shaders/Masks/gaussian_blur.glsl");
                blurVOp.BindStorageImage(0, tempHeightmap);
                blurVOp.BindSamplerWithTexture(1, blurPingPong);
                blurVOp.SetPushConstants(GpuUtils.CreatePushConstants()
                    .Add(0)         // direction_x (int) - 4 bytes
                    .Add(1)         // direction_y (int) - 4 bytes
                    .Add(1.0f)      // sample_distance (float) - 4 bytes
                    .Add(0.0f)      // padding - 4 bytes = 16 bytes total
                    .Build());
                blurVOp.CreateDispatchCommands(1, 1, 1);

                blurVPipeline = blurVOp.Pipeline;
                var blurVRids = blurVOp.GetTemporaryRids();
                if (blurVRids.Count > 0) blurVUniformSet = blurVRids[blurVRids.Count - 1];
                operationRids.AddRange(blurVRids);
            }

            // E. HYDRAULIC EROSION SETUP (push constants set per-iteration in execution lambda)
            Rid erosionPipeline = new Rid();
            Rid erosionUniformSet = new Rid();

            if (EnableHydraulicErosion)
            {
                var erosionOp = new AsyncComputeOperation("res://addons/terrain_3d_tools/Shaders/Masks/erosion_simulation.glsl");
                erosionOp.BindStorageImage(0, tempHeightmap);
                erosionOp.BindStorageImage(1, depositionMap);
                erosionOp.BindStorageImage(2, dropletFlowMap);
                erosionOp.BindStorageBuffer(3, dropletBuffer);
                // Push constants set per-iteration, but need dummy for uniform set creation
                erosionOp.SetPushConstants(GpuUtils.CreatePushConstants()
                    .Add(Inertia).Add(ErosionStrength).Add(DepositionStrength).Add(EvaporationRate)  // 16 bytes
                    .Add(MaxLifetime).Add(maskWidth).Add(maskHeight).Add(RandomSeed)                  // 16 bytes
                    .Add(Gravity).Add(HeightScale).Add(MaxErosionDepth).Add(ErosionRadius)            // 16 bytes
                    .Build());
                erosionOp.CreateDispatchCommands(1, 1, 1);

                erosionPipeline = erosionOp.Pipeline;
                var erosionRids = erosionOp.GetTemporaryRids();
                if (erosionRids.Count > 0) erosionUniformSet = erosionRids[erosionRids.Count - 1];
                operationRids.AddRange(erosionRids);
                shaderPaths.Add("res://addons/terrain_3d_tools/Shaders/Masks/erosion_simulation.glsl");
            }

            // F. THERMAL EROSION SETUP
            Rid thermalPipeline = new Rid();
            Rid thermalUniformSet = new Rid();

            if (EnableThermalWeathering)
            {
                var thermalOp = new AsyncComputeOperation("res://addons/terrain_3d_tools/Shaders/Masks/thermal_erosion.glsl");
                thermalOp.BindStorageImage(0, tempHeightmap);
                // 16-byte aligned push constants
                thermalOp.SetPushConstants(GpuUtils.CreatePushConstants()
                    .Add(TalusAngle)        // float - 4 bytes
                    .Add(ThermalStrength)   // float - 4 bytes
                    .Add(maskWidth)         // int - 4 bytes
                    .Add(maskHeight)        // int - 4 bytes = 16 bytes total
                    .Build());
                thermalOp.CreateDispatchCommands(1, 1, 1);

                thermalPipeline = thermalOp.Pipeline;
                var thermalRids = thermalOp.GetTemporaryRids();
                if (thermalRids.Count > 0) thermalUniformSet = thermalRids[thermalRids.Count - 1];
                operationRids.AddRange(thermalRids);
                shaderPaths.Add("res://addons/terrain_3d_tools/Shaders/Masks/thermal_erosion.glsl");
            }

            // G. BLEND SETUP (16-byte aligned)
            var blendOp = new AsyncComputeOperation("res://addons/terrain_3d_tools/Shaders/Masks/erosion_blend.glsl");
            blendOp.BindStorageImage(0, targetMaskTexture);

            Rid blendSourceMap = OutputMode == ErosionMaskOutput.DepositionMask ? depositionMap :
                                (OutputMode == ErosionMaskOutput.FlowMask ? dropletFlowMap : tempHeightmap);
            blendOp.BindSamplerWithTexture(1, blendSourceMap);

            blendOp.SetPushConstants(GpuUtils.CreatePushConstants()
                .Add((int)BlendType)        // int - 4 bytes
                .Add(LayerMix)              // float - 4 bytes
                .Add(Invert ? 1 : 0)        // int - 4 bytes
                .Add((int)OutputMode)       // int - 4 bytes = 16 bytes
                .Add(RemapMin)              // float - 4 bytes
                .Add(RemapMax)              // float - 4 bytes
                .AddPadding(8)              // 8 bytes padding = 16 bytes
                .Build());

            Action<long> blendCmd = blendOp.CreateDispatchCommands(groupsX, groupsY);
            operationRids.AddRange(blendOp.GetTemporaryRids());
            shaderPaths.Add("res://addons/terrain_3d_tools/Shaders/Masks/erosion_blend.glsl");

            DebugManager.Instance?.EndTimer(DEBUG_CLASS_NAME, DebugCategory.PerformanceTiming, "ErosionMask.Prepare");

            // --- Capture loop parameters ---
            int capturedIterations = Iterations;
            int capturedDropletCount = DropletCount;
            int capturedRandomSeed = RandomSeed;
            int capturedMaxLifetime = MaxLifetime;
            int capturedErosionRadius = ErosionRadius;
            float capturedInertia = Inertia;
            float capturedErosionStrength = ErosionStrength;
            float capturedDepositionStrength = DepositionStrength;
            float capturedEvaporationRate = EvaporationRate;
            float capturedMaxErosionDepth = MaxErosionDepth;
            float capturedGravity = Gravity;
            float capturedHeightScale = HeightScale;
            float capturedTalusAngle = TalusAngle;
            float capturedThermalStrength = ThermalStrength;
            int capturedThermalIterations = ThermalIterations;
            int capturedFlowIterations = FlowIterations;
            int capturedFlowBlurPasses = FlowBlurPasses;
            int capturedFinalBlurPasses = FinalBlurPasses;
            bool capturedEnableGlobalCarving = EnableGlobalCarving;
            bool capturedEnableFlowBlur = EnableFlowBlur;
            bool capturedEnableHydraulicErosion = EnableHydraulicErosion;
            bool capturedEnableThermalWeathering = EnableThermalWeathering;
            bool capturedEnableBlur = EnableBlur;

            // --- 3. Execution Lambda ---
            Action<long> combinedCommands = (computeList) =>
            {
                // 1. Copy Source Height to Temp Buffer
                copyCmd?.Invoke(computeList);
                Gpu.Rd.ComputeListAddBarrier(computeList);

                // 2. Global Carving Loop
                if (capturedEnableGlobalCarving && flowPipeline.IsValid && flowUniformSet.IsValid)
                {
                    clearFlowAccumCmd?.Invoke(computeList);
                    Gpu.Rd.ComputeListAddBarrier(computeList);

                    for (int i = 0; i < capturedFlowIterations; i++)
                    {
                        // NO push constants for flow_accumulation
                        Gpu.AddDispatchToComputeList(computeList, flowPipeline, flowUniformSet, null, groupsX, groupsY, 1);
                        Gpu.Rd.ComputeListAddBarrier(computeList);
                    }

                    carveCmd?.Invoke(computeList);
                    Gpu.Rd.ComputeListAddBarrier(computeList);

                    // Flow blur
                    if (capturedEnableFlowBlur && blurHPipeline.IsValid && blurVPipeline.IsValid)
                    {
                        // Pre-build push constants (16-byte aligned)
                        byte[] hPushConstants = GpuUtils.CreatePushConstants()
                            .Add(1).Add(0).Add(1.0f).Add(0.0f)
                            .Build();
                        byte[] vPushConstants = GpuUtils.CreatePushConstants()
                            .Add(0).Add(1).Add(1.0f).Add(0.0f)
                            .Build();

                        for (int p = 0; p < capturedFlowBlurPasses; p++)
                        {
                            Gpu.AddDispatchToComputeList(computeList, blurHPipeline, blurHUniformSet, hPushConstants, groupsX, groupsY, 1);
                            Gpu.Rd.ComputeListAddBarrier(computeList);
                            Gpu.AddDispatchToComputeList(computeList, blurVPipeline, blurVUniformSet, vPushConstants, groupsX, groupsY, 1);
                            Gpu.Rd.ComputeListAddBarrier(computeList);
                        }
                    }
                }

                // 3. Hydraulic Erosion Loop
                if (capturedEnableHydraulicErosion && erosionPipeline.IsValid && erosionUniformSet.IsValid)
                {
                    clearDepositionCmd?.Invoke(computeList);
                    clearFlowCmd?.Invoke(computeList);
                    Gpu.Rd.ComputeListAddBarrier(computeList);

                    uint dropletGroups = (uint)Math.Ceiling(capturedDropletCount / 64.0);

                    for (int i = 0; i < capturedIterations; i++)
                    {
                        // 48 bytes total (3 x 16 bytes)
                        var pc = GpuUtils.CreatePushConstants()
                            .Add(capturedInertia)           // float - 4
                            .Add(capturedErosionStrength)   // float - 4
                            .Add(capturedDepositionStrength)// float - 4
                            .Add(capturedEvaporationRate)   // float - 4 = 16 bytes
                            .Add(capturedMaxLifetime)       // int - 4
                            .Add(maskWidth)                 // int - 4
                            .Add(maskHeight)                // int - 4
                            .Add(i + capturedRandomSeed)    // int - 4 = 16 bytes
                            .Add(capturedGravity)           // float - 4
                            .Add(capturedHeightScale)       // float - 4
                            .Add(capturedMaxErosionDepth)   // float - 4
                            .Add(capturedErosionRadius)     // int - 4 = 16 bytes
                            .Build();

                        Gpu.AddDispatchToComputeList(computeList, erosionPipeline, erosionUniformSet, pc, dropletGroups, 1, 1);
                        Gpu.Rd.ComputeListAddBarrier(computeList);
                    }
                }

                // 4. Thermal Weathering Loop
                if (capturedEnableThermalWeathering && thermalPipeline.IsValid && thermalUniformSet.IsValid)
                {
                    // 16 bytes total
                    var pc = GpuUtils.CreatePushConstants()
                        .Add(capturedTalusAngle)        // float - 4
                        .Add(capturedThermalStrength)   // float - 4
                        .Add(maskWidth)                 // int - 4
                        .Add(maskHeight)                // int - 4 = 16 bytes
                        .Build();

                    for (int i = 0; i < capturedThermalIterations; i++)
                    {
                        Gpu.AddDispatchToComputeList(computeList, thermalPipeline, thermalUniformSet, pc, groupsX, groupsY, 1);
                        Gpu.Rd.ComputeListAddBarrier(computeList);
                    }
                }

                // 5. Final Blur
                if (capturedEnableBlur && blurHPipeline.IsValid && blurVPipeline.IsValid)
                {
                    byte[] hPushConstants = GpuUtils.CreatePushConstants()
                        .Add(1).Add(0).Add(1.0f).Add(0.0f)
                        .Build();
                    byte[] vPushConstants = GpuUtils.CreatePushConstants()
                        .Add(0).Add(1).Add(1.0f).Add(0.0f)
                        .Build();

                    for (int p = 0; p < capturedFinalBlurPasses; p++)
                    {
                        Gpu.AddDispatchToComputeList(computeList, blurHPipeline, blurHUniformSet, hPushConstants, groupsX, groupsY, 1);
                        Gpu.Rd.ComputeListAddBarrier(computeList);
                        Gpu.AddDispatchToComputeList(computeList, blurVPipeline, blurVUniformSet, vPushConstants, groupsX, groupsY, 1);
                        Gpu.Rd.ComputeListAddBarrier(computeList);
                    }
                }

                // 6. Final Blend
                blendCmd?.Invoke(computeList);
            };

            // Build final cleanup list
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
                .Add(0.0f)              // 4 bytes padding
                .Build();
        }
    }
}