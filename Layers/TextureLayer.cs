// /Layers/TextureLayer.cs
using Godot;
using System;
using System.Collections.Generic;
using Terrain3DTools.Core;
using Terrain3DTools.Utils;

namespace Terrain3DTools.Layers
{
    /// <summary>
    /// Defines how the texture layer blends with existing control map data (single-texture mode).
    /// </summary>
    public enum TextureBlendMode
    {
        /// <summary>Replaces existing textures based on mask strength.</summary>
        Replace = 0,
        /// <summary>Only strengthens textures already present.</summary>
        Strengthen = 1,
        /// <summary>Takes the maximum of current and new blend values.</summary>
        Max = 2,
        /// <summary>Adds to existing blend values (clamped).</summary>
        Additive = 3
    }

    /// <summary>
    /// Noise algorithm for texture variation.
    /// </summary>
    public enum NoiseType
    {
        Value = 0,
        Perlin = 1,
        Simplex = 2
    }

    /// <summary>
    /// Texture layer that applies textures to the terrain control map.
    /// 
    /// Supports two modes:
    /// - Single-texture mode: Applies one texture with blend mode options
    /// - Gradient mode: Zone-based blending of up to 4 textures (Original → Tertiary → Secondary → Primary)
    /// 
    /// Zone-based system ensures adjacent pixels never have flip-flopped texture pairs,
    /// eliminating striping artifacts caused by base/overlay swaps.
    /// </summary>
    [GlobalClass, Tool]
    public partial class TextureLayer : TerrainLayerBase
    {
        #region Private Fields
        
        // Texture selection
        private int _textureIndex = 0;
        private Godot.Collections.Array<int> _excludedTextureIds = new();

        // Blend settings (single-texture mode)
        private TextureBlendMode _blendMode = TextureBlendMode.Replace;
        private float _blendStrength = 1.0f;

        // Gradient mode
        private bool _gradientModeEnabled = false;
        private int _secondaryTextureIndex = -1;
        private int _tertiaryTextureIndex = -1;

        // Zone thresholds (where each texture starts dominating)
        // Ordered: 0.0 → Tertiary threshold → Secondary threshold → Primary threshold → 1.0
        private float _tertiaryThreshold = 0.20f;   // Original → Tertiary transition
        private float _secondaryThreshold = 0.45f;  // Tertiary → Secondary transition
        private float _primaryThreshold = 0.70f;    // Secondary → Primary transition

        // Transition widths (how gradual each zone transition is)
        private float _tertiaryTransition = 0.5f;
        private float _secondaryTransition = 0.5f;
        private float _primaryTransition = 0.5f;

        // Noise settings
        private bool _enableNoise = true;
        private float _noiseAmount = 0.15f;
        private float _noiseScale = 0.02f;
        private int _noiseSeed = 12345;
        private NoiseType _noiseType = NoiseType.Simplex;
        private bool _edgeAwareNoise = true;
        private float _edgeNoiseFalloff = 0.5f;

        // Smoothing settings (simplified for zone-based system)
        private bool _enableSmoothing = true;
        private float _blendSmoothing = 0.3f;
        private float _boundarySmoothing = 0.5f;
        private float _falloffEdgeSmoothing = 0.4f;
        private int _smoothingWindowSize = 1;

        // Cached RID for noise texture (optional)
        private Texture2D _noiseTexture;
        private Rid _noiseTextureRid;
        
        #endregion

        #region Falloff Override
        
        public override FalloffApplication FalloffApplyMode => FalloffApplication.ApplyToResult;
        
        #endregion

        #region Texture Selection Properties
        
        [ExportGroup("Texture Selection")]

        [Export(PropertyHint.Range, "0,31")]
        public int TextureIndex
        {
            get => _textureIndex;
            set => SetApplyProperty(ref _textureIndex, Mathf.Clamp(value, 0, 31));
        }

        [Export]
        public Godot.Collections.Array<int> ExcludedTextureIds
        {
            get => _excludedTextureIds;
            set
            {
                _excludedTextureIds = value ?? new Godot.Collections.Array<int>();
                ForcePositionDirty();
            }
        }
        
        #endregion

        #region Blend Settings Properties
        
        [ExportGroup("Blend Settings")]

