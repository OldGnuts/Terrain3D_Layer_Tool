// /Settings/GlobalToolSettingsManager.cs
using Godot;
using System;
using Terrain3DTools.Core;
using Terrain3DTools.Core.Debug;

namespace Terrain3DTools.Settings
{
    /// <summary>
    /// Manages loading, saving, and accessing GlobalToolSettings.
    /// Provides singleton-like access pattern for the current settings.
    /// All consumers should read from Current and call the appropriate notification method after modifications.
    /// </summary>
    public static class GlobalToolSettingsManager
    {
        #region Constants
        private const string SETTINGS_DIRECTORY = "res://addons/terrain_3d_tools/Settings/";
        private const string SETTINGS_FILENAME = "global_settings.tres";
        #endregion

        #region Events
        /// <summary>
        /// Fired when any setting changes. Consumers should subscribe to update their state.
        /// </summary>
        public static event Action SettingsChanged;
        #endregion

        #region State
        private static GlobalToolSettings _currentSettings;
        private static bool _isLoaded = false;
        #endregion

        #region Properties
        /// <summary>
        /// Gets the current settings, loading from disk if necessary.
        /// </summary>
        public static GlobalToolSettings Current
        {
            get
            {
                if (!_isLoaded || _currentSettings == null)
                {
                    Load();
                }
                return _currentSettings;
            }
        }

        /// <summary>
        /// Returns true if settings have been loaded.
        /// </summary>
        public static bool IsLoaded => _isLoaded && _currentSettings != null;
        #endregion

        #region Load/Save Operations
        /// <summary>
        /// Loads settings from disk. Creates default settings if file doesn't exist or is corrupted.
        /// </summary>
        public static GlobalToolSettings Load()
        {
            string fullPath = SETTINGS_DIRECTORY + SETTINGS_FILENAME;

            EnsureDirectoryExists();

            if (ResourceLoader.Exists(fullPath))
            {
                try
                {
                    var resource = ResourceLoader.Load(fullPath);
                    
                    // Check if it's the correct type
                    if (resource is GlobalToolSettings loaded)
                    {
                        _currentSettings = loaded;
                        _isLoaded = true;
                        GD.Print($"[GlobalToolSettingsManager] Loaded settings from {fullPath}");
                        // Apply debug settings silently on load
                        ApplyToDebugManager(silent: true);
                        return _currentSettings;
                    }
                    else
                    {
                        // Resource exists but is wrong type - delete and recreate
                        GD.PrintErr($"[GlobalToolSettingsManager] Settings file exists but is wrong type. Recreating...");
                        DeleteSettingsFile(fullPath);
                    }
                }
                catch (Exception e)
                {
                    GD.PrintErr($"[GlobalToolSettingsManager] Failed to load settings: {e.Message}");
                    // Try to delete corrupted file
                    DeleteSettingsFile(fullPath);
                }
            }

            // Create default settings if load failed or file doesn't exist
            _currentSettings = GlobalToolSettings.CreateDefault();
            _isLoaded = true;
            GD.Print("[GlobalToolSettingsManager] Created default settings");

            // Save the default settings so the file exists for future loads
            Save();

            return _currentSettings;
        }

        /// <summary>
        /// Attempts to delete a settings file that may be corrupted or wrong type.
        /// </summary>
        private static void DeleteSettingsFile(string fullPath)
        {
            try
            {
                using var dir = DirAccess.Open(SETTINGS_DIRECTORY);
                if (dir != null)
                {
                    string fileName = fullPath.GetFile();
                    var error = dir.Remove(fileName);
                    if (error == Error.Ok)
                    {
                        GD.Print($"[GlobalToolSettingsManager] Deleted old settings file: {fullPath}");
                    }
                }
            }
            catch (Exception e)
            {
                GD.PrintErr($"[GlobalToolSettingsManager] Failed to delete settings file: {e.Message}");
            }
        }

        /// <summary>
        /// Saves the current settings to disk.
        /// </summary>
        public static bool Save()
        {
            if (_currentSettings == null)
            {
                GD.PrintErr("[GlobalToolSettingsManager] Cannot save - no settings loaded");
                return false;
            }

            EnsureDirectoryExists();

            string fullPath = SETTINGS_DIRECTORY + SETTINGS_FILENAME;

            var error = ResourceSaver.Save(_currentSettings, fullPath);
            if (error != Error.Ok)
            {
                GD.PrintErr($"[GlobalToolSettingsManager] Failed to save settings to {fullPath}: {error}");
                return false;
            }

            GD.Print($"[GlobalToolSettingsManager] Saved settings to {fullPath}");
            return true;
        }

