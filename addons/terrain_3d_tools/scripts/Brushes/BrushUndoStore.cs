// /Brushes/BrushUndoStore.cs
using Godot;
using System.Collections.Generic;
using System.Linq;
using Terrain3DTools.Core.Debug;

namespace Terrain3DTools.Brushes
{
    /// <summary>
    /// Session-scoped storage for brush undo data.
    /// Implements rolling window with configurable limits.
    /// Automatically prunes oldest entries when limits are exceeded.
    /// </summary>
    public class BrushUndoStore
    {
        private const string DEBUG_CLASS_NAME = "BrushUndoStore";
        private const int DEFAULT_MAX_ENTRIES = 30;
        private const int SMOOTH_WEIGHT = 3;

        private readonly LinkedList<UndoEntry> _entries = new();
        private readonly Dictionary<ulong, LinkedListNode<UndoEntry>> _lookup = new();
        private int _weightedCount = 0;
        private int _maxWeightedEntries;

        /// <summary>
        /// Maximum weighted entries before pruning begins.
        /// Smooth operations count as 3 entries, instance operations count as 0.
        /// </summary>
        public int MaxEntries
        {
            get => _maxWeightedEntries;
            set => _maxWeightedEntries = value > 0 ? value : DEFAULT_MAX_ENTRIES;
        }

        /// <summary>
        /// Current number of entries (unweighted).
        /// </summary>
        public int Count => _entries.Count;

        /// <summary>
        /// Approximate memory usage in bytes.
        /// </summary>
        public long EstimatedMemoryUsage => _entries.Sum(e => EstimateSize(e.Data));

        public BrushUndoStore(int maxEntries = DEFAULT_MAX_ENTRIES)
        {
            _maxWeightedEntries = maxEntries;
            DebugManager.Instance?.RegisterClass(DEBUG_CLASS_NAME);
        }

        /// <summary>
        /// Stores undo data, pruning oldest entries if over limit.
        /// </summary>
        /// <param name="undoId">Unique ID for this undo entry</param>
        /// <param name="data">The undo data to store</param>
        public void Store(ulong undoId, BrushUndoData data)
        {
            if (data == null)
            {
                DebugManager.Instance?.LogWarning(DEBUG_CLASS_NAME,
                    $"Attempted to store null undo data for ID {undoId}");
                return;
            }

            // Remove existing entry with same ID if present
            if (_lookup.TryGetValue(undoId, out var existingNode))
            {
                _weightedCount -= existingNode.Value.Weight;
                _entries.Remove(existingNode);
                _lookup.Remove(undoId);
            }

            int weight = GetWeight(data);

            var entry = new UndoEntry
            {
                Id = undoId,
                Data = data,
                Weight = weight
            };

            var node = _entries.AddFirst(entry);
            _lookup[undoId] = node;
            _weightedCount += weight;

            // Prune oldest until under limit (keep at least 1 entry)
            while (_weightedCount > _maxWeightedEntries && _entries.Count > 1)
            {
                var oldestNode = _entries.Last;
                var oldest = oldestNode.Value;

                _entries.RemoveLast();
                _lookup.Remove(oldest.Id);
                _weightedCount -= oldest.Weight;

                DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.UpdateCycle,
                    $"Pruned undo entry {oldest.Id} (weighted: {_weightedCount}/{_maxWeightedEntries})");
            }

            DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.UpdateCycle,
                $"Stored undo {undoId}, weight={weight}, total={_entries.Count}, " +
                $"weighted={_weightedCount}/{_maxWeightedEntries}, " +
                $"memory=~{EstimatedMemoryUsage / 1024}KB");
        }

        /// <summary>
        /// Retrieves undo data by ID.
        /// </summary>
        /// <param name="undoId">The undo ID to look up</param>
        /// <returns>The undo data, or null if not found or pruned</returns>
        public BrushUndoData Get(ulong undoId)
        {
            if (_lookup.TryGetValue(undoId, out var node))
            {
                return node.Value.Data;
            }

            DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.UpdateCycle,
                $"Undo data {undoId} not found (may have been pruned)");
            return null;
        }

        /// <summary>
        /// Checks if an undo entry exists.
        /// </summary>
        public bool Contains(ulong undoId)
        {
            return _lookup.ContainsKey(undoId);
        }

        /// <summary>
        /// Removes a specific undo entry.
        /// </summary>
        /// <param name="undoId">The undo ID to remove</param>
        /// <returns>True if removed, false if not found</returns>
        public bool Remove(ulong undoId)
        {
            if (_lookup.TryGetValue(undoId, out var node))
            {
                _weightedCount -= node.Value.Weight;
                _entries.Remove(node);
                _lookup.Remove(undoId);
                return true;
            }
            return false;
        }

        /// <summary>
        /// Clears all undo data. Call on save or session end.
        /// </summary>
        public void Clear()
        {
            int previousCount = _entries.Count;
            _entries.Clear();
            _lookup.Clear();
            _weightedCount = 0;

            DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.Cleanup,
                $"Cleared {previousCount} undo entries");
        }

        /// <summary>
        /// Gets weight for an undo entry based on operation type.
        /// </summary>
        private int GetWeight(BrushUndoData data)
        {
            // Smooth operations are weighted higher due to non-invertibility
            // and typically larger impact
            if (data.UndoType == BrushUndoType.Height && data.IsSmooth)
            {
                return SMOOTH_WEIGHT;
            }

            // Instance operations are command-based with tiny memory footprint
            if (data.UndoType == BrushUndoType.InstancePlacement)
            {
                return 0; // Don't count against limit
            }

            return 1;
        }

        /// <summary>
        /// Estimates memory usage of an undo entry in bytes.
        /// </summary>
        private long EstimateSize(BrushUndoData data)
        {
            const int BASE_OVERHEAD = 200; // Object overhead, references, etc.

            if (data == null)
                return 0;

            // Instance-based undo is very small
            if (data.UndoType == BrushUndoType.InstancePlacement)
            {
                int instanceCount = (data.PlacedInstances?.Count ?? 0) +
                                   (data.RemovedInstances?.Count ?? 0);
                // PlacedInstanceRecord is about 80 bytes (Vector2I + int + int + Transform3D)
                return BASE_OVERHEAD + (instanceCount * 80);
            }

            // Patch-based undo
            if (data.RegionPatches == null || data.RegionPatches.Count == 0)
                return BASE_OVERHEAD;

            long patchSize = 0;
            foreach (var patch in data.RegionPatches.Values)
            {
                // Float data (height, exclusion)
                patchSize += (patch.BeforeData?.Length ?? 0) * sizeof(float);
                patchSize += (patch.AfterData?.Length ?? 0) * sizeof(float);

                // Uint data (texture)
                patchSize += (patch.BeforeDataUint?.Length ?? 0) * sizeof(uint);
                patchSize += (patch.AfterDataUint?.Length ?? 0) * sizeof(uint);

                // Rect2I overhead
                patchSize += 16;
            }

            return BASE_OVERHEAD + patchSize;
        }

        /// <summary>
        /// Internal entry structure for the linked list.
        /// </summary>
        private class UndoEntry
        {
            public ulong Id;
            public BrushUndoData Data;
            public int Weight;
        }
    }
}