        [Export]
        public TextureBlendMode BlendMode
        {
            get => _blendMode;
            set => SetApplyProperty(ref _blendMode, value);
        }

        [Export(PropertyHint.Range, "0,1,0.01")]
        public float BlendStrength
        {
            get => _blendStrength;
            set => SetApplyProperty(ref _blendStrength, Mathf.Clamp(value, 0f, 1f));
        }
        
        #endregion

        #region Gradient Mode Properties
        
        [ExportGroup("Gradient Mode")]

        [Export]
        public bool GradientModeEnabled
        {
            get => _gradientModeEnabled;
            set => SetApplyProperty(ref _gradientModeEnabled, value);
        }

        [Export(PropertyHint.Range, "-1,31")]
        public int SecondaryTextureIndex
        {
            get => _secondaryTextureIndex;
            set => SetApplyProperty(ref _secondaryTextureIndex, Mathf.Clamp(value, -1, 31));
        }

        [Export(PropertyHint.Range, "-1,31")]
        public int TertiaryTextureIndex
        {
            get => _tertiaryTextureIndex;
            set => SetApplyProperty(ref _tertiaryTextureIndex, Mathf.Clamp(value, -1, 31));
        }

        [ExportSubgroup("Zone Thresholds")]

        [Export(PropertyHint.Range, "0.01,0.98,0.01")]
        public float TertiaryThreshold
        {
            get => _tertiaryThreshold;
            set => SetApplyProperty(ref _tertiaryThreshold, Mathf.Clamp(value, 0.01f, 0.98f));
        }

        [Export(PropertyHint.Range, "0.02,0.99,0.01")]
        public float SecondaryThreshold
        {
            get => _secondaryThreshold;
            set => SetApplyProperty(ref _secondaryThreshold, Mathf.Clamp(value, 0.02f, 0.99f));
        }

        [Export(PropertyHint.Range, "0.03,1.0,0.01")]
        public float PrimaryThreshold
        {
            get => _primaryThreshold;
            set => SetApplyProperty(ref _primaryThreshold, Mathf.Clamp(value, 0.03f, 1.0f));
        }

        [ExportSubgroup("Transition Widths")]

        [Export(PropertyHint.Range, "0.1,2.0,0.05")]
        public float TertiaryTransition
        {
            get => _tertiaryTransition;
            set => SetApplyProperty(ref _tertiaryTransition, Mathf.Clamp(value, 0.1f, 2.0f));
        }

        [Export(PropertyHint.Range, "0.1,2.0,0.05")]
        public float SecondaryTransition
        {
            get => _secondaryTransition;
            set => SetApplyProperty(ref _secondaryTransition, Mathf.Clamp(value, 0.1f, 2.0f));
        }

        [Export(PropertyHint.Range, "0.1,2.0,0.05")]
        public float PrimaryTransition
        {
            get => _primaryTransition;
            set => SetApplyProperty(ref _primaryTransition, Mathf.Clamp(value, 0.1f, 2.0f));
        }
        
        #endregion

        #region Noise Properties
        
        [ExportGroup("Noise & Variation")]

        [Export]
        public bool EnableNoise
        {
            get => _enableNoise;
            set => SetApplyProperty(ref _enableNoise, value);
        }

        [Export(PropertyHint.Range, "0,0.5,0.01")]
        public float NoiseAmount
        {
            get => _noiseAmount;
            set => SetApplyProperty(ref _noiseAmount, Mathf.Clamp(value, 0f, 0.5f));
        }

        [Export(PropertyHint.Range, "0.001,0.2,0.001")]
        public float NoiseScale
        {
            get => _noiseScale;
            set => SetApplyProperty(ref _noiseScale, Mathf.Clamp(value, 0.001f, 0.2f));
        }

        [Export]
        public int NoiseSeed
        {
            get => _noiseSeed;
            set => SetApplyProperty(ref _noiseSeed, value);
        }

        [Export]
        public NoiseType NoiseType
        {
            get => _noiseType;
            set => SetApplyProperty(ref _noiseType, value);
        }

        [Export]
        public Texture2D NoiseTexture
        {
            get => _noiseTexture;
            set
            {
                _noiseTexture = value;
                UpdateNoiseTextureRid();
                ForcePositionDirty();
            }
        }

        [ExportSubgroup("Edge-Aware Noise")]

