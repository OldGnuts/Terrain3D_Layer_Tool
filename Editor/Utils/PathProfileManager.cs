// /Editor/Utils/PathProfileManager.cs
using System;
using System.Collections.Generic;
using System.IO;
using Godot;
using Terrain3DTools.Layers.Path;

namespace Terrain3DTools.Editor.Utils
{
    /// <summary>
    /// Manages saving, loading, and listing user-created path profiles.
    /// Profiles are stored as .tres Resource files.
    /// </summary>
    public static class PathProfileManager
    {
        #region Constants
        // Store in project folder so profiles are version controlled with the project
        private const string PROFILE_DIRECTORY = "res://addons/terrain_3d_tools/UserProfiles/";
        private const string PROFILE_EXTENSION = ".tres";
        #endregion

        #region Profile Operations

        /// <summary>
        /// Gets the list of all saved user profiles.
        /// </summary>
        public static List<SavedProfileInfo> GetSavedProfiles()
        {
            var profiles = new List<SavedProfileInfo>();

            EnsureDirectoryExists();

            using var dir = DirAccess.Open(PROFILE_DIRECTORY);
            if (dir == null)
            {
                GD.PrintErr($"PathProfileManager: Could not open directory {PROFILE_DIRECTORY}");
                return profiles;
            }

            dir.ListDirBegin();
            string fileName;

            while ((fileName = dir.GetNext()) != "")
            {
                if (dir.CurrentIsDir()) continue;
                if (!fileName.EndsWith(PROFILE_EXTENSION)) continue;

                string fullPath = PROFILE_DIRECTORY + fileName;
                string profileName = fileName.Replace(PROFILE_EXTENSION, "");

                // Try to load and get metadata
                var profile = LoadProfile(fullPath);
                if (profile != null)
                {
                    profiles.Add(new SavedProfileInfo
                    {
                        Name = profile.Name,
                        FileName = fileName,
                        FullPath = fullPath,
                        ZoneCount = profile.Zones?.Count ?? 0,
                        TotalWidth = profile.TotalWidth
                    });
                }
            }

            dir.ListDirEnd();

            // Sort by name
            profiles.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));

