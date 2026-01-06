// /Masks/StampMask.cs
using Godot;
using System;
using System.Collections.Generic;
using Terrain3DTools.Core;
using Terrain3DTools.Core.Debug;
using Terrain3DTools.Utils;

namespace Terrain3DTools.Masks
{
    [GlobalClass, Tool]
    public partial class StampMask : TerrainMask
    {
        private const string DEBUG_CLASS_NAME = "StampMask";

        public enum LdrChannel { Luminance, Red, Green, Blue, Alpha }

        #region Properties and Fields
        private Texture2D _stampTexture;
        private LdrChannel _sourceChannel = LdrChannel.Luminance;
        private bool _flipX = false;
        private bool _flipY = false;
        private Rid _cachedComputeTextureRid;
        private ulong _cachedTextureInstanceId;
        private bool _needsUpload = false;
        #endregion

        [ExportCategory("Stamp Properties")]
        [Export] 
        public Texture2D StampTexture 
        { 
            get => _stampTexture; 
            set 
            {
                if (_stampTexture != value)
                {
                    _stampTexture = value;
                    _needsUpload = true;
                    // Schedule upload for next idle frame (outside compute list)
                    if (value != null && Engine.IsEditorHint())
                    {
                        Callable.From(UploadTextureDeferred).CallDeferred();
                    }
                    EmitChanged();
                }
            }
        }
        
        [Export] 
        public LdrChannel SourceChannel 
        { 
            get => _sourceChannel; 
            set => SetProperty(ref _sourceChannel, value); 
        }
        
        [Export] 
        public bool FlipX 
        { 
            get => _flipX; 
            set => SetProperty(ref _flipX, value); 
        }
        
        [Export] 
        public bool FlipY 
        { 
            get => _flipY; 
            set => SetProperty(ref _flipY, value); 
        }

        public StampMask()
        {
            BlendType = MaskBlendType.Multiply;
            DebugManager.Instance?.RegisterClass(DEBUG_CLASS_NAME);
        }

        public override MaskRequirements MaskDataRequirements() => MaskRequirements.None;

        public override (Action<long> commands, List<Rid> tempRids, List<string>) CreateApplyCommands(
            Rid targetMaskTexture, int maskWidth, int maskHeight, Rid stitchedHeightmap = new Rid())
        {
            if (StampTexture == null) 
            { 
                DebugManager.Instance?.LogWarning(DEBUG_CLASS_NAME, "No stamp texture assigned");
                return (null, new List<Rid>(), new List<string>()); 
            }

            // Check if texture changed
            ulong currentInstanceId = StampTexture.GetInstanceId();
            if (currentInstanceId != _cachedTextureInstanceId)
            {
                _needsUpload = true;
            }

            // If we still need upload, the deferred call hasn't run yet
            // Return null to skip this frame - the layer will remain dirty
            if (_needsUpload || !_cachedComputeTextureRid.IsValid)
            {
                // Try deferred upload if not already scheduled
                if (StampTexture != null)
                {
                    Callable.From(UploadTextureDeferred).CallDeferred();
                }
                
                DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.MaskGeneration,
                    "Stamp texture not ready yet, waiting for upload");
                return (null, new List<Rid>(), new List<string>());
            }

            return CreateStampShaderCommands(targetMaskTexture, maskWidth, maskHeight);
        }

        /// <summary>
        /// Uploads the stamp texture to GPU. Called via CallDeferred to ensure
        /// it runs outside of any compute list operations.
        /// </summary>
        private void UploadTextureDeferred()
        {
            if (!_needsUpload) return;
            if (StampTexture == null) 
            {
                ClearCachedTexture();
                return;
            }

            // Clear old texture first
            ClearCachedTexture();

            Image sourceImage = StampTexture.GetImage();
            if (sourceImage == null || sourceImage.IsEmpty())
            {
                DebugManager.Instance?.LogWarning(DEBUG_CLASS_NAME, "Stamp texture image is null or empty");
                _needsUpload = false;
                return;
            }

            // Make a copy so we don't modify the original
            sourceImage = (Image)sourceImage.Duplicate();

            // Handle compressed textures - MUST decompress first
            if (sourceImage.IsCompressed())
            {
                var error = sourceImage.Decompress();
                if (error != Error.Ok)
                {
                    DebugManager.Instance?.LogError(DEBUG_CLASS_NAME, 
                        $"Failed to decompress texture: {error}");
                    _needsUpload = false;
                    return;
                }
                
                DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.GpuResources,
                    $"Decompressed texture from compressed format");
            }

            // Get image format and convert to RD format
            var imageFormat = sourceImage.GetFormat();
            RenderingDevice.DataFormat rdFormat = GetRdFormatFromImageFormat(imageFormat);
            
            if (rdFormat == RenderingDevice.DataFormat.Max)
            {
                DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.GpuResources,
                    $"Converting image from {imageFormat} to RGBA8");
                
