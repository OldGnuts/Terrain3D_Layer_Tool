// /Core/UpdateScheduler.cs
using Godot;
using System.Collections.Generic;
using Terrain3DTools.Layers;
using Terrain3DTools.Core.Debug;

namespace Terrain3DTools.Core
{
    /// <summary>
    /// Manages update timing and interaction states. Determines when terrain processing
    /// should occur and distinguishes between interactive updates (during user interaction)
    /// and full updates (after interaction ends).
    /// </summary>
    public class UpdateScheduler
    {
        private const string DEBUG_CLASS_NAME = "UpdateScheduler";

        #region Timing Configuration
        private double _interactionThreshold = 0.5;
        private double _updateInterval = 0.1;

        /// <summary>
        /// Time in seconds to wait after the last change before considering interaction complete.
        /// Lower values = faster final updates, higher values = better batching of rapid changes.
        /// </summary>
        public double InteractionThreshold
        {
            get => _interactionThreshold;
            set => _interactionThreshold = Mathf.Max(0.1, value);
        }

        /// <summary>
        /// Time in seconds between update checks. Lower values = more responsive but higher CPU usage.
        /// </summary>
        public double UpdateInterval
        {
            get => _updateInterval;
            set => _updateInterval = Mathf.Max(0.016, value); // Minimum ~60fps
        }
        #endregion

        #region State
        private bool _isInteracting = false;
        private double _interactionTimeout = 0.0;
        private bool _fullUpdateQueued = false;
        private double _updateTimer = 0.0;
        private readonly HashSet<TerrainLayerBase> _layersToReDirty = new();
        #endregion

        #region Properties
        public bool IsInteracting => _isInteracting;
        public bool IsFullUpdateQueued => _fullUpdateQueued;
        public IReadOnlyCollection<TerrainLayerBase> LayersToReDirty => _layersToReDirty;
        #endregion

        public UpdateScheduler()
        {
            DebugManager.Instance?.RegisterClass(DEBUG_CLASS_NAME);
            DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.Initialization,
                $"UpdateScheduler initialized (interaction threshold: {_interactionThreshold}s, update interval: {_updateInterval}s)");
        }

        #region Public API
        public void Process(double delta)
        {
            if (_isInteracting)
            {
                _interactionTimeout -= delta;

                if (_interactionTimeout <= 0)
                {
                    _isInteracting = false;
                    _fullUpdateQueued = true;

                    DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.Scheduling,
                        "Interaction period ended - full update queued");

                    if (_layersToReDirty.Count > 0)
                    {
                        DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.Scheduling,
                            $"{_layersToReDirty.Count} layer(s) will be re-dirtied on full update");
                    }
                }
            }

            _updateTimer += delta;
        }

        public bool ShouldProcessUpdate()
        {
            bool shouldUpdate = _updateTimer >= _updateInterval || _fullUpdateQueued;

            if (shouldUpdate)
            {
                DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.Scheduling,
                    $"Update triggered - Timer: {_updateTimer:F3}s, Full queued: {_fullUpdateQueued}");
            }

            return shouldUpdate;
        }

        public void SignalChanges()
        {
            bool wasInteracting = _isInteracting;

            _isInteracting = true;
            _interactionTimeout = _interactionThreshold;

            if (!wasInteracting)
            {
                DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.Scheduling,
                    "Interaction period started");
            }
            else
            {
                DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.Scheduling,
                    "Interaction period extended");
            }
        }

        public bool IsCurrentUpdateInteractive()
        {
            bool isInteractive = _isInteracting && !_fullUpdateQueued;

            DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.Scheduling,
                $"Update mode: {(isInteractive ? "INTERACTIVE" : "FULL")}");

            return isInteractive;
        }

        public void MarkLayerForReDirty(TerrainLayerBase layer)
        {
            if (layer != null && _layersToReDirty.Add(layer))
            {
                DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.LayerDirtying,
                    $"Marked layer '{layer.LayerName}' for re-dirty on full update");
            }
        }

        public void CompleteUpdateCycle()
        {
            _updateTimer = 0;

            if (_fullUpdateQueued)
            {
                _fullUpdateQueued = false;

                DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.Scheduling,
                    "Full update completed");
            }
        }

        public void ProcessReDirtyLayers()
        {
            if (_layersToReDirty.Count == 0) return;

            DebugManager.Instance?.StartTimer(DEBUG_CLASS_NAME, DebugCategory.LayerDirtying, "ProcessReDirtyLayers");

            int reDirtiedCount = 0;
            foreach (var layer in _layersToReDirty)
            {
                if (GodotObject.IsInstanceValid(layer))
                {
                    layer.ForceDirty();
                    reDirtiedCount++;

                    DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.LayerDetails,
                        $"Re-dirtied layer: {layer.LayerName}");
                }
            }

            _layersToReDirty.Clear();

            DebugManager.Instance?.EndTimer(DEBUG_CLASS_NAME, DebugCategory.LayerDirtying, "ProcessReDirtyLayers");
            DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.LayerDirtying,
                $"Re-dirtied {reDirtiedCount} layer(s)");
        }

        public void Reset()
        {
            DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.Cleanup,
                $"Resetting scheduler - Was interacting: {_isInteracting}, Layers to re-dirty: {_layersToReDirty.Count}");

            _isInteracting = false;
            _interactionTimeout = 0.0;
            _fullUpdateQueued = false;
            _updateTimer = 0.0;
            _layersToReDirty.Clear();
        }
        #endregion
    }
}