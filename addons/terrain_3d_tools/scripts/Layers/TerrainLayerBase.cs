// /Layers/TerrainLayerBase.cs 
using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Godot.Collections;
using Terrain3DTools.Core;
using Terrain3DTools.Utils;
using Terrain3DTools.Visuals;
using Terrain3DTools.Masks;

namespace Terrain3DTools.Layers
{
    public enum LayerType { Height, Texture, Feature }
    public enum FalloffType { None, Linear, Circular }

    [GlobalClass, Tool]
    public abstract partial class TerrainLayerBase : Node3D
    {
        #region Private Fields
        private string _layerName = "New Layer";
        private Vector2I _size = new(512, 512);
        private Array<TerrainMask> _masks = new();

        private float _falloffStrength = 1.0f;
        private Curve _falloffCurve;
        private FalloffType _falloffType = FalloffType.Circular;
        private bool _isDirty = true;
        private bool _positionDirty = false;
        private Vector2I _oldSize;
        private LayerVisualizer _visualizer;
        #endregion

        #region Properties
        public bool IsDirty => _isDirty;
        public bool PositionDirty => _positionDirty;
        public bool SizeHasChanged => _oldSize != _size;
        public LayerVisualizer Visualizer => _visualizer;

        [Export]
        public string LayerName
        {
            get => _layerName;
            set => SetProperty(ref _layerName, value);
        }

        [Export]
        public Vector2I Size
        {
            get => _size;
            set
            {
                if (_size == value) return;
                _size = value;
                _isDirty = true;
            }
        }

        [Export]
        public Array<TerrainMask> Masks
        {
            get => _masks;
            set
            {
                if (IsNodeReady())
                {
                    if (_masks != null) foreach (var mask in _masks.Where(m => IsInstanceValid(m))) mask.Changed -= OnMaskChanged;
                }
                _masks = value;
                if (IsNodeReady())
                {
                    if (_masks != null) foreach (var mask in _masks.Where(m => IsInstanceValid(m))) mask.Changed += OnMaskChanged;
                }
                _isDirty = true;
            }
        }
        [Export]
        public bool ModifiesHeight { get; protected set; } = false;

        [Export]
        public bool ModifiesTexture { get; protected set; } = false;

        [Export(PropertyHint.Range, "0,1,0.01")]
        public float FalloffStrength { get => _falloffStrength; set => SetProperty(ref _falloffStrength, Mathf.Clamp(value, 0f, 1f)); }

        [Export]
        public Curve FalloffCurve
        {
            get => _falloffCurve;
            set
            {
                if (IsNodeReady() && IsInstanceValid(_falloffCurve)) _falloffCurve.Changed -= OnFalloffCurveChanged;
                _falloffCurve = value;
                if (IsNodeReady() && IsInstanceValid(_falloffCurve)) _falloffCurve.Changed += OnFalloffCurveChanged;
                _isDirty = true;
            }
        }

        [Export]
        public FalloffType FalloffMode { get => _falloffType; set => SetProperty(ref _falloffType, value); }
        #endregion

        #region GPU Resources
        public Rid layerTextureRID;
        public Rid layerHeightVisualizationTextureRID;
        #endregion

        #region Abstract Methods

        // --- START OF CORRECTION ---
        /// <summary>
        /// REFACTORED: This method now returns a description of GPU commands that accept a compute list.
        /// </summary>
        /// <returns>A tuple containing the GPU command Action<long> and a list of any temporary RIDs created.</returns>
        public abstract (Action<long> commands, List<Rid> tempRids, List<string>) CreateApplyRegionCommands(
            Vector2I regionCoords,
            RegionData regionData,
            int regionSize,
            Vector2 regionMinWorld,
            Vector2 regionSizeWorld);
        // --- END OF CORRECTION ---

        public abstract LayerType GetLayerType();
        public abstract string LayerTypeName();
        #endregion

        // ... (The rest of the file remains exactly the same) ...
        #region Helpers
        protected void SetProperty<T>(ref T field, T value)
        {
            if (!Equals(field, value))
            {
                field = value;
                _isDirty = true;
            }
        }

        public void ClearDirty() { _isDirty = false; }
        public void ClearPositionDirty() { _positionDirty = false; }
        public void ForceDirty() { _isDirty = true; }
        public void ForcePositionDirty() { _positionDirty = true; }
        public virtual void MarkPositionDirty() { _positionDirty = true; }

        public bool DoesAnyMaskRequireHeightData()
        {
            return Masks.Any(mask =>
                mask != null &&
                (mask.RequiresBaseHeightData() || mask.MaskDataRequirements() == MaskRequirements.RequiresHeightData)
            );
        }
        #endregion

        #region Godot Lifecycle
        public override void _Ready()
        {
            if (!IsInGroup("terrain_layer")) AddToGroup("terrain_layer");
            _oldSize = _size;
            SetNotifyTransform(true);
            if (FalloffCurve == null) FalloffCurve = CurveUtils.CreateLinearCurve();
            if (Engine.IsEditorHint())
            {
                _visualizer = new LayerVisualizer();
                AddChild(_visualizer);
                _visualizer.Initialize(this);
            }
            ForceDirty();
            ForcePositionDirty();
        }

        public override void _Notification(int what)
        {
            if (what == NotificationTransformChanged && Engine.IsEditorHint())
            {
                MarkPositionDirty();
            }
            if (what == (int)NotificationPredelete)
            {
                Gpu.FreeRid(layerTextureRID);
                layerTextureRID = new Rid();
                Gpu.FreeRid(layerHeightVisualizationTextureRID);
                layerHeightVisualizationTextureRID = new Rid();
            }
        }

        public override void _EnterTree()
        {
            foreach (var mask in Masks.Where(m => IsInstanceValid(m))) mask.Changed += OnMaskChanged;
            if (IsInstanceValid(FalloffCurve)) FalloffCurve.Changed += OnFalloffCurveChanged;
        }

        public override void _ExitTree()
        {
            foreach (var mask in Masks.Where(m => IsInstanceValid(m))) mask.Changed -= OnMaskChanged;
            if (IsInstanceValid(FalloffCurve)) FalloffCurve.Changed -= OnFalloffCurveChanged;
        }
        #endregion

        #region Core Layer Functionality
        public virtual void PrepareMaskResources(bool isInteractive)
        {
            if (isInteractive && SizeHasChanged)
            {
                return;
            }

            int maskWidth = Size.X;
            int maskHeight = Size.Y;

            if (!layerTextureRID.IsValid || SizeHasChanged)
            {
                layerTextureRID = RefreshRid(layerTextureRID, maskWidth, maskHeight);
                layerHeightVisualizationTextureRID = RefreshRid(layerHeightVisualizationTextureRID, maskWidth, maskHeight);
                _oldSize = _size;
            }
        }

        private Rid RefreshRid(Rid rid, int maskWidth, int maskHeight)
        {
            if (rid.IsValid) Gpu.FreeRid(rid);

            return Gpu.CreateTexture2D(
                (uint)maskWidth, (uint)maskHeight,
                RenderingDevice.DataFormat.R32Sfloat,
                RenderingDevice.TextureUsageBits.StorageBit | RenderingDevice.TextureUsageBits.SamplingBit | RenderingDevice.TextureUsageBits.CanCopyFromBit
            );
        }

        private void OnMaskChanged()
        {
            _isDirty = true;
        }

        private void OnFalloffCurveChanged()
        {
            _isDirty = true;
        }
        #endregion
    }
}