        [Export]
        public bool EdgeAwareNoise
        {
            get => _edgeAwareNoise;
            set => SetApplyProperty(ref _edgeAwareNoise, value);
        }

        [Export(PropertyHint.Range, "0,1,0.01")]
        public float EdgeNoiseFalloff
        {
            get => _edgeNoiseFalloff;
            set => SetApplyProperty(ref _edgeNoiseFalloff, Mathf.Clamp(value, 0f, 1f));
        }
        
        #endregion

        #region Smoothing Properties
        
        [ExportGroup("Blend Smoothing")]

        [Export]
        public bool EnableSmoothing
        {
            get => _enableSmoothing;
            set => SetApplyProperty(ref _enableSmoothing, value);
        }

        [Export(PropertyHint.Range, "0,1,0.05")]
        public float BlendSmoothing
        {
            get => _blendSmoothing;
            set => SetApplyProperty(ref _blendSmoothing, Mathf.Clamp(value, 0f, 1f));
        }

        [Export(PropertyHint.Range, "0,1,0.05")]
        public float BoundarySmoothing
        {
            get => _boundarySmoothing;
            set => SetApplyProperty(ref _boundarySmoothing, Mathf.Clamp(value, 0f, 1f));
        }

        [Export(PropertyHint.Range, "0,1,0.05")]
        public float FalloffEdgeSmoothing
        {
            get => _falloffEdgeSmoothing;
            set => SetApplyProperty(ref _falloffEdgeSmoothing, Mathf.Clamp(value, 0f, 1f));
        }

        [Export(PropertyHint.Range, "1,2,1")]
        public int SmoothingWindowSize
        {
            get => _smoothingWindowSize;
            set => SetApplyProperty(ref _smoothingWindowSize, Mathf.Clamp(value, 1, 2));
        }
        
        #endregion

        #region Layer Info
        
        public override LayerType GetLayerType() => LayerType.Texture;
        public override string LayerTypeName() => "Texture Layer";
        
        #endregion

        #region Lifecycle
        
        public override void _Ready()
        {
            base._Ready();
            ModifiesTexture = true;

            if (string.IsNullOrEmpty(LayerName) || LayerName.StartsWith("New Layer"))
                LayerName = "Texture Layer " + IdGenerator.GenerateShortUid();
        }

        public override void _Notification(int what)
        {
            base._Notification(what);

            if (what == (int)NotificationPredelete)
            {
                _noiseTextureRid = new Rid();
            }
        }

        public override void MarkPositionDirty()
        {
            if (DoesAnyMaskRequireHeightData())
            {
                ForceDirty();
            }
            base.MarkPositionDirty();
        }
        
        #endregion

        #region Texture RID Management
        
        private void UpdateNoiseTextureRid()
        {
            if (_noiseTexture != null && _noiseTexture.GetRid().IsValid)
            {
                _noiseTextureRid = _noiseTexture.GetRid();
            }
            else
            {
                _noiseTextureRid = new Rid();
            }
        }
        
        #endregion

        #region GPU Commands
        
