// /Settings/PathToolSettings.cs
using Godot;
using Godot.Collections;
using System.Collections.Generic;
using Terrain3DTools.Layers.Path;

namespace Terrain3DTools.Settings
{
    /// <summary>
    /// Persistent settings for the Path Tools system.
    /// Stores user preferences for zone textures, default grades, and other global settings.
    /// </summary>
    [GlobalClass, Tool]
    public partial class PathToolSettings : Resource
    {
        #region Constants
        public const string SETTINGS_PATH = "res://addons/terrain_3d_tools/Settings/path_tools_settings.tres";
        #endregion

        #region Zone Texture Defaults
        /// <summary>
        /// Default texture ID for Center zones (-1 = no texture).
        /// </summary>
        [ExportGroup("Zone Texture Defaults")]
        [Export(PropertyHint.Range, "-1,31,1")]
        public int DefaultTextureCener { get; set; } = -1;

        /// <summary>
        /// Default texture ID for Inner zones.
        /// </summary>
        [Export(PropertyHint.Range, "-1,31,1")]
        public int DefaultTextureInner { get; set; } = -1;

        /// <summary>
        /// Default texture ID for Shoulder zones.
        /// </summary>
        [Export(PropertyHint.Range, "-1,31,1")]
        public int DefaultTextureShoulder { get; set; } = -1;

        /// <summary>
        /// Default texture ID for Edge zones.
        /// </summary>
        [Export(PropertyHint.Range, "-1,31,1")]
        public int DefaultTextureEdge { get; set; } = -1;

        /// <summary>
        /// Default texture ID for Wall zones.
        /// </summary>
        [Export(PropertyHint.Range, "-1,31,1")]
        public int DefaultTextureWall { get; set; } = -1;

        /// <summary>
        /// Default texture ID for Rim zones.
        /// </summary>
        [Export(PropertyHint.Range, "-1,31,1")]
        public int DefaultTextureRim { get; set; } = -1;

        /// <summary>
        /// Default texture ID for Slope zones.
        /// </summary>
        [Export(PropertyHint.Range, "-1,31,1")]
        public int DefaultTextureSlope { get; set; } = -1;

        /// <summary>
        /// Default texture ID for Transition zones.
        /// </summary>
        [Export(PropertyHint.Range, "-1,31,1")]
        public int DefaultTextureTransition { get; set; } = -1;
        #endregion

        #region Path Type Texture Overrides
        /// <summary>
        /// Per-path-type texture overrides. Key format: "{PathType}_{ZoneType}"
        /// </summary>
        [ExportGroup("Path Type Texture Overrides")]
        [Export]
        public Godot.Collections.Dictionary<string, int> PathTypeTextureOverrides { get; set; } = new();
        #endregion

        #region Grade Defaults
        /// <summary>
        /// Default maximum grade for roads (percentage).
        /// </summary>
        [ExportGroup("Grade Defaults")]
        [Export(PropertyHint.Range, "1.0,20.0,0.5")]
        public float DefaultRoadMaxGrade { get; set; } = 8.0f;

        /// <summary>
        /// Default maximum grade for trails (percentage).
        /// </summary>
        [Export(PropertyHint.Range, "1.0,30.0,0.5")]
        public float DefaultTrailMaxGrade { get; set; } = 15.0f;

        /// <summary>
        /// Default maximum grade for railways (percentage).
        /// </summary>
        [Export(PropertyHint.Range, "0.5,10.0,0.5")]
        public float DefaultRailwayMaxGrade { get; set; } = 3.0f;
        #endregion

        #region Global Settings
        /// <summary>
        /// Placeholder for future global settings from TerrainLayerManager.
        /// </summary>
        [ExportGroup("Global Settings")]
        [Export]
        public bool PlaceholderGlobalSetting { get; set; } = false;
        #endregion

        #region Global Texturing
        /// <summary>
        /// Placeholder for future global texturing settings.
        /// </summary>
        [ExportGroup("Global Texturing")]
        [Export]
        public bool PlaceholderTexturingSetting { get; set; } = false;
        #endregion

