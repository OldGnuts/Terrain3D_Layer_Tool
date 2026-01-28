// /Layers/InstancerLayer.cs
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using Terrain3DTools.Core;
using Terrain3DTools.Core.Debug;
using Terrain3DTools.Masks;
using Terrain3DTools.Layers.Instancer;
using Terrain3DTools.Utils;

namespace Terrain3DTools.Layers
{
    /// <summary>
    /// A feature layer that places mesh instances on the terrain based on a density mask.
    /// Processes after all other feature layers to respect exclusion zones.
    /// </summary>
    [GlobalClass, Tool]
    public partial class InstancerLayer : FeatureLayer
    {
        private const string DEBUG_CLASS_NAME = "InstancerLayer";

        #region Private Fields
        private Godot.Collections.Array<InstancerMeshEntry> _meshEntries = new();
        private float _baseDensity = 1.0f;
        private float _minimumSpacing = 0.5f;
        private int _seed = 12345;
        private float _exclusionThreshold = 0.5f;
        private bool _meshEntriesSubscribed = false;
        private int _previousMeshEntryCount = -1;
        private HashSet<int> _previousMeshAssetIds = new();
        #endregion

        #region Exported Properties
        [ExportGroup("Mesh Configuration")]
        [Export]
        public Godot.Collections.Array<InstancerMeshEntry> MeshEntries
        {
            get => _meshEntries;
            set
            {
                UnsubscribeFromEntries();
                _meshEntries = value ?? new();
                SubscribeToEntries();
                ForceDirty();
            }
        }

        [ExportGroup("Placement Settings")]
        [Export(PropertyHint.Range, "0.01,100,0.1")]
        public float BaseDensity
        {
            get => _baseDensity;
            set => SetProperty(ref _baseDensity, Mathf.Max(0.01f, value));
        }

        [Export(PropertyHint.Range, "0.1,50,0.1")]
        public float MinimumSpacing
        {
            get => _minimumSpacing;
            set => SetProperty(ref _minimumSpacing, Mathf.Max(0.1f, value));
        }

        [Export]
        public int Seed
        {
            get => _seed;
            set => SetProperty(ref _seed, value);
        }

        [ExportGroup("Exclusion Settings")]
        [Export(PropertyHint.Range, "0,1,0.05")]
        public float ExclusionThreshold
        {
            get => _exclusionThreshold;
            set => SetProperty(ref _exclusionThreshold, Mathf.Clamp(value, 0f, 1f));
        }
        #endregion

        #region Feature Layer Overrides
        public override bool IsInstancer => true;

        // TODO: 
        // We needed to use _Process to poll changes in the MeshEntries array 
        // in order to be able to have an immediate update when meshes where removed
        // this seems to be a limitation in Godot where the array is modified and does not
        // send a signal. Ideally we would use a signal to avoid unnecessay update loops.
        // The property setter does not seem to send the signal when the array is changed.
        public override void _Process(double delta)
        {
            base._Process(delta);

            if (Engine.IsEditorHint())
            {
                CheckMeshEntriesChanged();
            }
        }
        public override void _Ready()
        {
            base._Ready();

            // Instancers don't modify terrain data directly
            ModifiesHeight = false;
            ModifiesTexture = false;

            if (string.IsNullOrEmpty(LayerName) || LayerName.StartsWith("Feature Layer"))
                LayerName = "Instancer " + IdGenerator.GenerateShortUid();

            DebugManager.Instance?.RegisterClass(DEBUG_CLASS_NAME);

            _previousMeshEntryCount = _meshEntries?.Count ?? 0;
            _previousMeshAssetIds = new HashSet<int>(GetMeshAssetIds());

            CheckMeshEntriesChanged();
        }

        public override void _EnterTree()
        {
            base._EnterTree();
            SubscribeToEntries();
        }

        public override void _ExitTree()
        {
            base._ExitTree();
            UnsubscribeFromEntries();
        }

        public override LayerType GetLayerType() => LayerType.Feature;
        public override string LayerTypeName() => "Instancer Layer";

        /// <summary>
        /// Instancers don't write exclusion - they READ it.
        /// Override to return null explicitly.
        /// </summary>
        public override (Action<long> commands, List<Rid> tempRids, List<string> shaders) CreateWriteExclusionCommands(
            Vector2I regionCoords,
            RegionData regionData,
            int regionSize,
            Vector2 regionMinWorld,
            Vector2 regionSizeWorld)
        {
            return (null, new List<Rid>(), new List<string>());
        }

        /// <summary>
        /// Instancers don't apply to regions directly via CreateApplyRegionCommands.
        /// Placement is handled by InstancerPlacementPhase.
        /// </summary>
        public override (Action<long> commands, List<Rid> tempRids, List<string>) CreateApplyRegionCommands(
            Vector2I regionCoords,
            RegionData regionData,
            int regionSize,
            Vector2 regionMinWorld,
            Vector2 regionSizeWorld)
        {
            // Intentionally empty - instancers use separate placement phase
            return (null, new List<Rid>(), new List<string>());
        }
        #endregion

        #region State Capture

        private InstancerBakeState _activeBakeState;

