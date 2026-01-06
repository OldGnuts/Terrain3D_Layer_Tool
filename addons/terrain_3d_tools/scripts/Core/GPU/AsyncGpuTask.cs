using Godot;
using System;
using System.Collections.Generic;

namespace Terrain3DTools.Core
{
    public enum GpuTaskState { Pending, Ready, InFlight, Completed }

    /// <summary>
    /// Represents a unit of work to be executed on the GPU.
    /// <para>
    /// <b>Refactor Note:</b> This class no longer manages the direct disposal of GPU resources.
    /// Instead, it collects resources used during execution and hands them off to the
    /// <see cref="AsyncGpuTaskManager"/> for deferred cleanup via the Resource Graveyard pattern.
    /// </para>
    /// </summary>
    public class AsyncGpuTask
    {
        public Guid Id { get; } = Guid.NewGuid();
        
        /// <summary>
        /// The raw GPU command action to be executed on a ComputeList.
        /// </summary>
        public Action<long> GpuCommands { get; private set; }
        
        // Internal storage for resources that need to be freed after execution.
        // Changed from public properties to private fields to enforce the Extraction pattern.
        private List<Rid> _temporaryRidsToFree;
        private List<string> _cachedResourcePaths;
        
        public Action OnComplete { get; }
        public GpuTaskState State { get; set; } = GpuTaskState.Pending;
        public string TaskName = "";
        
        public readonly List<AsyncGpuTask> Dependencies = new();
        public readonly List<AsyncGpuTask> Dependents = new();
        public int UnresolvedDependencies { get; set; }
        
        /// <summary>
        /// Objects (Layers/Regions) that "own" this task. Used for debugging/tracking.
        /// </summary>
        public readonly HashSet<object> ResourceOwners = new();

        // The JIT generator function. Null after Prepare() is called.
        private Func<(Action<long> cmds, List<Rid> rids, List<string> shaders)> _commandGenerator;

        #region Constructors

        /// <summary>
        /// <b>Lazy Constructor (JIT):</b>
        /// Resources and Commands are allocated only when <see cref="Prepare"/> is called.
        /// Preferred for complex tasks to avoid allocation spikes during the planning phase.
        /// </summary>
        public AsyncGpuTask(
            Func<(Action<long>, List<Rid>, List<string>)> commandGenerator,
            Action onComplete,
            List<object> owners,
            string taskName,
            List<AsyncGpuTask> dependencies = null)
        {
            _commandGenerator = commandGenerator;
            OnComplete = onComplete;
            TaskName = taskName;
            
            _temporaryRidsToFree = new List<Rid>();
            _cachedResourcePaths = new List<string>();

            if (owners != null) foreach (var owner in owners) ResourceOwners.Add(owner);
            if (dependencies != null) Dependencies.AddRange(dependencies);

            UnresolvedDependencies = Dependencies.Count;
            if (UnresolvedDependencies == 0) State = GpuTaskState.Ready;
        }

        /// <summary>
        /// <b>Eager Constructor:</b>
        /// Resources and Commands are pre-allocated.
        /// Use for simple tasks where overhead is minimal.
        /// </summary>
        public AsyncGpuTask(
            Action<long> gpuCommands, 
            Action onComplete, 
            List<Rid> tempRids, 
            List<object> owners, 
            string taskName, 
            List<AsyncGpuTask> dependencies = null, 
            List<string> cachedResourcePaths = null)
        {
            GpuCommands = gpuCommands;
            OnComplete = onComplete;
            
            _temporaryRidsToFree = tempRids ?? new List<Rid>();
            _cachedResourcePaths = cachedResourcePaths ?? new List<string>();
            TaskName = taskName;

            if (owners != null) foreach (var owner in owners) ResourceOwners.Add(owner);
            if (dependencies != null) Dependencies.AddRange(dependencies);

            UnresolvedDependencies = Dependencies.Count;
            if (UnresolvedDependencies == 0) State = GpuTaskState.Ready;
        }
        #endregion

        /// <summary>
        /// JIT Initialization. Called by the manager right before execution.
        /// Executes the generator function to populate GpuCommands and resource lists.
        /// </summary>
        public void Prepare()
        {
            if (_commandGenerator != null && GpuCommands == null)
            {
                try 
                {
                    var result = _commandGenerator.Invoke();
                    GpuCommands = result.cmds;
                    
                    if (result.rids != null) 
                        _temporaryRidsToFree.AddRange(result.rids);
                    
                    if (result.shaders != null) 
                        _cachedResourcePaths.AddRange(result.shaders);
                }
                catch (Exception ex)
                {
                    GD.PrintErr($"[AsyncGpuTask] Failed to prepare task '{TaskName}': {ex.Message}");
                    // Ensure we don't crash the manager; allow it to run an empty command
                    GpuCommands = (l) => { }; 
                }
                finally
                {
                    _commandGenerator = null; // Free the closure to save memory
                }
            }
        }

        /// <summary>
        /// Transfers ownership of temporary resources to the caller (Manager) for cleanup.
        /// <para>
        /// <b>Crucial:</b> This clears the internal lists to prevent double-freeing.
        /// The caller becomes responsible for queueing these RIDs into the Resource Graveyard.
        /// </para>
        /// </summary>
        public (List<Rid> rids, List<string> shaderPaths) ExtractResourcesForCleanup()
        {
            var rids = new List<Rid>(_temporaryRidsToFree);
            var paths = new List<string>(_cachedResourcePaths);
            
            _temporaryRidsToFree.Clear();
            _cachedResourcePaths.Clear();
            
            return (rids, paths);
        }
        
        // Expose read-only access if needed for debugging, but modification is forbidden
        public IReadOnlyList<Rid> PeekTemporaryRids() => _temporaryRidsToFree;
    }
}