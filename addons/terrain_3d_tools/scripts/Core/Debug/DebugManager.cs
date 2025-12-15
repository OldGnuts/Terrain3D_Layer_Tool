using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using Godot.Collections;
using Terrain3DWrapper;
using System.ComponentModel;

namespace Terrain3DTools.Core.Debug
{
    /// <summary>
    /// Central debug logging system for the terrain tools.
    /// Manages per-class debug settings and provides aggregation for high-frequency messages.
    /// </summary>
    public partial class DebugManager : GodotObject
    {
        #region Singleton Pattern
        /// <summary>
        /// The active debug manager instance. Set by TerrainLayerManager on initialization.
        /// </summary>
        public static DebugManager Instance { get; set; }
        #endregion

        #region Settings
        private bool _alwaysReportErrors = true;
        private bool _alwaysReportWarnings = true;
        private bool _enableMessageAggregation = true;
        private float _aggregationWindowSeconds = 1.0f;

        public bool AlwaysReportErrors
        {
            get => _alwaysReportErrors;
            set => _alwaysReportErrors = value;
        }

        public bool AlwaysReportWarnings
        {
            get => _alwaysReportWarnings;
            set => _alwaysReportWarnings = value;
        }

        public bool EnableMessageAggregation
        {
            get => _enableMessageAggregation;
            set => _enableMessageAggregation = value;
        }

        public float AggregationWindowSeconds
        {
            get => _aggregationWindowSeconds;
            set => _aggregationWindowSeconds = Mathf.Max(0.1f, value);
        }
        #endregion

        #region Class Registration
        private readonly System.Collections.Generic.Dictionary<string, ClassDebugConfig> _classConfigs = new();
        private readonly HashSet<string> _registeredClasses = new();

        /// <summary>
        /// Registers a class as available for debugging.
        /// Called by classes in their constructors.
        /// </summary>
        public void RegisterClass(string className)
        {
            if (string.IsNullOrEmpty(className)) return;
            if (_registeredClasses.Contains(className)) return;
            _registeredClasses.Add(className);
        }

        /// <summary>
        /// Gets all classes that have registered themselves.
        /// Used by the inspector to populate the class list.
        /// </summary>
        public List<string> GetRegisteredClasses()
        {
            return _registeredClasses.OrderBy(c => c).ToList();
        }

        /// <summary>
        /// Updates the internal config map from the inspector array.
        /// Called by TerrainLayerManager when settings change.
        /// Safely handles mixed resource types in the array.
        /// </summary>
        public void UpdateFromConfigArray(Array<ClassDebugConfig> configArray)
        {
            _classConfigs.Clear();

            if (configArray == null || configArray.Count == 0)
            {
                return;
            }

            GD.Print($"[DebugManager] Processing {configArray.Count} config items from the inspector.");

            for (int i = 0; i < configArray.Count; i++)
            {
                var config = configArray[i];

                if (config != null)
                {
                    GD.Print($"[DebugManager] Processing item {i}: Instance ID {config.GetInstanceId()}");
                    GD.Print($"  - Original Config: Class: {config.ClassName}, Enabled: {config.Enabled}, Categories: {config.EnabledCategories}");

                    if (!string.IsNullOrEmpty(config.ClassName))
                    {
                        // Don't create a new instance, use the one from the editor
                        _classConfigs[config.ClassName] = config;
                    }
                }
                else
                {
                    GD.PrintErr($"[DebugManager] Item {i} in config array is null.");
                }
            }

            if (_classConfigs.Count > 0)
            {
                GD.Print($"[DebugManager] Loaded {_classConfigs.Count} debug class configuration(s):");
                foreach (var kvp in _classConfigs)
                {
                    var loadedConfig = kvp.Value;
                    GD.Print($"  - Loaded: Class: {loadedConfig.ClassName}, Enabled: {loadedConfig.Enabled}, Categories: {loadedConfig.EnabledCategories}, Instance ID: {loadedConfig.GetInstanceId()}");
                }
            }
        }        
        #endregion

        #region Message Aggregation
        private class MessageKey
        {
            public string ClassName;
            public DebugCategory Category;
            public string Message;

