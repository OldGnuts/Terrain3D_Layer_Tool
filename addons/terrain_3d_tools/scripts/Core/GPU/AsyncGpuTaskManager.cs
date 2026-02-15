using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using Terrain3DTools.Core.Debug;

namespace Terrain3DTools.Core
{
    /// <summary>
    /// Central manager for scheduling and executing GPU compute tasks.
    /// <para>
    /// <b>Key Features:</b>
    /// <br/>- Dependency resolution (DAG execution)
    /// <br/>- Parallel task grouping based on resource conflicts
    /// <br/>- Batch submission to RenderingDevice
    /// <br/>- Resource Graveyard for deferred cleanup of GPU resources
    /// <br/>- Throttled sync to prevent "Device already submitted" errors
    /// </para>
    /// </summary>
    public partial class AsyncGpuTaskManager : Node
    {
        private const string DEBUG_CLASS_NAME = "AsyncGpuTaskManager";
        public static AsyncGpuTaskManager Instance { get; private set; }

        private readonly Dictionary<Guid, AsyncGpuTask> _allTasks = new();
        private readonly Queue<AsyncGpuTask> _readyQueue = new();
        private readonly List<AsyncGpuTask> _inFlightTasks = new();

        /// <summary>
        /// Tracks whether we have submitted work that hasn't been synced yet.
        /// </summary>
        private bool _hasPendingGpuWork = false;
        private int _pendingTaskCount = 0;
        private const int MAX_DISPATCHES_PER_FRAME = 128;

        #region Public API

        /// <summary>
        /// Returns true if there is GPU work that has been submitted but not yet synced.
        /// </summary>
        public bool HasPendingGpuSubmission => _hasPendingGpuWork;

        /// <summary>
        /// Syncs with the GPU only if there is pending work.
        /// Call this before any GPU readback operation.
        /// </summary>
        public void SyncIfNeeded()
        {
            if (_hasPendingGpuWork)
            {
                Gpu.Sync();
                _hasPendingGpuWork = false;
            }
        }

        /// <summary>
        /// Marks that a GPU submission has occurred outside of the task manager.
        /// Call this after any direct Gpu.Submit() call.
        /// </summary>
        public void MarkPendingSubmission()
        {
            _hasPendingGpuWork = true;
        }

        #endregion

        #region Resource Graveyard
        private struct StaleResource
        {
            public Rid Rid;
            public ulong DeathFrame;
        }

        private struct StaleShaderRef
        {
            public string Path;
            public ulong DeathFrame;
        }

        private readonly Queue<StaleResource> _staleResources = new();
        private readonly Queue<StaleShaderRef> _staleShaderRefs = new();

        /// <summary>
        /// Number of frames to wait before freeing a GPU resource.
        /// Default is 5 for deferred sync safety (submit → process → complete → buffer → safe).
        /// </summary>
        public int CleanupFrameThreshold { get; set; } = 5;
        #endregion

        public event Action OnBatchComplete;

        public bool HasPendingWork => _readyQueue.Count > 0 || _inFlightTasks.Count > 0 || _pendingTaskCount > 0;
        public int PendingTaskCount => _readyQueue.Count + _inFlightTasks.Count + _pendingTaskCount;

        #region Godot Lifecycle
        public override void _EnterTree()
        {
            if (Instance != null) { QueueFree(); return; }
            Instance = this;
            DebugManager.Instance?.RegisterClass(DEBUG_CLASS_NAME);
        }

        public override void _ExitTree()
        {
            if (Instance == this) Instance = null;
            FlushStaleResources();
        }

        public override void _Process(double delta)
        {
            ProcessStaleResources();

            if (_inFlightTasks.Count > 0)
            {
                int completedCount = _inFlightTasks.Count;
                foreach (var task in _inFlightTasks)
                {
                    CompleteTask(task);
                }
                _inFlightTasks.Clear();

                DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.TaskExecution,
                    $"Batch complete: {completedCount} tasks finished");

                OnBatchComplete?.Invoke();
            }

            if (_readyQueue.Count > 0)
            {
                SubmitBatch();
            }
        }
        #endregion