        /// <summary>
        /// Reloads settings from disk, discarding any unsaved changes.
        /// </summary>
        public static GlobalToolSettings Reload()
        {
            _isLoaded = false;
            _currentSettings = null;
            var settings = Load();
            // Load() already applies debug settings silently, just refresh visualizer and notify
            RefreshActiveVisualizer();
            SettingsChanged?.Invoke();
            return settings;
        }

        /// <summary>
        /// Resets settings to defaults and saves.
        /// </summary>
        public static GlobalToolSettings ResetToDefaults()
        {
            _currentSettings = GlobalToolSettings.CreateDefault();
            _isLoaded = true;
            Save();
            // Full reset - apply debug settings with logging since this is an explicit user action
            ApplyToDebugManager(silent: false);
            RefreshActiveVisualizer();
            SettingsChanged?.Invoke();
            GD.Print("[GlobalToolSettingsManager] Reset settings to defaults");
            return _currentSettings;
        }
        #endregion

        #region Change Notification
        /// <summary>
        /// Call this after modifying general settings (visualization, timing, etc.).
        /// Does NOT re-apply debug settings - use NotifyDebugSettingsChanged() for that.
        /// </summary>
        public static void NotifySettingsChanged()
        {
            RefreshActiveVisualizer();
            SettingsChanged?.Invoke();
        }

        /// <summary>
        /// Call this specifically when debug settings change.
        /// Applies debug settings to DebugManager (with logging) and notifies consumers.
        /// </summary>
        public static void NotifyDebugSettingsChanged()
        {
            ApplyToDebugManager(silent: false);
            SettingsChanged?.Invoke();
        }

        /// <summary>
        /// Applies current debug settings to the DebugManager singleton.
        /// </summary>
        /// <param name="silent">If true, suppresses verbose logging in DebugManager</param>
        private static void ApplyToDebugManager(bool silent = false)
        {
            if (_currentSettings == null || DebugManager.Instance == null) return;

            DebugManager.Instance.AlwaysReportErrors = _currentSettings.AlwaysReportErrors;
            DebugManager.Instance.AlwaysReportWarnings = _currentSettings.AlwaysReportWarnings;
            DebugManager.Instance.EnableMessageAggregation = _currentSettings.EnableMessageAggregation;
            DebugManager.Instance.AggregationWindowSeconds = _currentSettings.AggregationWindowSeconds;
            DebugManager.Instance.UpdateFromConfigArray(_currentSettings.ActiveDebugClasses, silent);
        }

        /// <summary>
        /// Finds and refreshes the visualizer for the currently selected layer.
        /// </summary>
        private static void RefreshActiveVisualizer()
        {
            try
            {
                var manager = FindTerrainLayerManager();
                if (manager == null) return;

                var selectedLayer = manager.GetSelectedLayer();
                if (selectedLayer == null || !GodotObject.IsInstanceValid(selectedLayer)) return;

                var visualizer = selectedLayer.GetNodeOrNull("Visualizer");
                if (visualizer != null && visualizer.HasMethod("Update"))
                {
                    visualizer.Call("Update");
                }
            }
            catch (Exception)
            {
                // Silently handle - this is a nice-to-have, not critical
            }
        }

        /// <summary>
        /// Attempts to find the TerrainLayerManager in the scene.
        /// </summary>
        private static TerrainLayerManager FindTerrainLayerManager()
        {
            // Try via group first (most reliable)
            var tree = Engine.GetMainLoop() as SceneTree;
            if (tree != null)
            {
                var managers = tree.GetNodesInGroup("terrain_layer_manager");
                if (managers.Count > 0 && managers[0] is TerrainLayerManager manager)
                {
                    return manager;
                }
            }

            return null;
        }
        #endregion

        #region Helpers
        private static void EnsureDirectoryExists()
        {
            if (!DirAccess.DirExistsAbsolute(SETTINGS_DIRECTORY))
            {
                var error = DirAccess.MakeDirRecursiveAbsolute(SETTINGS_DIRECTORY);
                if (error != Error.Ok)
                {
                    GD.PrintErr($"[GlobalToolSettingsManager] Failed to create directory {SETTINGS_DIRECTORY}: {error}");
                }
                else
                {
                    GD.Print($"[GlobalToolSettingsManager] Created settings directory at {SETTINGS_DIRECTORY}");
                }
            }
        }
        #endregion

        #region Convenience Accessors
        /// <summary>
        /// Gets blend smoothing enabled state.
        /// </summary>
        public static bool BlendSmoothingEnabled => Current?.EnableBlendSmoothing ?? true;

        /// <summary>
        /// Gets the world height scale.
        /// </summary>
        public static float WorldHeightScale => Current?.WorldHeightScale ?? 128f;
        #endregion
    }
}