            public override bool Equals(object obj)
            {
                if (obj is MessageKey other)
                {
                    return ClassName == other.ClassName &&
                           Category == other.Category &&
                           Message == other.Message;
                }
                return false;
            }

            public override int GetHashCode()
            {
                return HashCode.Combine(ClassName, Category, Message);
            }
        }

        private class AggregatedMessage
        {
            public int Count = 0;
            public double FirstLogTime = 0; // New field
            public double LastLogTime = 0;
        }

        private readonly System.Collections.Generic.Dictionary<MessageKey, AggregatedMessage> _aggregatedMessages = new();
        private double _currentTime = 0;

        /// <summary>
        /// Should be called each frame to update timing for aggregation.
        /// </summary>
        public void Process(double delta)
        {
            _currentTime += delta;

            // Flush messages that have expired
            foreach (var kvp in _aggregatedMessages.ToList())
            {
                if (_currentTime - kvp.Value.FirstLogTime >= _aggregationWindowSeconds)
                {
                    string outputMessage = kvp.Value.Count > 1
                        ? $"{kvp.Key.Message} (x{kvp.Value.Count})"
                        : kvp.Key.Message;

                    OutputMessage(kvp.Key.ClassName, kvp.Key.Category, outputMessage);
                    _aggregatedMessages.Remove(kvp.Key);
                }
            }
        }

        /// <summary>
        /// Flushes any pending aggregated messages.
        /// Called at the end of update cycles or when aggregation window expires.
        /// </summary>
        public void FlushAggregatedMessages()
        {
            foreach (var kvp in _aggregatedMessages.ToList())
            {
                var key = kvp.Key;
                var agg = kvp.Value;

                if (agg.Count > 0)
                {
                    string message = agg.Count > 1
                        ? $"{key.Message} (x{agg.Count})"
                        : key.Message;

                    OutputMessage(key.ClassName, key.Category, message);
                }
            }

            _aggregatedMessages.Clear();
        }
        #endregion

        #region Batch Counting
        private class BatchCounter
        {
            public System.Collections.Generic.Dictionary<string, int> Counts = new();
        }

        private readonly System.Collections.Generic.Dictionary<string, BatchCounter> _activeBatches = new();

        /// <summary>
        /// Begins a batch counting session for high-frequency operations.
        /// </summary>
        public void BeginBatch(string className)
        {
            if (!_activeBatches.ContainsKey(className))
            {
                _activeBatches[className] = new BatchCounter();
            }
        }

        /// <summary>
        /// Counts an event in the current batch.
        /// </summary>
        public void CountEvent(string className, DebugCategory category, string eventName)
        {
            if (!ShouldLog(className, category)) return;

            if (!_activeBatches.TryGetValue(className, out var batch))
            {
                // No active batch, log immediately
                Log(className, category, eventName);
                return;
            }

            string key = $"{category}:{eventName}";
            if (!batch.Counts.ContainsKey(key))
            {
                batch.Counts[key] = 0;
            }
            batch.Counts[key]++;
        }

        /// <summary>
        /// Ends a batch and outputs aggregated counts.
        /// </summary>
        public void EndBatch(string className)
        {
            if (!_activeBatches.TryGetValue(className, out var batch))
            {
                return;
            }

            foreach (var kvp in batch.Counts)
            {
                var parts = kvp.Key.Split(':');
                if (parts.Length == 2)
                {
                    var category = Enum.Parse<DebugCategory>(parts[0]);
                    var eventName = parts[1];

                    string message = kvp.Value > 1
                        ? $"{eventName} (x{kvp.Value})"
                        : eventName;

                    OutputMessage(className, category, message);
                }
            }

            _activeBatches.Remove(className);
        }
        #endregion

