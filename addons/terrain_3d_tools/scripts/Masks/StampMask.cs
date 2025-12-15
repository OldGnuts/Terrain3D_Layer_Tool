// /Masks/StampMask.cs (Corrected for Action<long>)
using Godot;
using System;
using System.Collections.Generic;
using Terrain3DTools.Core;
using Terrain3DTools.Utils;

namespace Terrain3DTools.Masks
{
    [GlobalClass, Tool]
    public partial class StampMask : TerrainMask
    {
        public enum LdrChannel { Luminance, Red, Green, Blue, Alpha }

        #region Properties and Fields
        private Texture2D _stampTexture;
        private LdrChannel _sourceChannel = LdrChannel.Luminance;
        private bool _flipX = false;
        private bool _flipY = false;
        private Rid _cachedComputeTextureRid;
        private Rid _cachedSourceTextureRid;
        private static Dictionary<Rid, AsyncGpuTask> _uploadTasks = new();
        #endregion

        [ExportCategory("Stamp Properties")]
        [Export] public Texture2D StampTexture { get => _stampTexture; set => SetProperty(ref _stampTexture, value); }
        [Export] public LdrChannel SourceChannel { get => _sourceChannel; set => SetProperty(ref _sourceChannel, value); }
        [Export] public bool FlipX { get => _flipX; set => SetProperty(ref _flipX, value); }
        [Export] public bool FlipY { get => _flipY; set => SetProperty(ref _flipY, value); }

        public override MaskRequirements MaskDataRequirements() => MaskRequirements.None;

        // --- START OF CORRECTION FOR THE ENTIRE FILE ---
        public override (Action<long> commands, List<Rid> tempRids, List<string>) CreateApplyCommands(Rid targetMaskTexture, int maskWidth, int maskHeight, Rid stitchedHeightmap = new Rid())
        {
            if (StampTexture == null) { ClearCachedTexture(); return (null, null, new List<string> { "" }); }
            Rid sourceRid = StampTexture.GetRid();
            if (!sourceRid.IsValid) { ClearCachedTexture(); return (null, null,  new List<string> { "" }); }

            if (sourceRid != _cachedSourceTextureRid) { ClearCachedTexture(); }
            
            if (!_cachedComputeTextureRid.IsValid)
            {
                ScheduleUploadTask();
            }

            if (!_cachedComputeTextureRid.IsValid) return (null, null,  new List<string> { "" });

            if (_uploadTasks.TryGetValue(sourceRid, out var uploadTask) && uploadTask.State != GpuTaskState.Completed)
            {
                // The upload is in-flight. The layer will remain dirty and re-poll next frame.
                return (null, null,  new List<string> { "" });
            }
            
            return CreateStampShaderCommands(targetMaskTexture, maskWidth, maskHeight);
        }
        
        private void ScheduleUploadTask()
        {
            if (StampTexture == null) return;
            Rid sourceRid = StampTexture.GetRid();
            Image sourceImage = StampTexture.GetImage();
            if (sourceImage == null || sourceImage.IsEmpty()) return;

            byte[] textureData = sourceImage.GetData();
            if(textureData == null || textureData.Length == 0) return;

            RenderingDevice.DataFormat rdFormat = GpuUtils.GetRdFormatFromImageFormat(sourceImage.GetFormat());
            if (rdFormat == RenderingDevice.DataFormat.Max) { ClearCachedTexture(); return; }

            _cachedComputeTextureRid = Gpu.CreateTexture2D(
                (uint)sourceImage.GetWidth(), (uint)sourceImage.GetHeight(),
                rdFormat,
                RenderingDevice.TextureUsageBits.SamplingBit | RenderingDevice.TextureUsageBits.CanUpdateBit
            );

            // CHANGED: The command must now be an Action<long> to be compatible with the task system.
            // The 'computeList' parameter is unused by TextureUpdate, but the signature must match.
            Action<long> uploadCommand = (computeList) => {
                Gpu.TextureUpdate(_cachedComputeTextureRid, 0, textureData);
            };

            Action onComplete = () => {
                if(_uploadTasks.ContainsKey(sourceRid)) _uploadTasks.Remove(sourceRid);
            };
            
            // This constructor call is now valid.
            var uploadTask = new AsyncGpuTask(uploadCommand, onComplete, null, null, "Upload Image");
            _uploadTasks[sourceRid] = uploadTask;
            AsyncGpuTaskManager.Instance.AddTask(uploadTask);
            
            _cachedSourceTextureRid = sourceRid;
        }

        private (Action<long> commands, List<Rid> tempRids, List<string>) CreateStampShaderCommands(Rid targetMaskTexture, int maskWidth, int maskHeight)
        {
            var image = StampTexture.GetImage();
            if (image == null || image.IsEmpty()) return (null, null, new List<string> { "" });
            
            var format = image.GetFormat();
            bool isHdr = format == Image.Format.Rf || format == Image.Format.Rgf || format == Image.Format.Rgbaf;
            int sampleMode = isHdr ? (int)LdrChannel.Red : (int)SourceChannel;

            var shaderPath = "res://addons/terrain_3d_tools/Shaders/Masks/stamp_mask.glsl";
            var op = new AsyncComputeOperation(shaderPath);
            
            op.BindStorageImage(0, targetMaskTexture);
            op.BindSamplerWithTexture(1, _cachedComputeTextureRid);
            var pcb = GpuUtils.CreatePushConstants()
                .Add((int)BlendType).Add(LayerMix).Add(Invert ? 1 : 0)
                .Add(sampleMode).Add(FlipX).Add(FlipY)
                .AddPadding(8)
                .Build();
            op.SetPushConstants(pcb);

            uint groupsX = (uint)((maskWidth + 7) / 8);
            uint groupsY = (uint)((maskHeight + 7) / 8);
            return (op.CreateDispatchCommands(groupsX, groupsY), op.GetTemporaryRids(), new List<string> { shaderPath });
        }
        // --- END OF CORRECTION ---

        private void ClearCachedTexture()
        {
            if (_cachedComputeTextureRid.IsValid)
            {
                Gpu.FreeRid(_cachedComputeTextureRid);
            }
            _cachedComputeTextureRid = new Rid();
            _cachedSourceTextureRid = new Rid();
        }
        
        public override void _Notification(int what)
        {
            if (what == NotificationPredelete) { ClearCachedTexture(); }
        }
    }
}