        #region Batch Submission
        private void SubmitBatch()
        {
            if (_readyQueue.Count == 0) return;

            // Sync if previous submission is still in flight
            SyncIfNeeded();

            var groups = BuildParallelGroups();

            int totalDispatched = 0;
            int groupsDispatched = 0;
            long computeList = Gpu.ComputeListBegin();

            for (int g = 0; g < groups.Count; g++)
            {
                if (totalDispatched >= MAX_DISPATCHES_PER_FRAME)
                {
                    RequeueRemainingGroups(groups, g);
                    break;
                }

                if (g > 0)
                {
                    Gpu.Rd.ComputeListAddBarrier(computeList);
                }

                int dispatchedInGroup = 0;
                var group = groups[g];

                for (int t = 0; t < group.Count; t++)
                {
                    if (totalDispatched >= MAX_DISPATCHES_PER_FRAME)
                    {
                        RequeueRemainingTasks(group, t);
                        RequeueRemainingGroups(groups, g + 1);
                        break;
                    }

                    var task = group[t];

                    try
                    {
                        task.Prepare();
                        task.GpuCommands?.Invoke(computeList);
                        task.State = GpuTaskState.InFlight;
                        _inFlightTasks.Add(task);
                        totalDispatched++;
                        dispatchedInGroup++;
                    }
                    catch (Exception ex)
                    {
                        DebugManager.Instance?.LogError(DEBUG_CLASS_NAME,
                            $"Task dispatch failed: {task.TaskName} - {ex.Message}");

                        task.State = GpuTaskState.Completed;
                        var (rids, paths) = task.ExtractResourcesForCleanup();
                        QueueCleanup(rids, paths);

                        try { task.OnComplete?.Invoke(); } catch { }
                        ReleaseDependents(task);
                    }
                }

                if (dispatchedInGroup > 0)
                {
                    groupsDispatched++;
                }
            }

            Gpu.ComputeListEnd();
            Gpu.Submit();
            _hasPendingGpuWork = true;  // Mark that GPU has work in flight

            DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.GpuDispatches,
                $"Submitted batch: {totalDispatched} tasks in {groupsDispatched} parallel group(s) " +
                $"(Pending: {_readyQueue.Count + _pendingTaskCount})");
        }

        /// <summary>
        /// Groups ready tasks by resource conflicts for parallel dispatch.
        /// Tasks within a group have no read/write conflicts and can execute simultaneously.
        /// Barriers are only inserted between groups.
        /// </summary>
        private List<List<AsyncGpuTask>> BuildParallelGroups()
        {
            var groups = new List<List<AsyncGpuTask>>();
            var remaining = new List<AsyncGpuTask>();

            while (_readyQueue.Count > 0)
            {
                remaining.Add(_readyQueue.Dequeue());
            }

            while (remaining.Count > 0)
            {
                var group = new List<AsyncGpuTask>();
                var groupWrites = new HashSet<Rid>();
                var groupReads = new HashSet<Rid>();

                for (int i = remaining.Count - 1; i >= 0; i--)
                {
                    var task = remaining[i];

                    if (CanJoinGroup(task, groupWrites, groupReads))
                    {
                        group.Add(task);
                        groupWrites.UnionWith(task.WriteTargets);
                        groupReads.UnionWith(task.ReadSources);
                        remaining.RemoveAt(i);
                    }
                }

                if (group.Count == 0 && remaining.Count > 0)
                {
                    var task = remaining[0];
                    group.Add(task);
                    groupWrites.UnionWith(task.WriteTargets);
                    groupReads.UnionWith(task.ReadSources);
                    remaining.RemoveAt(0);
                }

                if (group.Count > 0)
                {
                    groups.Add(group);
                }
            }

            if (groups.Count > 1)
            {
                int totalTasks = groups.Sum(g => g.Count);
                DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.TaskExecution,
                    $"Parallel grouping: {totalTasks} tasks → {groups.Count} groups");
            }

            return groups;
        }

        /// <summary>
        /// Determines if a task can be added to the current parallel group without conflicts.
        /// </summary>
        private bool CanJoinGroup(AsyncGpuTask task, HashSet<Rid> groupWrites, HashSet<Rid> groupReads)
        {
            if (task.WriteTargets.Overlaps(groupWrites))
                return false;

            if (task.WriteTargets.Overlaps(groupReads))
                return false;

            if (task.ReadSources.Overlaps(groupWrites))
                return false;

            return true;
        }

        private void RequeueRemainingGroups(List<List<AsyncGpuTask>> groups, int startIndex)
        {
            for (int g = startIndex; g < groups.Count; g++)
            {
                foreach (var task in groups[g])
                {
                    _readyQueue.Enqueue(task);
                }
            }
        }

        private void RequeueRemainingTasks(List<AsyncGpuTask> group, int startIndex)
        {
            for (int t = startIndex; t < group.Count; t++)
            {
                _readyQueue.Enqueue(group[t]);
            }
        }
        #endregion

        #region Task Completion
        private void CompleteTask(AsyncGpuTask task)
        {
            task.State = GpuTaskState.Completed;
            try
            {
                task.OnComplete?.Invoke();
            }
            catch (Exception ex)
            {
                DebugManager.Instance?.LogError(DEBUG_CLASS_NAME,
                    $"Task callback failed: {task.TaskName} - {ex.Message}");
            }

            var (rids, paths) = task.ExtractResourcesForCleanup();
            QueueCleanup(rids, paths);

            _allTasks.Remove(task.Id);
            ReleaseDependents(task);
        }
        #endregion

        #region Resource Graveyard
        /// <summary>
        /// Queues a single RID for deferred cleanup.
        /// </summary>
        public void QueueCleanup(Rid rid)
        {
            if (!rid.IsValid) return;
            _staleResources.Enqueue(new StaleResource { Rid = rid, DeathFrame = Engine.GetProcessFrames() });
        }

        /// <summary>
        /// Queues a list of RIDs and shader paths for deferred cleanup.
        /// </summary>
        public void QueueCleanup(List<Rid> rids, List<string> shaderPaths = null)
        {
            ulong currentFrame = Engine.GetProcessFrames();

            if (rids != null)
            {
                foreach (var rid in rids)
                {
                    if (rid.IsValid)
                        _staleResources.Enqueue(new StaleResource { Rid = rid, DeathFrame = currentFrame });
                }
            }

            if (shaderPaths != null)
            {
                foreach (var path in shaderPaths)
                {
                    if (!string.IsNullOrEmpty(path))
                        _staleShaderRefs.Enqueue(new StaleShaderRef { Path = path, DeathFrame = currentFrame });
                }
            }
        }

        private void ProcessStaleResources()
        {
            ulong currentFrame = Engine.GetProcessFrames();
            ulong safeFrame = currentFrame > (ulong)CleanupFrameThreshold
                ? currentFrame - (ulong)CleanupFrameThreshold
                : 0;

            int freedRids = 0;
            int releasedShaders = 0;
            int skippedInvalid = 0;

            while (_staleResources.Count > 0)
            {
                var stale = _staleResources.Peek();
                if (stale.DeathFrame >= safeFrame) break;

                _staleResources.Dequeue();
                if (stale.Rid.IsValid)
                {
                    try
                    {
                        Gpu.FreeRid(stale.Rid);
                        freedRids++;
                    }
                    catch (Exception ex)
                    {
                        DebugManager.Instance?.LogWarning(DEBUG_CLASS_NAME,
                            $"Failed to free RID: {ex.Message}");
                        skippedInvalid++;
                    }
                }
                else
                {
                    skippedInvalid++;
                }
            }

            while (_staleShaderRefs.Count > 0)
            {
                var stale = _staleShaderRefs.Peek();
                if (stale.DeathFrame >= safeFrame) break;

                _staleShaderRefs.Dequeue();
                GpuCache.ReleasePipeline(stale.Path);
                releasedShaders++;
            }

            if (freedRids > 0 || releasedShaders > 0 || skippedInvalid > 0)
            {
                DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.GpuResources,
                    $"Graveyard cleanup: Freed {freedRids} RIDs, Released {releasedShaders} shaders" +
                    (skippedInvalid > 0 ? $", Skipped {skippedInvalid} invalid" : ""));
            }
        }

        private void FlushStaleResources()
        {
            SyncIfNeeded();

            int count = _staleResources.Count + _staleShaderRefs.Count;

            while (_staleResources.Count > 0)
            {
                var stale = _staleResources.Dequeue();
                if (stale.Rid.IsValid)
                    Gpu.FreeRid(stale.Rid);
            }

            while (_staleShaderRefs.Count > 0)
            {
                var stale = _staleShaderRefs.Dequeue();
                GpuCache.ReleasePipeline(stale.Path);
            }

            DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.Cleanup,
                $"Flushed {count} stale resources on exit");
        }
        #endregion

        #region Dependency Management
        public void AddTask(AsyncGpuTask task)
        {
            if (_allTasks.ContainsKey(task.Id)) return;

            _allTasks.Add(task.Id, task);

            foreach (var dependency in task.Dependencies)
            {
                if (dependency.State != GpuTaskState.Completed && _allTasks.ContainsKey(dependency.Id))
                {
                    dependency.Dependents.Add(task);
                }
                else
                {
                    task.UnresolvedDependencies--;
                }
            }

            if (task.UnresolvedDependencies < 0) task.UnresolvedDependencies = 0;

            if (task.UnresolvedDependencies == 0)
            {
                task.State = GpuTaskState.Ready;
                _readyQueue.Enqueue(task);
            }
            else
            {
                task.State = GpuTaskState.Pending;
                _pendingTaskCount++;
            }
        }

        private void ReleaseDependents(AsyncGpuTask task)
        {
            foreach (var dependent in task.Dependents)
            {
                dependent.UnresolvedDependencies--;

                if (dependent.UnresolvedDependencies <= 0 && dependent.State == GpuTaskState.Pending)
                {
                    _pendingTaskCount--;
                    dependent.State = GpuTaskState.Ready;
                    _readyQueue.Enqueue(dependent);
                }
            }
        }

        public void CancelAllPending()
        {
            SyncIfNeeded();

            int count = _readyQueue.Count + _allTasks.Count;
            _readyQueue.Clear();
            _inFlightTasks.Clear();
            _allTasks.Clear();
            _pendingTaskCount = 0;

            DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.TaskExecution,
                $"Cancelled {count} pending tasks");
        }
        #endregion
    }
}