        #region Core Logging API
        /// <summary>
        /// Main logging method. Checks if the message should be output based on class config.
        /// </summary>
        public void Log(string className, DebugCategory category, string message)
        {
            if (!ShouldLog(className, category))
            {
                //GD.Print(className + " " + category.ToString());
                return;  
            } 

            if (_enableMessageAggregation && !IsDetailCategory(category))
            {
                // Aggregate this message
                var key = new MessageKey
                {
                    ClassName = className,
                    Category = category,
                    Message = message
                };

                if (!_aggregatedMessages.TryGetValue(key, out var agg))
                {
                    agg = new AggregatedMessage();
                    agg.FirstLogTime = _currentTime; // Set first log time
                    _aggregatedMessages[key] = agg;
                }

                agg.Count++;
                agg.LastLogTime = _currentTime;
            }
            else
            {
                // Output immediately
                OutputMessage(className, category, message);
            }
        }

        /// <summary>
        /// Logs a warning message. Respects AlwaysReportWarnings setting.
        /// </summary>
        public void LogWarning(string className, string message)
        {
            if (!_alwaysReportWarnings && !ShouldLog(className, DebugCategory.Validation))
            {
                return;
            }

            GD.PushWarning($"[{className} Warning] {message}");
        }

        /// <summary>
        /// Logs an error message. Respects AlwaysReportErrors setting.
        /// </summary>
        public void LogError(string className, string message)
        {
            if (!_alwaysReportErrors && !ShouldLog(className, DebugCategory.Validation))
            {
                return;
            }

            GD.PrintErr($"[{className} Error] {message}");
        }
        #endregion

        #region Internal Helpers
        private bool ShouldLog(string className, DebugCategory category)
        {
            // Check if class has a config
            if (!_classConfigs.TryGetValue(className, out var config))
            {
                //GD.Print("No config = no output");
                return false; // No config = no output
            }

            // Check if class is enabled and category is enabled
            //GD.Print(category.ToString());
            return config.IsCategoryEnabled(category);
        }

        private bool IsDetailCategory(DebugCategory category)
        {
            // Categories that should never be aggregated because they contain unique data
            return category == DebugCategory.LayerDetails ||
                   category == DebugCategory.RegionDetails ||
                   category == DebugCategory.TaskDependencies ||
                    category == DebugCategory.RegionDependencies ||
                   category == DebugCategory.RegionLifecycle ||
                   category == DebugCategory.TerrainPush ||
                   category == DebugCategory.ShaderOperations;
        }

        private void OutputMessage(string className, DebugCategory category, string message)
        {
            GD.Print($"[{className} {category}] {message}");
        }
        #endregion

        #region Status & Diagnostics
        /// <summary>
        /// Gets a summary of current debug configuration.
        /// </summary>
        public string GetStatusSummary()
        {
            int enabledClasses = _classConfigs.Count(kvp => kvp.Value.Enabled);
            int pendingAggregated = _aggregatedMessages.Sum(kvp => kvp.Value.Count);

            return $"Debug Manager Status:\n" +
                   $"  Registered Classes: {_registeredClasses.Count}\n" +
                   $"  Enabled Classes: {enabledClasses}\n" +
                   $"  Pending Aggregated Messages: {pendingAggregated}\n" +
                   $"  Always Report Errors: {_alwaysReportErrors}\n" +
                   $"  Always Report Warnings: {_alwaysReportWarnings}\n" +
                   $"  Message Aggregation: {_enableMessageAggregation}";
        }
        #endregion

        #region Performance Timing
        private class PerformanceTimer
        {
            public string TaskName;
            public DebugCategory Category;
            public double StartTime;
        }

        private readonly System.Collections.Generic.Dictionary<string, PerformanceTimer> _activeTimers = new();

        /// <summary>
        /// Starts a performance timer for a named operation.
        /// Usage: DebugManager.Instance?.StartTimer(className, category, "ErosionMask.GlobalCarving");
        /// </summary>
        public void StartTimer(string className, DebugCategory category, string taskName)
        {
            if (!ShouldLog(className, category)) return;

            string timerKey = $"{className}:{taskName}";

            if (_activeTimers.ContainsKey(timerKey))
            {
                LogWarning("DebugManager", $"Timer '{timerKey}' already running - stopping previous timer");
                EndTimer(className, category, taskName);
            }

            _activeTimers[timerKey] = new PerformanceTimer
            {
                TaskName = taskName,
                Category = category,
                StartTime = _currentTime
            };

            if (ShouldLog(className, DebugCategory.PerformanceTiming))
            {
                OutputMessage(className, category, $"⏱️ START: {taskName}");
            }
        }

