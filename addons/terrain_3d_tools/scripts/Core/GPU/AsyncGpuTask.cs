// /Core/GPU/AsyncGpuTask.cs 
using Godot;
using System;
using System.Collections.Generic;

namespace Terrain3DTools.Core
{
    public enum GpuTaskState { Pending, Ready, InFlight, Completed }

    public class AsyncGpuTask
    {
        public Guid Id { get; } = Guid.NewGuid();
        public Action<long> GpuCommands { get; }
        public Action OnComplete { get; }
        public List<Rid> TemporaryRidsToFree { get; }
        public GpuTaskState State { get; set; } = GpuTaskState.Pending;
        public string TaskName = "";
        public readonly List<AsyncGpuTask> Dependencies = new();
        public readonly List<AsyncGpuTask> Dependents = new();
        public int UnresolvedDependencies { get; set; }
        public readonly HashSet<object> ResourceOwners = new();
        public readonly List<string> CachedResourcePaths;

        public AsyncGpuTask(Action<long> gpuCommands, Action onComplete, List<Rid> tempRids, List<object> owners, string taskName, List<AsyncGpuTask> dependencies = null, List<string> cachedResourcePaths = null)
        {
            GpuCommands = gpuCommands;
            OnComplete = onComplete;
            TemporaryRidsToFree = tempRids;
            TaskName = taskName;


            if (owners != null)
            {
                foreach (var owner in owners) ResourceOwners.Add(owner);
            }

            if (dependencies != null)
            {
                Dependencies.AddRange(dependencies);
            }

            UnresolvedDependencies = Dependencies.Count;
            if (UnresolvedDependencies == 0)
            {
                State = GpuTaskState.Ready;
            }

            CachedResourcePaths = cachedResourcePaths ?? new List<string>();
        }

        public void Cleanup()
        {
            // The task just needs to free its temporary RIDs.
            //GD.Print("[AsyncGpuTask]Cleaning up gpu task");
            foreach (var rid in TemporaryRidsToFree)
            {
                Gpu.FreeRid(rid);
            }
            TemporaryRidsToFree.Clear();

            foreach (var path in CachedResourcePaths)
            {
                //GD.Print("[AsyncGpuTask]Releasing pipeline : " + path);
                GpuCache.ReleasePipeline(path);
            }
            CachedResourcePaths.Clear();
        }
    }
}