        public override (Action<long> commands, List<Rid> tempRids, List<string>) CreateApplyRegionCommands(
            Vector2I regionCoords,
            RegionData regionData,
            int regionSize,
            Vector2 regionMinWorld,
            Vector2 regionSizeWorld)
        {
            if (!layerTextureRID.IsValid || !regionData.ControlMap.IsValid)
            {
                GD.PrintErr($"[TextureLayer] CreateApplyRegionCommands called on '{LayerName}' but a required texture is invalid.");
                return (null, new List<Rid>(), new List<string>());
            }

            Vector2 maskCenter = new Vector2(GlobalPosition.X, GlobalPosition.Z);
            OverlapResult? overlap = RegionMaskOverlap.GetRegionMaskOverlap(regionCoords, regionSize, maskCenter, Size);
            if (!overlap.HasValue)
            {
                return (null, new List<Rid>(), new List<string>());
            }
            var o = overlap.Value;

            var tempRids = new List<Rid>();
            var shaderPaths = new List<string>();
            var allCommands = new List<Action<long>>();

            // Create metadata texture for two-pass system
            Rid metadataTexture = new Rid();
            if (_enableSmoothing)
            {
                metadataTexture = Gpu.CreateTexture2D(
                    (uint)regionSize, (uint)regionSize,
                    RenderingDevice.DataFormat.R32Uint,
                    RenderingDevice.TextureUsageBits.StorageBit |
                    RenderingDevice.TextureUsageBits.SamplingBit);
                tempRids.Add(metadataTexture);
            }

            // === PASS 1: Zone-Based Texture Placement ===
            var (pass1Commands, pass1Rids, pass1Shaders) = CreatePass1Commands(
                regionCoords, regionData, regionSize, o, metadataTexture);

            if (pass1Commands != null)
            {
                allCommands.Add(pass1Commands);
                tempRids.AddRange(pass1Rids);
                shaderPaths.AddRange(pass1Shaders);
            }

            // === PASS 2: Blend Smoothing ===
            if (_enableSmoothing && metadataTexture.IsValid)
            {
                var (pass2Commands, pass2Rids, pass2Shaders) = CreatePass2Commands(
                    regionData, regionSize, metadataTexture);

                if (pass2Commands != null)
                {
                    allCommands.Add(pass2Commands);
                    tempRids.AddRange(pass2Rids);
                    shaderPaths.AddRange(pass2Shaders);
                }
            }

            if (allCommands.Count == 0)
            {
                return (null, tempRids, shaderPaths);
            }

            Action<long> combinedCommands = (computeList) =>
            {
                for (int i = 0; i < allCommands.Count; i++)
                {
                    allCommands[i]?.Invoke(computeList);
                    if (i < allCommands.Count - 1)
                    {
                        Gpu.Rd.ComputeListAddBarrier(computeList);
                    }
                }
            };

            return (combinedCommands, tempRids, shaderPaths);
        }

        private (Action<long> commands, List<Rid> tempRids, List<string> shaderPaths) CreatePass1Commands(
            Vector2I regionCoords,
            RegionData regionData,
            int regionSize,
            OverlapResult overlap,
            Rid metadataTexture)
        {
            var shaderPath = "res://addons/terrain_3d_tools/Shaders/Layers/TextureLayerPass1.glsl";
            var operation = new AsyncComputeOperation(shaderPath);
            var tempRids = new List<Rid>();

            // Binding 0: Control map (read/write)
            operation.BindStorageImage(0, regionData.ControlMap);

            // Binding 1: Metadata map (write in pass 1)
            if (metadataTexture.IsValid)
            {
                operation.BindStorageImage(1, metadataTexture);
            }
            else
            {
                operation.BindStorageImage(1, regionData.ControlMap);
            }

            // Binding 2: Layer mask sampler
            operation.BindSamplerWithTexture(2, layerTextureRID);

            // Binding 3: Exclusion list buffer
            Rid exclusionBufferRid = CreateExclusionBuffer();
            if (exclusionBufferRid.IsValid)
            {
                operation.BindStorageBuffer(3, exclusionBufferRid);
            }

            // Binding 4: Noise texture (optional)
            bool hasNoiseTexture = _noiseTextureRid.IsValid;
            if (hasNoiseTexture)
            {
                operation.BindSamplerWithTexture(4, _noiseTextureRid,
                    RenderingDevice.SamplerFilter.Linear,
                    RenderingDevice.SamplerRepeatMode.Repeat);
            }
            else
            {
                operation.BindSamplerWithTexture(4, layerTextureRID);
            }

            // Binding 5: Layer settings uniform buffer
            Rid settingsBufferRid = CreatePass1SettingsBuffer(hasNoiseTexture, metadataTexture.IsValid);
            operation.BindUniformBuffer(5, settingsBufferRid);

            // Binding 6: Falloff curve buffer
            Rid falloffCurveBufferRid = CreateFalloffCurveBuffer();
            operation.BindStorageBuffer(6, falloffCurveBufferRid);

            // Calculate world-space position info
            Vector2 maskCenter = new Vector2(GlobalPosition.X, GlobalPosition.Z);
            Vector2 layerWorldMin = maskCenter - new Vector2(Size.X * 0.5f, Size.Y * 0.5f);
            Vector2 layerWorldSize = new Vector2(Size.X, Size.Y);

            // Build push constants
            var pcb = GpuUtils.CreatePushConstants()
                .Add(overlap.RegionMin.X).Add(overlap.RegionMin.Y)
                .Add(overlap.MaskMin.X).Add(overlap.MaskMin.Y)
                .Add(Size.X).Add(Size.Y)
                .Add((uint)TextureIndex)
                .Add((uint)_excludedTextureIds.Count)
                .Add(layerWorldMin.X).Add(layerWorldMin.Y)
                .Add(layerWorldSize.X).Add(layerWorldSize.Y)
                .Add((uint)FalloffMode).Add(FalloffStrength)
                .Add((uint)regionSize).Add(0u)
                .Build();

            operation.SetPushConstants(pcb);

            uint groupsX = (uint)((regionSize + 7) / 8);
            uint groupsY = (uint)((regionSize + 7) / 8);

            tempRids.AddRange(operation.GetTemporaryRids());
            if (exclusionBufferRid.IsValid) tempRids.Add(exclusionBufferRid);
            if (settingsBufferRid.IsValid) tempRids.Add(settingsBufferRid);
            if (falloffCurveBufferRid.IsValid) tempRids.Add(falloffCurveBufferRid);

            return (operation.CreateDispatchCommands(groupsX, groupsY), tempRids, new List<string> { shaderPath });
        }

