// /Core/UpdateScheduler.cs
using Godot;
using System.Collections.Generic;
using Terrain3DTools.Layers;
using Terrain3DTools.Core.Debug;

namespace Terrain3DTools.Core
{
    /// <summary>
    /// Manages update timing, interaction states, and determines when terrain processing should occur.
    /// Handles the distinction between interactive updates (during user interaction) and full updates
    /// (after interaction ends).
    /// </summary>
    public class UpdateScheduler
    {
        private const string DEBUG_CLASS_NAME = "UpdateScheduler";
        
        #region Constants
        private const double INTERACTION_THRESHOLD = 0.5;
        private const double UPDATE_INTERVAL = 0.1;
        #endregion

        #region State
        private bool _isInteracting = false;
        private double _interactionTimeout = 0.0;
        private bool _fullUpdateQueued = false;
        private double _updateTimer = 0.0;
        private readonly HashSet<TerrainLayerBase> _layersToReDirty = new();
        #endregion

        #region Properties
        /// <summary>
        /// True if currently in an interactive update cycle (user is actively making changes).
        /// </summary>
        public bool IsInteracting => _isInteracting;

        /// <summary>
        /// True if a full (non-interactive) update has been queued.
        /// </summary>
        public bool IsFullUpdateQueued => _fullUpdateQueued;

        /// <summary>
        /// Returns the set of layers that should be re-dirtied when the full update executes.
        /// </summary>
        public IReadOnlyCollection<TerrainLayerBase> LayersToReDirty => _layersToReDirty;
        #endregion

        public UpdateScheduler()
        {
            DebugManager.Instance?.RegisterClass(DEBUG_CLASS_NAME);
            DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.Initialization, 
                $"UpdateScheduler initialized (interaction threshold: {INTERACTION_THRESHOLD}s, update interval: {UPDATE_INTERVAL}s)");
        }

        #region Public API
        /// <summary>
        /// Updates timing state. Should be called every frame with delta time.
        /// </summary>
        /// <param name="delta">Time elapsed since last frame in seconds</param>
        public void Process(double delta)
        {
            if (_isInteracting)
            {
                _interactionTimeout -= delta;
                
                if (_interactionTimeout <= 0)
                {
                    // Interaction period ended
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

        /// <summary>
        /// Checks if an update should be processed this frame based on timing.
        /// </summary>
        /// <returns>True if an update should be processed</returns>
        public bool ShouldProcessUpdate()
        {
            bool shouldUpdate = _updateTimer >= UPDATE_INTERVAL || _fullUpdateQueued;
            
            if (shouldUpdate)
            {
                DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.Scheduling, 
                    $"Update triggered - Timer: {_updateTimer:F3}s, Full queued: {_fullUpdateQueued}");
            }
            
            return shouldUpdate;
        }

        /// <summary>
        /// Signals that changes have been detected, starting or extending the interaction period.
        /// </summary>
        public void SignalChanges()
        {
            bool wasInteracting = _isInteracting;
            
            _isInteracting = true;
            _interactionTimeout = INTERACTION_THRESHOLD;
            
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

        /// <summary>
        /// Determines if the current update should be treated as interactive (not final).
        /// </summary>
        /// <returns>True if this is an interactive update</returns>
        public bool IsCurrentUpdateInteractive()
        {
            bool isInteractive = _isInteracting && !_fullUpdateQueued;
            
            DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.Scheduling, 
                $"Update mode: {(isInteractive ? "INTERACTIVE" : "FULL")}");
            
            return isInteractive;
        }

        /// <summary>
        /// Marks a layer to be re-dirtied when the full update executes.
        /// Typically used for layers that were skipped during interactive resize.
        /// </summary>
        public void MarkLayerForReDirty(TerrainLayerBase layer)
        {
            if (layer != null && _layersToReDirty.Add(layer))
            {
                DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.LayerDirtying, 
                    $"Marked layer '{layer.LayerName}' for re-dirty on full update");
            }
        }

        /// <summary>
        /// Completes the current update cycle, resetting timers and processing queued states.
        /// Should be called after ProcessUpdate completes.
        /// </summary>
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

        /// <summary>
        /// Re-dirties all layers that were marked during interactive updates and clears the collection.
        /// Should be called at the start of a full update.
        /// </summary>
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

        /// <summary>
        /// Resets all timing state. Useful for cleanup or re-initialization.
        /// </summary>
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