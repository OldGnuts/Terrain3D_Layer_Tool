// /Settings/PathToolSettingsManager.cs
using Godot;
using System;
using Terrain3DTools.Layers.Path;

namespace Terrain3DTools.Settings
{
    /// <summary>
    /// Manages loading, saving, and accessing PathToolsSettings.
    /// Provides a singleton-like access pattern for the current settings.
    /// </summary>
    public static class PathToolsSettingsManager
    {
        #region Constants
        private const string SETTINGS_DIRECTORY = "res://addons/terrain_3d_tools/Settings/";
        private const string SETTINGS_FILENAME = "path_tools_settings.tres";
        #endregion

        #region State
        private static PathToolSettings _currentSettings;
        private static bool _isLoaded = false;
        #endregion

        #region Properties
        /// <summary>
        /// Gets the current settings, loading from disk if necessary.
        /// </summary>
        public static PathToolSettings Current
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
        public static PathToolSettings Load()
        {
            string fullPath = SETTINGS_DIRECTORY + SETTINGS_FILENAME;

            EnsureDirectoryExists();

            if (ResourceLoader.Exists(fullPath))
            {
                try
                {
                    var resource = ResourceLoader.Load(fullPath);
                    
                    // Check if it's the correct type
                    if (resource is PathToolSettings loaded)
                    {
                        _currentSettings = loaded;
                        _isLoaded = true;
                        GD.Print($"[PathToolsSettingsManager] Loaded settings from {fullPath}");
                        return _currentSettings;
                    }
                    else
                    {
                        // Resource exists but is wrong type - delete and recreate
                        GD.PrintErr($"[PathToolsSettingsManager] Settings file exists but is wrong type. Recreating...");
                        DeleteSettingsFile(fullPath);
                    }
                }
                catch (Exception e)
                {
                    GD.PrintErr($"[PathToolsSettingsManager] Failed to load settings: {e.Message}");
                    // Try to delete corrupted file
                    DeleteSettingsFile(fullPath);
                }
            }

            // Create default settings if load failed or file doesn't exist
            _currentSettings = PathToolSettings.CreateDefault();
            _isLoaded = true;
            GD.Print("[PathToolsSettingsManager] Created default settings");

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
                        GD.Print($"[PathToolsSettingsManager] Deleted old settings file: {fullPath}");
                    }
                }
            }
            catch (Exception e)
            {
                GD.PrintErr($"[PathToolsSettingsManager] Failed to delete settings file: {e.Message}");
            }
        }

        /// <summary>
        /// Saves the current settings to disk.
        /// </summary>
        public static bool Save()
        {
            if (_currentSettings == null)
            {
                GD.PrintErr("[PathToolsSettingsManager] Cannot save - no settings loaded");
                return false;
            }

            EnsureDirectoryExists();

            string fullPath = SETTINGS_DIRECTORY + SETTINGS_FILENAME;

            var error = ResourceSaver.Save(_currentSettings, fullPath);
            if (error != Error.Ok)
            {
                GD.PrintErr($"[PathToolsSettingsManager] Failed to save settings to {fullPath}: {error}");
                return false;
            }

            GD.Print($"[PathToolsSettingsManager] Saved settings to {fullPath}");
            return true;
        }

        /// <summary>
        /// Reloads settings from disk, discarding any unsaved changes.
        /// </summary>
        public static PathToolSettings Reload()
        {
            _isLoaded = false;
            _currentSettings = null;
            return Load();
        }

        /// <summary>
        /// Resets settings to defaults and saves.
        /// </summary>
        public static PathToolSettings ResetToDefaults()
        {
            _currentSettings = PathToolSettings.CreateDefault();
            _isLoaded = true;
            Save();
            GD.Print("[PathToolsSettingsManager] Reset settings to defaults");
            return _currentSettings;
        }
        #endregion

        #region Settings Application
        /// <summary>
        /// Applies the current texture settings to a new profile.
        /// Call this when creating new PathLayer instances.
        /// </summary>
        public static void ApplyTextureDefaults(PathProfile profile, PathType pathType)
        {
            if (profile == null || !IsLoaded) return;

            foreach (var zone in profile.GetAllZones())
            {
                if (zone == null) continue;

                // Check for path-type-specific override first
                int textureId = Current.GetTextureForPathAndZone(pathType, zone.Type);

                // Only apply if we have a valid texture set (not -1 which means "use preset default")
                // Actually, we should allow -1 to override to "none"
                // But we need a way to distinguish "no override" from "override to none"
                // For now, only apply if there's an explicit override
                if (Current.HasTextureOverride(pathType, zone.Type))
                {
                    zone.TextureId = textureId;
                }
                else
                {
                    // Check zone-type default
                    int zoneDefault = Current.GetDefaultTextureForZone(zone.Type);
                    if (zoneDefault != -1)
                    {
                        zone.TextureId = zoneDefault;
                    }
                    // Otherwise, keep the preset's default texture
                }
            }
        }

        /// <summary>
        /// Applies the current grade settings to a PathLayer based on its type.
        /// Call this when creating new PathLayer instances.
        /// </summary>
        public static void ApplyGradeDefaults(PathType pathType, out float maxGrade, out bool enableConstraint)
        {
            maxGrade = Current.GetDefaultMaxGrade(pathType);
            enableConstraint = PathPresets.GetDefaultGradeConstraintEnabled(pathType);
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
                    GD.PrintErr($"[PathToolsSettingsManager] Failed to create directory {SETTINGS_DIRECTORY}: {error}");
                }
                else
                {
                    GD.Print($"[PathToolsSettingsManager] Created settings directory at {SETTINGS_DIRECTORY}");
                }
            }
        }
        #endregion
    }
}