        private (Action<long> commands, List<Rid> tempRids, List<string> shaderPaths) CreatePass2Commands(
            RegionData regionData,
            int regionSize,
            Rid metadataTexture)
        {
            var shaderPath = "res://addons/terrain_3d_tools/Shaders/Layers/TextureLayerPass2.glsl";
            var operation = new AsyncComputeOperation(shaderPath);
            var tempRids = new List<Rid>();

            // Binding 0: Control map (read/write)
            operation.BindStorageImage(0, regionData.ControlMap);

            // Binding 1: Metadata map (read-only in pass 2)
            operation.BindStorageImage(1, metadataTexture);

            // Push constants for smoothing parameters
            var pcb = GpuUtils.CreatePushConstants()
                .Add(_blendSmoothing)
                .Add(_boundarySmoothing)
                .Add(_falloffEdgeSmoothing)
                .Add(0f)  // Reserved
                .Add(_smoothingWindowSize)
                .Add(regionSize)
                .Add(0u).Add(0u)  // Padding
                .Build();

            operation.SetPushConstants(pcb);

            uint groupsX = (uint)((regionSize + 7) / 8);
            uint groupsY = (uint)((regionSize + 7) / 8);

            tempRids.AddRange(operation.GetTemporaryRids());

            return (operation.CreateDispatchCommands(groupsX, groupsY), tempRids, new List<string> { shaderPath });
        }

        private Rid CreateExclusionBuffer()
        {
            int count = Math.Max(1, _excludedTextureIds.Count);
            var data = new uint[count];

            for (int i = 0; i < _excludedTextureIds.Count; i++)
            {
                data[i] = (uint)Mathf.Clamp(_excludedTextureIds[i], 0, 31);
            }

            byte[] bytes = new byte[count * sizeof(uint)];
            Buffer.BlockCopy(data, 0, bytes, 0, bytes.Length);

            return Gpu.Rd.StorageBufferCreate((uint)bytes.Length, bytes);
        }

