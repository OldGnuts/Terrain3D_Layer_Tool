// /Core/GPU/AsyncGpuTaskManager.cs
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using Terrain3DTools.Core.Debug;

namespace Terrain3DTools.Core
{
    /// <summary>
    /// Manages GPU task execution with a simple batch processing model.
    /// Submits work, waits for completion, processes results.
    /// </summary>
    public partial class AsyncGpuTaskManager : Node
    {
        private const string DEBUG_CLASS_NAME = "AsyncGpuTaskManager";
        public static AsyncGpuTaskManager Instance { get; private set; }

        private readonly Dictionary<Guid, AsyncGpuTask> _allTasks = new();
        private readonly Queue<AsyncGpuTask> _readyQueue = new();
        private readonly List<AsyncGpuTask> _inFlightTasks = new();
        private bool _isGpuBusy = false;

        // Callback when a batch completes
        public event Action OnBatchComplete;

        public bool HasPendingWork => _readyQueue.Count > 0 || _inFlightTasks.Count > 0;
        public int PendingTaskCount => _readyQueue.Count + _inFlightTasks.Count;

        public override void _EnterTree()
        {
            if (Instance != null)
            {
                QueueFree();
                return;
            }
            Instance = this;
            DebugManager.Instance?.RegisterClass(DEBUG_CLASS_NAME);
        }

        public override void _ExitTree()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>
        /// Adds a task to the execution queue.
        /// Tasks with unresolved dependencies remain pending.
        /// </summary>
        public void AddTask(AsyncGpuTask task)
        {
            if (_allTasks.ContainsKey(task.Id))
            {
                DebugManager.Instance?.LogWarning(DEBUG_CLASS_NAME,
                    $"Duplicate task: {task.TaskName}");
                return;
            }

            _allTasks.Add(task.Id, task);

            // Link dependencies
            foreach (var dependency in task.Dependencies)
            {
                dependency.Dependents.Add(task);
            }

            // Queue if ready
            if (task.State == GpuTaskState.Ready)
            {
                _readyQueue.Enqueue(task);
            }

            DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.TaskCreation,
                $"Added task: {task.TaskName} (deps: {task.Dependencies.Count})");
        }

        public override void _Process(double delta)
        {
            // Wait for in-flight batch to complete
            if (_isGpuBusy)
            {
                Gpu.Rd.Sync(); // Blocking wait for GPU

                DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.PerformanceMetrics,
                    $"Batch completed: {_inFlightTasks.Count} tasks");

                foreach (var task in _inFlightTasks)
                {
                    CompleteTask(task);
                }

                _inFlightTasks.Clear();
                _isGpuBusy = false;

                // Notify listeners that batch is done
                OnBatchComplete?.Invoke();
            }

            // Submit new batch if ready
            if (!_isGpuBusy && _readyQueue.Count > 0)
            {
                SubmitBatch();
            }
        }

        private void SubmitBatch()
        {
            int batchSize = _readyQueue.Count;
            DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.GpuDispatches,
                $"Submitting batch: {batchSize} tasks");

            long computeList = Gpu.ComputeListBegin();

            while (_readyQueue.Count > 0)
            {
                var task = _readyQueue.Dequeue();

                try
                {
                    task.GpuCommands?.Invoke(computeList);
                    task.State = GpuTaskState.InFlight;
                    _inFlightTasks.Add(task);
                }
                catch (Exception ex)
                {
                    DebugManager.Instance?.LogError(DEBUG_CLASS_NAME,
                        $"Task dispatch failed: {task.TaskName} - {ex.Message}");

                    // Mark as failed and complete immediately
                    task.State = GpuTaskState.Completed;
                    CompleteTask(task);
                }
            }

            Gpu.ComputeListEnd();
            Gpu.Submit();
            _isGpuBusy = true;
        }

        private void CompleteTask(AsyncGpuTask task)
        {
            task.State = GpuTaskState.Completed;

            // Invoke callback
            try
            {
                task.OnComplete?.Invoke();
            }
            catch (Exception ex)
            {
                DebugManager.Instance?.LogError(DEBUG_CLASS_NAME,
                    $"Task callback failed: {task.TaskName} - {ex.Message}");
            }

            // Cleanup resources
            task.Cleanup();
            _allTasks.Remove(task.Id);

            // Unlock dependents
            int readyCount = 0;
            foreach (var dependent in task.Dependents)
            {
                dependent.UnresolvedDependencies--;

                if (dependent.UnresolvedDependencies == 0 &&
                    dependent.State == GpuTaskState.Pending)
                {
                    dependent.State = GpuTaskState.Ready;
                    _readyQueue.Enqueue(dependent);
                    readyCount++;
                }
            }

            if (readyCount > 0)
            {
                DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.TaskDependencies,
                    $"Unlocked {readyCount} dependent(s)");
            }
        }

        /// <summary>
        /// Cancels all pending tasks. In-flight tasks will still complete.
        /// </summary>
        public void CancelAllPending()
        {
            int cancelledCount = _readyQueue.Count;

            // Remove from tracking
            while (_readyQueue.Count > 0)
            {
                var task = _readyQueue.Dequeue();
                task.Cleanup();
                _allTasks.Remove(task.Id);
            }

            if (cancelledCount > 0)
            {
                DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.TaskExecution,
                    $"Cancelled {cancelledCount} pending tasks");
            }
        }
    }
}