            return profiles;
        }

        /// <summary>
        /// Saves a profile to disk.
        /// </summary>
        /// <param name="profile">The profile to save</param>
        /// <param name="profileName">Name for the saved profile (without extension)</param>
        /// <returns>True if save succeeded</returns>
        public static bool SaveProfile(PathProfile profile, string profileName)
        {
            if (profile == null || string.IsNullOrWhiteSpace(profileName))
            {
                GD.PrintErr("PathProfileManager: Cannot save null profile or empty name");
                return false;
            }

            EnsureDirectoryExists();

            // Sanitize filename
            string safeFileName = SanitizeFileName(profileName);
            string fullPath = PROFILE_DIRECTORY + safeFileName + PROFILE_EXTENSION;

            // Create a duplicate to avoid saving references to the original
            var profileToSave = (PathProfile)profile.Duplicate(true);
            profileToSave.Name = profileName;

            // Ensure all nested resources are also duplicated properly
            if (profileToSave.Zones != null)
            {
                for (int i = 0; i < profileToSave.Zones.Count; i++)
                {
                    if (profileToSave.Zones[i] != null)
                    {
                        profileToSave.Zones[i] = (ProfileZone)profileToSave.Zones[i].Duplicate(true);
                    }
                }
            }

            var error = ResourceSaver.Save(profileToSave, fullPath);

            if (error != Error.Ok)
            {
                GD.PrintErr($"PathProfileManager: Failed to save profile to {fullPath}: {error}");
                return false;
            }

            GD.Print($"PathProfileManager: Saved profile '{profileName}' to {fullPath}");
            return true;
        }

        /// <summary>
        /// Loads a profile from disk.
        /// </summary>
        /// <param name="fullPath">Full path to the .tres file</param>
        /// <returns>The loaded profile, or null if failed</returns>
        public static PathProfile LoadProfile(string fullPath)
        {
            if (!ResourceLoader.Exists(fullPath))
            {
                GD.PrintErr($"PathProfileManager: Profile not found at {fullPath}");
                return null;
            }

            var resource = ResourceLoader.Load<PathProfile>(fullPath);

            if (resource == null)
            {
                GD.PrintErr($"PathProfileManager: Failed to load profile from {fullPath}");
                return null;
            }

            // Return a duplicate so modifications don't affect the cached resource
            return (PathProfile)resource.Duplicate(true);
        }

        /// <summary>
        /// Deletes a saved profile.
        /// </summary>
        /// <param name="fullPath">Full path to the .tres file</param>
        /// <returns>True if deletion succeeded</returns>
        public static bool DeleteProfile(string fullPath)
        {
            using var dir = DirAccess.Open(PROFILE_DIRECTORY);
            if (dir == null) return false;

            string fileName = fullPath.GetFile();
            var error = dir.Remove(fileName);

            if (error != Error.Ok)
            {
                GD.PrintErr($"PathProfileManager: Failed to delete profile at {fullPath}: {error}");
                return false;
            }

            GD.Print($"PathProfileManager: Deleted profile at {fullPath}");
            return true;
        }

        /// <summary>
        /// Checks if a profile name already exists.
        /// </summary>
        public static bool ProfileExists(string profileName)
        {
            string safeFileName = SanitizeFileName(profileName);
            string fullPath = PROFILE_DIRECTORY + safeFileName + PROFILE_EXTENSION;
            return ResourceLoader.Exists(fullPath);
        }

        /// <summary>
        /// Creates a deep copy of a profile suitable for editing.
        /// </summary>
        public static PathProfile DuplicateProfile(PathProfile source)
        {
            if (source == null) return null;

            var duplicate = (PathProfile)source.Duplicate(true);

            // Ensure zones are properly duplicated
            if (duplicate.Zones != null)
            {
                var newZones = new Godot.Collections.Array<ProfileZone>();
                foreach (var zone in duplicate.Zones)
                {
                    if (zone != null)
                    {
                        newZones.Add((ProfileZone)zone.Duplicate(true));
                    }
                }
                duplicate.Zones = newZones;
            }

            return duplicate;
        }

        #endregion

        #region Helpers

        private static void EnsureDirectoryExists()
        {
            if (!DirAccess.DirExistsAbsolute(PROFILE_DIRECTORY))
            {
                var error = DirAccess.MakeDirRecursiveAbsolute(PROFILE_DIRECTORY);
                if (error != Error.Ok)
                {
                    GD.PrintErr($"PathProfileManager: Failed to create directory {PROFILE_DIRECTORY}: {error}");
                }
                else
                {
                    GD.Print($"PathProfileManager: Created profile directory at {PROFILE_DIRECTORY}");
                }
            }
        }

        private static string SanitizeFileName(string name)
        {
            // Remove invalid filename characters
            char[] invalid = Path.GetInvalidFileNameChars();
            string result = name;

            foreach (char c in invalid)
            {
                result = result.Replace(c, '_');
            }

            // Also replace spaces for cleaner filenames
            result = result.Replace(' ', '_');

            // Ensure not empty
            if (string.IsNullOrWhiteSpace(result))
            {
                result = "Unnamed_Profile";
            }

            return result;
        }

        #endregion
    }

    /// <summary>
    /// Information about a saved profile for display in the browser.
    /// </summary>
    public class SavedProfileInfo
    {
        public string Name { get; set; }
        public string FileName { get; set; }
        public string FullPath { get; set; }
        public int ZoneCount { get; set; }
        public float TotalWidth { get; set; }

        public string GetDisplayText()
        {
            return $"{Name} ({ZoneCount} zones, {TotalWidth:F1}m wide)";
        }
    }
}