        /// <summary>
        /// Captures the current state for thread-safe GPU operations.
        /// Call from main thread before submitting GPU work.
        /// </summary>
        public InstancerBakeState CaptureBakeState()
        {
            var state = new InstancerBakeState
            {
                LayerInstanceId = GetInstanceId(),
                DensityMaskRid = layerTextureRID,
                MaskWorldMin = GetWorldBounds().Min,
                MaskWorldMax = GetWorldBounds().Max,
                Size = Size,
                BaseDensity = _baseDensity,
                MinimumSpacing = _minimumSpacing,
                Seed = _seed,
                ExclusionThreshold = _exclusionThreshold,
                TotalProbabilityWeight = GetTotalProbabilityWeight()
            };

            foreach (var entry in GetValidMeshEntries())
            {
                state.MeshEntries.Add(new InstancerBakeState.MeshEntrySnapshot
                {
                    MeshAssetId = entry.MeshAssetId,
                    ProbabilityWeight = entry.ProbabilityWeight,
                    MinScale = entry.ScaleRange.X,
                    MaxScale = entry.ScaleRange.Y,
                    YRotationRangeRadians = Mathf.DegToRad(entry.YRotationRange),
                    AlignToNormal = entry.AlignToNormal,
                    NormalAlignmentStrength = entry.NormalAlignmentStrength,
                    HeightOffset = entry.HeightOffset
                });
            }

            return state;
        }

        /// <summary>
        /// Sets the active bake state. Called during PrepareMaskResources.
        /// </summary>
        public void SetActiveBakeState(InstancerBakeState state)
        {
            _activeBakeState = state;
        }

        /// <summary>
        /// Gets the active bake state for GPU operations.
        /// </summary>
        public InstancerBakeState GetActiveBakeState() => _activeBakeState;

        public override void PrepareMaskResources(bool isInteractive)
        {
            base.PrepareMaskResources(isInteractive);

            // Capture state after base resources are ready
            _activeBakeState = CaptureBakeState();
        }

        #endregion

        #region Dependency Tracking

        /// <summary>
        /// Detects when mesh entries are added/removed via the inspector.
        /// Godot modifies arrays in-place without calling the property setter.
        /// </summary>
        private void CheckMeshEntriesChanged()
        {
            int currentCount = _meshEntries?.Count ?? 0;

            // First run - initialize tracking
            if (_previousMeshEntryCount < 0)
            {
                _previousMeshEntryCount = currentCount;
                _previousMeshAssetIds = new HashSet<int>(GetMeshAssetIds());
                return;
            }

            bool changed = false;

            // Check if count changed (entry added or removed)
            if (currentCount != _previousMeshEntryCount)
            {
                changed = true;
                DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.LayerDirtying,
                    $"Mesh entry count changed: {_previousMeshEntryCount} -> {currentCount}");
            }

            // Also check if mesh IDs changed (entry's MeshAssetId was modified)
            var currentIds = new HashSet<int>(GetMeshAssetIds());
            if (!changed && !currentIds.SetEquals(_previousMeshAssetIds))
            {
                changed = true;
                DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.LayerDirtying,
                    $"Mesh asset IDs changed: [{string.Join(", ", _previousMeshAssetIds)}] -> [{string.Join(", ", currentIds)}]");
            }

            if (changed)
            {
                _previousMeshEntryCount = currentCount;
                _previousMeshAssetIds = currentIds;

                // Re-subscribe in case entries were added/removed
                UnsubscribeFromEntries();
                SubscribeToEntries();

                ForceDirty();
            }
        }
        /// <summary>
        /// Returns true if this instancer's mask uses texture data.
        /// Used to determine if texture layer changes should dirty this layer.
        /// </summary>
        public bool RequiresTextureData()
        {
            return Masks.Any(m => m != null &&
                m.MaskDataRequirements() == MaskRequirements.RequiresTextureData);
        }

        /// <summary>
        /// Returns distinct mesh asset IDs configured in this layer.
        /// </summary>
        public int[] GetMeshAssetIds()
        {
            return _meshEntries
                .Where(e => e != null && e.MeshAssetId >= 0)
                .Select(e => e.MeshAssetId)
                .Distinct()
                .ToArray();
        }

        /// <summary>
        /// Gets total probability weight for normalization.
        /// </summary>
        public float GetTotalProbabilityWeight()
        {
            return _meshEntries
                .Where(e => e != null && e.MeshAssetId >= 0)
                .Sum(e => e.ProbabilityWeight);
        }

        /// <summary>
        /// Gets valid mesh entries for GPU upload.
        /// </summary>
        public List<InstancerMeshEntry> GetValidMeshEntries()
        {
            return _meshEntries
                .Where(e => e != null && e.MeshAssetId >= 0)
                .ToList();
        }
        #endregion

        #region Subscription Management
        private void SubscribeToEntries()
        {
            if (_meshEntriesSubscribed) return;
            foreach (var entry in _meshEntries.Where(e => e != null))
            {
                entry.Changed += OnMeshEntryChanged;
            }
            _meshEntriesSubscribed = true;
        }

        private void UnsubscribeFromEntries()
        {
            if (!_meshEntriesSubscribed) return;
            foreach (var entry in _meshEntries.Where(e => GodotObject.IsInstanceValid(e)))
            {
                entry.Changed -= OnMeshEntryChanged;
            }
            _meshEntriesSubscribed = false;
        }

        private void OnMeshEntryChanged()
        {
            ForceDirty();
        }
        #endregion
    }
}