        /// <summary>
        /// Ends a performance timer and outputs the elapsed time.
        /// Usage: DebugManager.Instance?.EndTimer(className, category, "ErosionMask.GlobalCarving");
        /// </summary>
        public void EndTimer(string className, DebugCategory category, string taskName)
        {
            if (!ShouldLog(className, category)) return;

            string timerKey = $"{className}:{taskName}";

            if (!_activeTimers.TryGetValue(timerKey, out var timer))
            {
                LogWarning("DebugManager", $"Timer '{timerKey}' was not started");
                return;
            }

            double elapsed = _currentTime - timer.StartTime;
            _activeTimers.Remove(timerKey);

            if (ShouldLog(className, DebugCategory.PerformanceTiming))
            {
                OutputMessage(className, category, $"⏱️ END: {taskName} ({elapsed * 1000:F2}ms)");
            }
        }

        /// <summary>
        /// Convenience method for timing a code block with automatic cleanup.
        /// Returns an IDisposable that ends the timer when disposed.
        /// Usage: using (DebugManager.Instance?.TimeScope(className, category, "TaskName")) { ... }
        /// </summary>
        public IDisposable TimeScope(string className, DebugCategory category, string taskName)
        {
            StartTimer(className, category, taskName);
            return new TimerScope(this, className, category, taskName);
        }

        private class TimerScope : IDisposable
        {
            private readonly DebugManager _manager;
            private readonly string _className;
            private readonly DebugCategory _category;
            private readonly string _taskName;
            private bool _disposed;

            public TimerScope(DebugManager manager, string className, DebugCategory category, string taskName)
            {
                _manager = manager;
                _className = className;
                _category = category;
                _taskName = taskName;
            }

            public void Dispose()
            {
                if (!_disposed)
                {
                    _manager.EndTimer(_className, _category, _taskName);
                    _disposed = true;
                }
            }
        }

        /// <summary>
        /// Gets all currently active timers (useful for debugging stuck operations).
        /// </summary>
        public List<string> GetActiveTimers()
        {
            return new List<string>(_activeTimers.Keys);
        }
        #endregion

        #region Mask-Specific Helpers
        /// <summary>
        /// Logs the start of a mask pass with iteration info.
        /// Usage: DebugManager.Instance?.LogMaskPass("ErosionMask", "FlowIteration", iteration, totalIterations);
        /// </summary>
        public void LogMaskPass(string maskName, string passName, int currentIteration, int totalIterations)
        {
            if (!ShouldLog(maskName, DebugCategory.MaskPasses)) return;

            string message = $"{passName}: iteration {currentIteration + 1}/{totalIterations}";

            // Use aggregation for high iteration counts
            if (totalIterations > 10)
            {
                Log(maskName, DebugCategory.MaskPasses, message);
            }
            else
            {
                OutputMessage(maskName, DebugCategory.MaskPasses, message);
            }
        }

        /// <summary>
        /// Logs mask configuration/setup information.
        /// </summary>
        public void LogMaskConfig(string maskName, string configInfo)
        {
            if (!ShouldLog(maskName, DebugCategory.MaskSetup)) return;
            OutputMessage(maskName, DebugCategory.MaskSetup, configInfo);
        }
        #endregion

        /// <summary>
        /// Gets registered classes as a comma-separated string for enum hints.
        /// Used by ClassDebugConfig to populate the dropdown.
        /// </summary>
        public static string GetRegisteredClassesForHint()
        {
            if (Instance == null || Instance._registeredClasses.Count == 0)
            {
                return "None";
            }

            return string.Join(",", Instance._registeredClasses.OrderBy(c => c));
        }

        /// <summary>
        /// Gets registered classes as an array (for other uses).
        /// </summary>
        public static string[] GetRegisteredClassesArray()
        {
            if (Instance == null)
            {
                return System.Array.Empty<string>();
            }

            return Instance._registeredClasses.OrderBy(c => c).ToArray();
        }
    }
}