        #region Texture Lookup Methods
        /// <summary>
        /// Gets the default texture ID for a zone type.
        /// </summary>
        public int GetDefaultTextureForZone(ZoneType zoneType)
        {
            return zoneType switch
            {
                ZoneType.Center => DefaultTextureCener,
                ZoneType.Inner => DefaultTextureInner,
                ZoneType.Shoulder => DefaultTextureShoulder,
                ZoneType.Edge => DefaultTextureEdge,
                ZoneType.Wall => DefaultTextureWall,
                ZoneType.Rim => DefaultTextureRim,
                ZoneType.Slope => DefaultTextureSlope,
                ZoneType.Transition => DefaultTextureTransition,
                _ => -1
            };
        }

        /// <summary>
        /// Sets the default texture ID for a zone type.
        /// </summary>
        public void SetDefaultTextureForZone(ZoneType zoneType, int textureId)
        {
            switch (zoneType)
            {
                case ZoneType.Center: DefaultTextureCener = textureId; break;
                case ZoneType.Inner: DefaultTextureInner = textureId; break;
                case ZoneType.Shoulder: DefaultTextureShoulder = textureId; break;
                case ZoneType.Edge: DefaultTextureEdge = textureId; break;
                case ZoneType.Wall: DefaultTextureWall = textureId; break;
                case ZoneType.Rim: DefaultTextureRim = textureId; break;
                case ZoneType.Slope: DefaultTextureSlope = textureId; break;
                case ZoneType.Transition: DefaultTextureTransition = textureId; break;
            }
            EmitChanged();
        }

        /// <summary>
        /// Gets the texture ID for a specific path type and zone type combination.
        /// Falls back to zone default if no override exists.
        /// </summary>
        public int GetTextureForPathAndZone(PathType pathType, ZoneType zoneType)
        {
            string key = $"{pathType}_{zoneType}";
            if (PathTypeTextureOverrides.TryGetValue(key, out int textureId))
            {
                return textureId;
            }
            return GetDefaultTextureForZone(zoneType);
        }

        /// <summary>
        /// Sets a texture override for a specific path type and zone type combination.
        /// </summary>
        public void SetTextureForPathAndZone(PathType pathType, ZoneType zoneType, int textureId)
        {
            string key = $"{pathType}_{zoneType}";
            PathTypeTextureOverrides[key] = textureId;
            EmitChanged();
        }

        /// <summary>
        /// Clears the texture override for a specific path type and zone type combination.
        /// </summary>
        public void ClearTextureOverride(PathType pathType, ZoneType zoneType)
        {
            string key = $"{pathType}_{zoneType}";
            if (PathTypeTextureOverrides.ContainsKey(key))
            {
                PathTypeTextureOverrides.Remove(key);
                EmitChanged();
            }
        }

        /// <summary>
        /// Checks if there's a texture override for a specific path type and zone type.
        /// </summary>
        public bool HasTextureOverride(PathType pathType, ZoneType zoneType)
        {
            string key = $"{pathType}_{zoneType}";
            return PathTypeTextureOverrides.ContainsKey(key);
        }
        #endregion

        #region Grade Lookup Methods
        /// <summary>
        /// Gets the default maximum grade for a path type.
        /// </summary>
        public float GetDefaultMaxGrade(PathType pathType)
        {
            return pathType switch
            {
                PathType.Road => DefaultRoadMaxGrade,
                PathType.Trail => DefaultTrailMaxGrade,
                PathType.Railway => DefaultRailwayMaxGrade,
                _ => 8.0f
            };
        }

        /// <summary>
        /// Sets the default maximum grade for a path type.
        /// </summary>
        public void SetDefaultMaxGrade(PathType pathType, float grade)
        {
            switch (pathType)
            {
                case PathType.Road: DefaultRoadMaxGrade = grade; break;
                case PathType.Trail: DefaultTrailMaxGrade = grade; break;
                case PathType.Railway: DefaultRailwayMaxGrade = grade; break;
            }
            EmitChanged();
        }
        #endregion

        #region Factory
        /// <summary>
        /// Creates a new settings instance with default values.
        /// </summary>
        public static PathToolSettings CreateDefault()
        {
            return new PathToolSettings();
        }
        #endregion
    }
}