        private Rid CreatePass1SettingsBuffer(bool hasNoiseTexture, bool outputMetadata)
        {
            // std140 layout: 112 bytes (7 x 16-byte aligned blocks)
            var data = new List<byte>();

            // Block 1: Blend settings (16 bytes)
            data.AddRange(BitConverter.GetBytes((uint)BlendMode));
            data.AddRange(BitConverter.GetBytes(BlendStrength));
            data.AddRange(BitConverter.GetBytes(GradientModeEnabled ? 1u : 0u));
            data.AddRange(BitConverter.GetBytes(outputMetadata ? 1u : 0u));

            // Block 2: Texture IDs (16 bytes)
            data.AddRange(BitConverter.GetBytes(SecondaryTextureIndex < 0 ? 0xFFFFFFFFu : (uint)SecondaryTextureIndex));
            data.AddRange(BitConverter.GetBytes(TertiaryTextureIndex < 0 ? 0xFFFFFFFFu : (uint)TertiaryTextureIndex));
            data.AddRange(BitConverter.GetBytes(0u)); // original_base_id - captured from control map
            data.AddRange(BitConverter.GetBytes(0u)); // _pad0

            // Block 3: Zone thresholds (16 bytes)
            data.AddRange(BitConverter.GetBytes(TertiaryThreshold));
            data.AddRange(BitConverter.GetBytes(SecondaryThreshold));
            data.AddRange(BitConverter.GetBytes(PrimaryThreshold));
            data.AddRange(BitConverter.GetBytes(0u)); // _pad1

            // Block 4: Transition widths (16 bytes)
            data.AddRange(BitConverter.GetBytes(TertiaryTransition));
            data.AddRange(BitConverter.GetBytes(SecondaryTransition));
            data.AddRange(BitConverter.GetBytes(PrimaryTransition));
            data.AddRange(BitConverter.GetBytes(0u)); // _pad2

            // Block 5: Reserved (16 bytes)
            data.AddRange(BitConverter.GetBytes(0f));
            data.AddRange(BitConverter.GetBytes(0f));
            data.AddRange(BitConverter.GetBytes(0f));
            data.AddRange(BitConverter.GetBytes(0u)); // _pad3

            // Block 6: Noise settings (16 bytes)
            data.AddRange(BitConverter.GetBytes(EnableNoise ? 1u : 0u));
            data.AddRange(BitConverter.GetBytes(NoiseAmount));
            data.AddRange(BitConverter.GetBytes(NoiseScale));
            data.AddRange(BitConverter.GetBytes((uint)NoiseSeed));

            // Block 7: Noise settings continued (16 bytes)
            data.AddRange(BitConverter.GetBytes((uint)NoiseType));
            data.AddRange(BitConverter.GetBytes(hasNoiseTexture ? 1u : 0u));
            data.AddRange(BitConverter.GetBytes(EdgeAwareNoise ? 1u : 0u));
            data.AddRange(BitConverter.GetBytes(EdgeNoiseFalloff));

            return Gpu.Rd.UniformBufferCreate((uint)data.Count, data.ToArray());
        }
        
        #endregion

        #region Utility Methods
        
        /// <summary>
        /// Returns true if gradient mode is enabled and has at least one additional texture configured.
        /// </summary>
        public bool HasGradientTextures()
        {
            return GradientModeEnabled && (SecondaryTextureIndex >= 0 || TertiaryTextureIndex >= 0);
        }

        /// <summary>
        /// Gets a list of all texture IDs used by this layer.
        /// </summary>
        public List<int> GetUsedTextureIds()
        {
            var ids = new List<int> { TextureIndex };
            
            if (GradientModeEnabled)
            {
                if (SecondaryTextureIndex >= 0) ids.Add(SecondaryTextureIndex);
                if (TertiaryTextureIndex >= 0) ids.Add(TertiaryTextureIndex);
            }
            
            return ids;
        }

        /// <summary>
        /// Randomizes the noise seed.
        /// </summary>
        public void RandomizeNoiseSeed()
        {
            NoiseSeed = (int)(GD.Randi() % 100000);
        }

        /// <summary>
        /// Gets the sorted zone thresholds for the influence preview.
        /// Returns thresholds in ascending order based on which textures are enabled.
        /// </summary>
        public List<(float threshold, string label)> GetZoneThresholds()
        {
            var thresholds = new List<(float threshold, string label)>();
            
            if (TertiaryTextureIndex >= 0)
            {
                thresholds.Add((TertiaryThreshold, "Tertiary"));
            }
            
            if (SecondaryTextureIndex >= 0)
            {
                thresholds.Add((SecondaryThreshold, "Secondary"));
            }
            
            thresholds.Add((PrimaryThreshold, "Primary"));
            
            return thresholds;
        }