                // Convert to RGBA8 for compatibility
                sourceImage.Convert(Image.Format.Rgba8);
                rdFormat = RenderingDevice.DataFormat.R8G8B8A8Unorm;
            }

            byte[] textureData = sourceImage.GetData();
            if (textureData == null || textureData.Length == 0)
            {
                DebugManager.Instance?.LogWarning(DEBUG_CLASS_NAME, "Failed to get texture data");
                _needsUpload = false;
                return;
            }

            // Create GPU texture
            _cachedComputeTextureRid = Gpu.CreateTexture2D(
                (uint)sourceImage.GetWidth(), 
                (uint)sourceImage.GetHeight(),
                rdFormat,
                RenderingDevice.TextureUsageBits.SamplingBit | 
                RenderingDevice.TextureUsageBits.CanUpdateBit
            );

            if (!_cachedComputeTextureRid.IsValid)
            {
                DebugManager.Instance?.LogError(DEBUG_CLASS_NAME, "Failed to create GPU texture");
                _needsUpload = false;
                return;
            }

            // Upload data - safe now since we're in deferred call, outside compute list
            Gpu.TextureUpdate(_cachedComputeTextureRid, 0, textureData);

            _cachedTextureInstanceId = StampTexture.GetInstanceId();
            _needsUpload = false;

            DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.GpuResources,
                $"Uploaded stamp texture: {sourceImage.GetWidth()}x{sourceImage.GetHeight()}, format: {rdFormat}");

            // Force the mask to re-emit changed so the layer picks up the new texture
            EmitChanged();
        }

        /// <summary>
        /// Converts Godot Image.Format to RenderingDevice.DataFormat.
        /// </summary>
        private static RenderingDevice.DataFormat GetRdFormatFromImageFormat(Image.Format format)
        {
            return format switch
            {
                // Single channel (greyscale)
                Image.Format.L8 => RenderingDevice.DataFormat.R8Unorm,
                Image.Format.R8 => RenderingDevice.DataFormat.R8Unorm,
                
                // Single channel HDR
                Image.Format.Rf => RenderingDevice.DataFormat.R32Sfloat,
                Image.Format.Rh => RenderingDevice.DataFormat.R16Sfloat,
                
                // Two channel
                Image.Format.La8 => RenderingDevice.DataFormat.R8G8Unorm,
                Image.Format.Rg8 => RenderingDevice.DataFormat.R8G8Unorm,
                Image.Format.Rgf => RenderingDevice.DataFormat.R32G32Sfloat,
                Image.Format.Rgh => RenderingDevice.DataFormat.R16G16Sfloat,
                
                // RGBA
                Image.Format.Rgba8 => RenderingDevice.DataFormat.R8G8B8A8Unorm,
                Image.Format.Rgbaf => RenderingDevice.DataFormat.R32G32B32A32Sfloat,
                Image.Format.Rgbah => RenderingDevice.DataFormat.R16G16B16A16Sfloat,
                
                // Everything else needs conversion
                _ => RenderingDevice.DataFormat.Max
            };
        }

        private (Action<long> commands, List<Rid> tempRids, List<string>) CreateStampShaderCommands(
            Rid targetMaskTexture, int maskWidth, int maskHeight)
        {
            if (!_cachedComputeTextureRid.IsValid)
            {
                return (null, new List<Rid>(), new List<string>());
            }

            var image = StampTexture?.GetImage();
            if (image == null || image.IsEmpty()) 
            {
                return (null, new List<Rid>(), new List<string>());
            }
            
            var format = image.GetFormat();
            
            // Check for HDR formats (use Red channel directly)
            bool isHdr = format == Image.Format.Rf || 
                        format == Image.Format.Rh ||
                        format == Image.Format.Rgf || 
                        format == Image.Format.Rgh ||
                        format == Image.Format.Rgbaf ||
                        format == Image.Format.Rgbah;
            
            // For HDR, always use Red channel; for LDR, use selected channel
            int sampleMode = isHdr ? (int)LdrChannel.Red : (int)SourceChannel;

            var shaderPath = "res://addons/terrain_3d_tools/Shaders/Masks/stamp_mask.glsl";
            var op = new AsyncComputeOperation(shaderPath);
            
            op.BindStorageImage(0, targetMaskTexture);
            op.BindSamplerWithTexture(1, _cachedComputeTextureRid);
            
            // Push constants - 32 bytes (2 x 16 byte alignment)
            var pcb = GpuUtils.CreatePushConstants()
                .Add((int)BlendType)        // int - 4 bytes
                .Add(LayerMix)              // float - 4 bytes
                .Add(Invert ? 1 : 0)        // int - 4 bytes
                .Add(sampleMode)            // int - 4 bytes = 16 bytes
                .Add(FlipX ? 1 : 0)         // int - 4 bytes
                .Add(FlipY ? 1 : 0)         // int - 4 bytes
                .AddPadding(8)              // 8 bytes padding = 16 bytes
                .Build();                   // Total: 32 bytes
            
            op.SetPushConstants(pcb);

            uint groupsX = (uint)((maskWidth + 7) / 8);
            uint groupsY = (uint)((maskHeight + 7) / 8);
            
            return (op.CreateDispatchCommands(groupsX, groupsY), op.GetTemporaryRids(), new List<string> { shaderPath });
        }

        private void ClearCachedTexture()
        {
            if (_cachedComputeTextureRid.IsValid)
            {
                if (AsyncGpuTaskManager.Instance != null)
                {
                    AsyncGpuTaskManager.Instance.QueueCleanup(_cachedComputeTextureRid);
                }
                else
                {
                    Gpu.FreeRid(_cachedComputeTextureRid);
                }
            }
            _cachedComputeTextureRid = new Rid();
            _cachedTextureInstanceId = 0;
        }

        public override void _Notification(int what)
        {
            if (what == NotificationPredelete)
            {
                ClearCachedTexture();
            }
        }
    }
}