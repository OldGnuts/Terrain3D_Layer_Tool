using Godot;
using System;
using System.Collections.Generic;

namespace Terrain3DTools.Core
{
    public enum GpuTaskState { Pending, Ready, InFlight, Completed }

    /// <summary>
    /// Represents a unit of work to be executed on the GPU.
    /// <para>
    /// <b>Resource Tracking:</b> Tasks declare their read/write targets to enable
    /// parallel dispatch grouping. Tasks with non-conflicting resources execute
    /// without barriers between them.
    /// </para>
    /// <para>
    /// <b>Cleanup:</b> This class collects resources used during execution and hands 
    /// them off to the <see cref="AsyncGpuTaskManager"/> for deferred cleanup via 
    /// the Resource Graveyard pattern.
    /// </para>
    /// </summary>
    public class AsyncGpuTask
    {
        public Guid Id { get; } = Guid.NewGuid();
        
        /// <summary>
        /// The raw GPU command action to be executed on a ComputeList.
        /// </summary>
        public Action<long> GpuCommands { get; private set; }
        
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

        /// <summary>
        /// RIDs this task writes to. Used by the task manager to group non-conflicting 
        /// tasks for parallel dispatch.
        /// </summary>
        public readonly HashSet<Rid> WriteTargets = new();

        /// <summary>
        /// RIDs this task reads from. Used by the task manager to group non-conflicting 
        /// tasks for parallel dispatch.
        /// </summary>
        public readonly HashSet<Rid> ReadSources = new();

        private Func<(Action<long> cmds, List<Rid> rids, List<string> shaders)> _commandGenerator;

        #region Constructors

        /// <summary>
        /// <b>Lazy Constructor (JIT):</b>
        /// Resources and commands are allocated only when <see cref="Prepare"/> is called.
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

            if (owners != null) 
                foreach (var owner in owners) 
                    ResourceOwners.Add(owner);
            
            if (dependencies != null) 
                Dependencies.AddRange(dependencies);

            UnresolvedDependencies = Dependencies.Count;
            if (UnresolvedDependencies == 0) State = GpuTaskState.Ready;
        }

        /// <summary>
        /// <b>Eager Constructor:</b>
        /// Resources and commands are pre-allocated.
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

            if (owners != null) 
                foreach (var owner in owners) 
                    ResourceOwners.Add(owner);
            
            if (dependencies != null) 
                Dependencies.AddRange(dependencies);

            UnresolvedDependencies = Dependencies.Count;
            if (UnresolvedDependencies == 0) State = GpuTaskState.Ready;
        }
        #endregion

        #region Resource Declaration

        /// <summary>
        /// Declares the RIDs this task will write to and read from.
        /// Must be called before the task is added to the manager.
        /// <para>
        /// The task manager uses this information to group non-conflicting tasks
        /// for parallel dispatch, reducing unnecessary GPU barriers.
        /// </para>
        /// </summary>
        /// <param name="writes">RIDs this task will write to (modify)</param>
        /// <param name="reads">RIDs this task will read from (sample/copy)</param>
        public void DeclareResources(IEnumerable<Rid> writes, IEnumerable<Rid> reads = null)
        {
            if (writes != null)
            {
                foreach (var rid in writes)
                {
                    if (rid.IsValid)
                        WriteTargets.Add(rid);
                }
            }

            if (reads != null)
            {
                foreach (var rid in reads)
                {
                    if (rid.IsValid)
                        ReadSources.Add(rid);
                }
            }
        }

        /// <summary>
        /// Adds a single write target RID.
        /// </summary>
        public void AddWriteTarget(Rid rid)
        {
            if (rid.IsValid)
                WriteTargets.Add(rid);
        }

        /// <summary>
        /// Adds a single read source RID.
        /// </summary>
        public void AddReadSource(Rid rid)
        {
            if (rid.IsValid)
                ReadSources.Add(rid);
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
                    GpuCommands = (l) => { }; 
                }
                finally
                {
                    _commandGenerator = null;
                }
            }
        }

        /// <summary>
        /// Transfers ownership of temporary resources to the caller for cleanup.
        /// <para>
        /// Clears the internal lists to prevent double-freeing.
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
        
        public IReadOnlyList<Rid> PeekTemporaryRids() => _temporaryRidsToFree;
    }
}