        /// <summary>
        /// Calculates the zone index and blend value for a given mask value.
        /// Used by the UI to show accurate preview.
        /// </summary>
        public (int zoneIndex, uint baseTex, uint overlayTex, float blendT) CalculateZoneForMaskValue(float maskValue, uint originalBase)
        {
            bool hasSecondary = SecondaryTextureIndex >= 0;
            bool hasTertiary = TertiaryTextureIndex >= 0;
            
            uint primaryTex = (uint)TextureIndex;
            uint secondaryTex = hasSecondary ? (uint)SecondaryTextureIndex : 0xFFFFFFFF;
            uint tertiaryTex = hasTertiary ? (uint)TertiaryTextureIndex : 0xFFFFFFFF;
            
            float tTert = TertiaryThreshold;
            float tSec = SecondaryThreshold;
            float tPrim = PrimaryThreshold;
            
            int zone;
            uint baseTex, overlayTex;
            float zoneT;
            
            if (hasTertiary && hasSecondary)
            {
                // Full 4-texture gradient
                if (maskValue < tTert)
                {
                    zone = 0;
                    baseTex = originalBase;
                    overlayTex = tertiaryTex;
                    zoneT = maskValue / Mathf.Max(tTert, 0.001f);
                }
                else if (maskValue < tSec)
                {
                    zone = 1;
                    baseTex = tertiaryTex;
                    overlayTex = secondaryTex;
                    zoneT = (maskValue - tTert) / Mathf.Max(tSec - tTert, 0.001f);
                }
                else if (maskValue < tPrim)
                {
                    zone = 2;
                    baseTex = secondaryTex;
                    overlayTex = primaryTex;
                    zoneT = (maskValue - tSec) / Mathf.Max(tPrim - tSec, 0.001f);
                }
                else
                {
                    zone = 3;
                    baseTex = primaryTex;
                    overlayTex = primaryTex;
                    zoneT = 1.0f;
                }
            }
            else if (hasTertiary && !hasSecondary)
            {
                // 3-texture: Original → Tertiary → Primary
                if (maskValue < tTert)
                {
                    zone = 0;
                    baseTex = originalBase;
                    overlayTex = tertiaryTex;
                    zoneT = maskValue / Mathf.Max(tTert, 0.001f);
                }
                else if (maskValue < tPrim)
                {
                    zone = 1;
                    baseTex = tertiaryTex;
                    overlayTex = primaryTex;
                    zoneT = (maskValue - tTert) / Mathf.Max(tPrim - tTert, 0.001f);
                }
                else
                {
                    zone = 2;
                    baseTex = primaryTex;
                    overlayTex = primaryTex;
                    zoneT = 1.0f;
                }
            }
            else if (!hasTertiary && hasSecondary)
            {
                // 3-texture: Original → Secondary → Primary
                if (maskValue < tSec)
                {
                    zone = 0;
                    baseTex = originalBase;
                    overlayTex = secondaryTex;
                    zoneT = maskValue / Mathf.Max(tSec, 0.001f);
                }
                else if (maskValue < tPrim)
                {
                    zone = 1;
                    baseTex = secondaryTex;
                    overlayTex = primaryTex;
                    zoneT = (maskValue - tSec) / Mathf.Max(tPrim - tSec, 0.001f);
                }
                else
                {
                    zone = 2;
                    baseTex = primaryTex;
                    overlayTex = primaryTex;
                    zoneT = 1.0f;
                }
            }
            else
            {
                // 2-texture: Original → Primary
                if (maskValue < tPrim)
                {
                    zone = 0;
                    baseTex = originalBase;
                    overlayTex = primaryTex;
                    zoneT = maskValue / Mathf.Max(tPrim, 0.001f);
                }
                else
                {
                    zone = 1;
                    baseTex = primaryTex;
                    overlayTex = primaryTex;
                    zoneT = 1.0f;
                }
            }
            
            return (zone, baseTex, overlayTex, Mathf.Clamp(zoneT, 0f, 1f));
        }

        /// <summary>
        /// Applies the transition curve to a zone_t value.
        /// </summary>
        public float ApplyTransitionCurve(float zoneT, float transitionWidth)
        {
            float halfWidth = transitionWidth * 0.5f;
            float low = 0.5f - halfWidth;
            float high = 0.5f + halfWidth;
            
            // Smoothstep
            float t = Mathf.Clamp((zoneT - low) / (high - low), 0f, 1f);
            return t * t * (3f - 2f * t);
        }

        /// <summary>
        /// Gets the transition width for a given zone index.
        /// </summary>
        public float GetTransitionWidthForZone(int zoneIndex)
        {
            return zoneIndex switch
            {
                0 => TertiaryTransition,
                1 => SecondaryTransition,
                2 => PrimaryTransition,
                _ => 0.5f
            };
        }
        
        #endregion
    }
}