using Godot;
using System;
using System.Collections.Generic;
using Terrain3DTools.Core.Debug;

namespace Terrain3DTools.Core
{
    /// <summary>
    /// Central manager for scheduling and executing GPU compute tasks.
    /// <para>
    /// <b>Key Features:</b>
    /// <br/>- Dependency resolution (DAG execution)
    /// <br/>- Batch submission to RenderingDevice
    /// <br/>- <b>Resource Graveyard:</b> Handles deferred cleanup of GPU resources to prevent "Invalid ID" crashes.
    /// <br/>- <b>Throttled Sync:</b> Manages RenderingDevice synchronization to prevent "Device already submitted" errors.
    /// </para>
    /// </summary>
    public partial class AsyncGpuTaskManager : Node
    {
        private const string DEBUG_CLASS_NAME = "AsyncGpuTaskManager";
        public static AsyncGpuTaskManager Instance { get; private set; }

        private readonly Dictionary<Guid, AsyncGpuTask> _allTasks = new();
        private readonly Queue<AsyncGpuTask> _readyQueue = new();
        private readonly List<AsyncGpuTask> _inFlightTasks = new();

        private int _pendingTaskCount = 0;
        private const int MAX_DISPATCHES_PER_FRAME = 128;

        #region Resource Graveyard Data
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
        /// Default is 3 (Double buffering + 1 safety frame).
        /// </summary>
        public int CleanupFrameThreshold { get; set; } = 3;
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
            FlushStaleResources(); // Force clean on exit
        }

        public override void _Process(double delta)
        {
            // 1. Process Graveyard (Free old resources)
            ProcessStaleResources();

            // 2. Handle Completed Batches
            // Tasks in _inFlightTasks were submitted in a previous frame.
            // We consider them "logically" complete on CPU.
            if (_inFlightTasks.Count > 0)
            {
                int completedCount = _inFlightTasks.Count;
                foreach (var task in _inFlightTasks)
                {
                    if (task.TaskName.Contains("Feature"))
                    {
                        DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.TaskExecution,
                        $"Completing task on frame {Engine.GetProcessFrames()} : {task.TaskName}");
                    }
                    CompleteTask(task);
                }
                _inFlightTasks.Clear();

                // Aggregate logging to avoid spam
                DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.TaskExecution,
                    $"Batch Complete: {completedCount} tasks finished (Logical)");

                OnBatchComplete?.Invoke();
            }

            // 3. Submit New Batch
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

            int batchCount = 0;
            long computeList = Gpu.ComputeListBegin();

            while (_readyQueue.Count > 0 && batchCount < MAX_DISPATCHES_PER_FRAME)
            {
                var task = _readyQueue.Dequeue();

                try
                {
                    // JIT Allocation
                    task.Prepare();

                    if (batchCount > 0)
                    {
                        Gpu.Rd.ComputeListAddBarrier(computeList);
                    }

                    task.GpuCommands?.Invoke(computeList);

                    task.State = GpuTaskState.InFlight;
                    _inFlightTasks.Add(task);
                    batchCount++;
                }
                catch (Exception ex)
                {
                    DebugManager.Instance?.LogError(DEBUG_CLASS_NAME, $"Task dispatch failed: {task.TaskName} - {ex.Message}");
                    task.State = GpuTaskState.Completed;

                    // Even if dispatch failed, we must safely cleanup any resources allocated during Prepare()
                    var (rids, paths) = task.ExtractResourcesForCleanup();
                    QueueCleanup(rids, paths);

                    try { task.OnComplete?.Invoke(); } catch { }

                    ReleaseDependents(task);
                }
            }

            Gpu.ComputeListEnd();
            Gpu.Submit();
            Gpu.Sync();

            DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.GpuDispatches,
                $"Submitted Batch: {batchCount} tasks (Pending: {_readyQueue.Count + _pendingTaskCount})");
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
                DebugManager.Instance?.LogError(DEBUG_CLASS_NAME, $"Task callback failed: {task.TaskName} - {ex.Message}");
            }

            // --- CRITICAL STABILITY FIX ---
            // We do NOT call Gpu.FreeRid here. 
            // We extract the resources and queue them for deferred cleanup.
            var (rids, paths) = task.ExtractResourcesForCleanup();
            QueueCleanup(rids, paths);

            _allTasks.Remove(task.Id);
            ReleaseDependents(task);
        }
        #endregion

        #region Resource Graveyard Logic
        /// <summary>
        /// Queue a single RID for deferred cleanup. 
        /// Use this for external systems (like PathLayer) that need safe disposal.
        /// </summary>
        public void QueueCleanup(Rid rid)
        {
            if (!rid.IsValid) return;
            _staleResources.Enqueue(new StaleResource { Rid = rid, DeathFrame = Engine.GetProcessFrames() });
        }

        /// <summary>
        /// Queue a list of RIDs and Shader paths for deferred cleanup.
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

        /// <summary>
        /// Checks the graveyard and frees resources that have aged beyond the threshold.
        /// </summary>
        private void ProcessStaleResources()
        {
            ulong currentFrame = Engine.GetProcessFrames();
            ulong safeFrame = currentFrame > (ulong)CleanupFrameThreshold ? currentFrame - (ulong)CleanupFrameThreshold : 0;

            int freedRids = 0;
            int releasedShaders = 0;
            int skippedInvalid = 0;

            // Free RIDs
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
                    catch (System.Exception ex)
                    {
                        DebugManager.Instance?.LogWarning(DEBUG_CLASS_NAME,
                            $"Failed to free RID (may already be freed): {ex.Message}");
                        skippedInvalid++;
                    }
                }
                else
                {
                    skippedInvalid++;
                }
            }

            // Release Shader Refs
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
                    $"Graveyard Cleanup: Freed {freedRids} RIDs, Released {releasedShaders} Shaders, Skipped {skippedInvalid} invalid");
            }
        }

        private void FlushStaleResources()
        {
            // If the manager is being destroyed, we force wait for the GPU and clean everything immediately.
            Gpu.Sync();

            int count = _staleResources.Count + _staleShaderRefs.Count;

            while (_staleResources.Count > 0)
            {
                var stale = _staleResources.Dequeue();
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

        #region Dependency Logic
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