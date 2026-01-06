using Godot;
using Terrain3DTools.Utils;

namespace Terrain3DTools.Layers.Path
{
    /// <summary>
    /// Factory class for creating standard path profiles.
    /// Each preset creates a complete, usable profile with sensible defaults.
    /// 
    /// Note: Uses CurveUtils for all curve creation to avoid duplication.
    /// Texture IDs are configurable via constants - adjust for your terrain setup.
    /// </summary>
    public static class PathPresets
    {
        #region Texture ID Constants
        // Adjust these to match your Terrain3D texture setup
        public const int TEXTURE_ROAD = 5;
        public const int TEXTURE_GRAVEL = 2;
        public const int TEXTURE_GRASS = 1;
        public const int TEXTURE_DIRT = 3;
        public const int TEXTURE_STONE = 4;
        public const int TEXTURE_ROCK = 6;
        public const int TEXTURE_MUD = 8;
        public const int TEXTURE_RIVERBED = 12;
        public const int TEXTURE_BALLAST = 2;  // Railway ballast (gravel)
        public const int TEXTURE_NONE = -1;
        #endregion

        #region Default Grade Constants
        /// <summary>Default maximum grade for roads (8%)</summary>
        public const float DEFAULT_ROAD_MAX_GRADE = 8.0f;
        /// <summary>Default maximum grade for highways (6%)</summary>
        public const float DEFAULT_HIGHWAY_MAX_GRADE = 6.0f;
        /// <summary>Default maximum grade for trails (15%)</summary>
        public const float DEFAULT_TRAIL_MAX_GRADE = 15.0f;
        /// <summary>Default maximum grade for railways (3%)</summary>
        public const float DEFAULT_RAILWAY_MAX_GRADE = 3.0f;
        #endregion

        #region Road Profiles
        public static PathProfile CreateRoad()
        {
            var profile = new PathProfile { Name = "Road" };

            profile.AddZone(new ProfileZone
            {
                Name = "Surface",
                Type = ZoneType.Center,
                Width = 4.0f,
                HeightOffset = 0.15f,
                HeightBlendMode = HeightBlendMode.Replace,
                HeightStrength = 1.0f,
                TerrainConformance = 0.0f,
                TextureId = TEXTURE_ROAD,
                TextureStrength = 1.0f,
                HeightCurve = CurveUtils.CreateFlatWithCamberCurve(0.02f)
            });

            profile.AddZone(new ProfileZone
            {
                Name = "Shoulder",
                Type = ZoneType.Shoulder,
                Width = 1.5f,
                HeightOffset = 0.05f,
                HeightBlendMode = HeightBlendMode.Blend,
                HeightStrength = 0.9f,
                TerrainConformance = 0.0f,
                TextureId = TEXTURE_GRAVEL,
                TextureStrength = 0.9f,
                HeightCurve = CurveUtils.CreateSlopeDownCurve()
            });

            profile.AddZone(new ProfileZone
            {
                Name = "Edge",
                Type = ZoneType.Edge,
                Width = 10.0f,  // Was 2.0f - increased 5x
                HeightOffset = 0.0f,
                HeightBlendMode = HeightBlendMode.Blend,
                HeightStrength = 0.7f,  // Increased for wider zone
                TerrainConformance = 0.0f,
                TextureId = TEXTURE_NONE,
                HeightCurve = CurveUtils.CreateEaseOutCurve()
            });

            profile.GlobalSmoothingRadius = 1.5f;
            return profile;
        }

        public static PathProfile CreateHighway()
        {
            var profile = new PathProfile { Name = "Highway" };

            profile.AddZone(new ProfileZone
            {
                Name = "Lanes",
                Type = ZoneType.Center,
                Width = 8.0f,
                HeightOffset = 0.2f,
                HeightBlendMode = HeightBlendMode.Replace,
                HeightStrength = 1.0f,
                TerrainConformance = 0.0f,
                TextureId = TEXTURE_ROAD,
                TextureStrength = 1.0f,
                HeightCurve = CurveUtils.CreateFlatWithCamberCurve(0.015f)
            });

            profile.AddZone(new ProfileZone
            {
                Name = "Shoulder",
                Type = ZoneType.Shoulder,
                Width = 2.5f,
                HeightOffset = 0.1f,
                HeightBlendMode = HeightBlendMode.Blend,
                HeightStrength = 0.9f,
                TerrainConformance = 0.0f,
                TextureId = TEXTURE_GRAVEL,
                TextureStrength = 0.95f,
                HeightCurve = CurveUtils.CreateSlopeDownCurve()
            });

            profile.AddZone(new ProfileZone
            {
                Name = "Embankment",
                Type = ZoneType.Slope,
                Width = 4.0f,
                HeightOffset = -0.5f,
                HeightBlendMode = HeightBlendMode.Blend,
                HeightStrength = 0.9f,
                TerrainConformance = 0.0f,
                TextureId = TEXTURE_GRASS,
                TextureStrength = 0.7f,
                HeightCurve = CurveUtils.CreateSteepSlopeCurve()
            });

            profile.AddZone(new ProfileZone
            {
                Name = "Edge",
                Type = ZoneType.Edge,
                Width = 15.0f,  // Wide transition for highways
                HeightOffset = 0.0f,
                HeightBlendMode = HeightBlendMode.Blend,
                HeightStrength = 0.6f,
                TerrainConformance = 0.0f,
                TextureId = TEXTURE_NONE,
                HeightCurve = CurveUtils.CreateEaseOutCurve()
            });

            profile.GlobalSmoothingRadius = 2.0f;
            return profile;
        }
        #endregion

        #region Railway Profile
        public static PathProfile CreateRailway()
        {
            var profile = new PathProfile { Name = "Railway" };

            // Track bed - flat surface for rails
            profile.AddZone(new ProfileZone
            {
                Name = "Track Bed",
                Type = ZoneType.Center,
                Width = 2.5f,
                HeightOffset = 0.4f,
                HeightBlendMode = HeightBlendMode.Replace,
                HeightStrength = 1.0f,
                TerrainConformance = 0.0f,
                TextureId = TEXTURE_BALLAST,
                TextureStrength = 1.0f,
                HeightCurve = CurveUtils.CreateFlatCurve()
            });

            // Ballast shoulders - raised gravel mounds on sides
            profile.AddZone(new ProfileZone
            {
                Name = "Ballast",
                Type = ZoneType.Shoulder,
                Width = 1.5f,
                HeightOffset = 0.25f,
                HeightBlendMode = HeightBlendMode.Blend,
                HeightStrength = 0.95f,
                TerrainConformance = 0.0f,
                TextureId = TEXTURE_BALLAST,
                TextureStrength = 0.95f,
                HeightCurve = CurveUtils.CreateSlopeDownCurve()
            });

            // Embankment slope
            profile.AddZone(new ProfileZone
            {
                Name = "Embankment",
                Type = ZoneType.Slope,
                Width = 3.0f,
                HeightOffset = -0.2f,
                HeightBlendMode = HeightBlendMode.Blend,
                HeightStrength = 0.9f,
                TerrainConformance = 0.0f,
                TextureId = TEXTURE_GRASS,
                TextureStrength = 0.7f,
                HeightCurve = CurveUtils.CreateSteepSlopeCurve()
            });

            // Wide edge transition
            profile.AddZone(new ProfileZone
            {
                Name = "Edge",
                Type = ZoneType.Edge,
                Width = 12.0f,
                HeightOffset = 0.0f,
                HeightBlendMode = HeightBlendMode.Blend,
                HeightStrength = 0.6f,
                TerrainConformance = 0.0f,
                TextureId = TEXTURE_NONE,
                HeightCurve = CurveUtils.CreateEaseOutCurve()
            });

            profile.GlobalSmoothingRadius = 1.5f;
            return profile;
        }
        #endregion

        #region River Profiles
        public static PathProfile CreateRiver()
        {
            var profile = new PathProfile { Name = "River" };

            profile.AddZone(new ProfileZone
            {
                Name = "Riverbed",
                Type = ZoneType.Center,
                Width = 6.0f,
                HeightOffset = -3.0f,
                HeightBlendMode = HeightBlendMode.Replace,
                HeightStrength = 1.0f,
                TerrainConformance = 0.0f,
                TextureId = TEXTURE_RIVERBED,
                TextureStrength = 1.0f,
                HeightCurve = CurveUtils.CreateRiverBedCurve(),
                HeightNoise = new NoiseConfig
                {
                    Enabled = true,
                    Amplitude = 0.3f,
                    Frequency = 0.15f,
                    Octaves = 2
                }
            });

            profile.AddZone(new ProfileZone
            {
                Name = "Bank",
                Type = ZoneType.Wall,
                Width = 3.0f,
                HeightOffset = -1.5f,
                HeightBlendMode = HeightBlendMode.Blend,
                HeightStrength = 0.9f,
                TerrainConformance = 0.0f,
                TextureId = TEXTURE_MUD,
                TextureStrength = 0.9f,
                HeightCurve = CurveUtils.CreateBankCurve()
            });

            profile.AddZone(new ProfileZone
            {
                Name = "Rim",
                Type = ZoneType.Rim,
                Width = 1.5f,
                HeightOffset = 0.3f,
                HeightBlendMode = HeightBlendMode.Add,
                HeightStrength = 0.7f,
                TerrainConformance = 0.0f,
                TextureId = TEXTURE_GRASS,
                TextureStrength = 0.6f,
                HeightCurve = CurveUtils.CreateRimCurve()
            });

            profile.AddZone(new ProfileZone
            {
                Name = "Transition",
                Type = ZoneType.Transition,
                Width = 15.0f,  // Was 3.0f - increased 5x
                HeightOffset = 0.0f,
                HeightBlendMode = HeightBlendMode.Blend,
                HeightStrength = 0.5f,  // Increased for wider zone
                TerrainConformance = 0.0f,
                TextureId = TEXTURE_NONE,
                HeightCurve = CurveUtils.CreateEaseOutCurve()
            });

            profile.GlobalSmoothingRadius = 2.5f;
            return profile;
        }

        public static PathProfile CreateStream()
        {
            var profile = new PathProfile { Name = "Stream" };

            profile.AddZone(new ProfileZone
            {
                Name = "Channel",
                Type = ZoneType.Center,
                Width = 2.0f,
                HeightOffset = -1.0f,
                HeightBlendMode = HeightBlendMode.Replace,
                HeightStrength = 1.0f,
                TerrainConformance = 0.0f,
                TextureId = TEXTURE_RIVERBED,
                TextureStrength = 1.0f,
                HeightCurve = CurveUtils.CreateVShapeCurve(),
                HeightNoise = new NoiseConfig
                {
                    Enabled = true,
                    Amplitude = 0.15f,
                    Frequency = 0.25f,
                    Octaves = 2
                }
            });

            profile.AddZone(new ProfileZone
            {
                Name = "Bank",
                Type = ZoneType.Shoulder,
                Width = 1.0f,
                HeightOffset = -0.2f,
                HeightBlendMode = HeightBlendMode.Blend,
                HeightStrength = 0.9f,
                TerrainConformance = 0.0f,
                TextureId = TEXTURE_MUD,
                TextureStrength = 0.7f,
                HeightCurve = CurveUtils.CreateSlopeUpCurve()
            });

            profile.AddZone(new ProfileZone
            {
                Name = "Edge",
                Type = ZoneType.Edge,
                Width = 7.5f,  // Was 1.5f - increased 5x
                HeightOffset = 0.0f,
                HeightBlendMode = HeightBlendMode.Blend,
                HeightStrength = 0.6f,  // Increased for wider zone
                TerrainConformance = 0.0f,
                TextureId = TEXTURE_NONE,
                HeightCurve = CurveUtils.CreateEaseOutCurve()
            });

            profile.GlobalSmoothingRadius = 1.0f;
            return profile;
        }
        #endregion

        #region Trail Profiles
        public static PathProfile CreateTrail()
        {
            var profile = new PathProfile { Name = "Trail" };

            profile.AddZone(new ProfileZone
            {
                Name = "Path",
                Type = ZoneType.Center,
                Width = 1.5f,
                HeightOffset = -0.1f,
                HeightBlendMode = HeightBlendMode.Blend,
                HeightStrength = 0.9f,
                TerrainConformance = 0.0f,
                TextureId = TEXTURE_DIRT,
                TextureStrength = 0.8f,
                HeightCurve = CurveUtils.CreateFlatCurve(),
                HeightNoise = new NoiseConfig
                {
                    Enabled = true,
                    Amplitude = 0.05f,
                    Frequency = 0.3f,
                    Octaves = 2
                }
            });

            profile.AddZone(new ProfileZone
            {
                Name = "Edge",
                Type = ZoneType.Edge,
                Width = 5.0f,  // Was 1.0f - increased 5x
                HeightOffset = 0.0f,
                HeightBlendMode = HeightBlendMode.Blend,
                HeightStrength = 0.5f,  // Adjusted for wider zone
                TerrainConformance = 0.0f,
                TextureId = TEXTURE_NONE,
                HeightCurve = CurveUtils.CreateEaseOutCurve()
            });

            profile.GlobalSmoothingRadius = 0.5f;
            return profile;
        }
        #endregion

        #region Trench Profiles
        public static PathProfile CreateTrench()
        {
            var profile = new PathProfile { Name = "Trench" };

            profile.AddZone(new ProfileZone
            {
                Name = "Floor",
                Type = ZoneType.Center,
                Width = 2.0f,
                HeightOffset = -2.5f,
                HeightBlendMode = HeightBlendMode.Replace,
                HeightStrength = 1.0f,
                TerrainConformance = 0.0f,
                TextureId = TEXTURE_STONE,
                TextureStrength = 1.0f,
                HeightCurve = CurveUtils.CreateFlatCurve()
            });

            profile.AddZone(new ProfileZone
            {
                Name = "Wall",
                Type = ZoneType.Wall,
                Width = 0.5f,
                HeightOffset = -1.25f,
                HeightBlendMode = HeightBlendMode.Replace,
                HeightStrength = 1.0f,
                TerrainConformance = 0.0f,
                TextureId = TEXTURE_STONE,
                TextureStrength = 0.9f,
                HeightCurve = CurveUtils.CreateVerticalWallCurve()
            });

            profile.AddZone(new ProfileZone
            {
                Name = "Rim",
                Type = ZoneType.Rim,
                Width = 1.0f,
                HeightOffset = 0.5f,
                HeightBlendMode = HeightBlendMode.Add,
                HeightStrength = 0.9f,
                TerrainConformance = 0.0f,
                TextureId = TEXTURE_GRAVEL,
                TextureStrength = 0.7f,
                HeightCurve = CurveUtils.CreateRimCurve()
            });

            profile.AddZone(new ProfileZone
            {
                Name = "Transition",
                Type = ZoneType.Transition,
                Width = 10.0f,  // Was 2.0f - increased 5x
                HeightOffset = 0.0f,
                HeightBlendMode = HeightBlendMode.Blend,
                HeightStrength = 0.6f,  // Increased for wider zone
                TerrainConformance = 0.0f,
                TextureId = TEXTURE_NONE,
                HeightCurve = CurveUtils.CreateEaseOutCurve()
            });

            profile.GlobalSmoothingRadius = 0.3f;
            return profile;
        }
        #endregion

        #region Ridge Profiles
        public static PathProfile CreateRidge()
        {
            var profile = new PathProfile { Name = "Ridge" };

            profile.AddZone(new ProfileZone
            {
                Name = "Crest",
                Type = ZoneType.Center,
                Width = 2.0f,
                HeightOffset = 2.0f,
                HeightBlendMode = HeightBlendMode.Add,
                HeightStrength = 1.0f,
                TerrainConformance = 0.0f,
                TextureId = TEXTURE_ROCK,
                TextureStrength = 0.8f,
                HeightCurve = CurveUtils.CreateRoundedTopCurve()
            });

            profile.AddZone(new ProfileZone
            {
                Name = "Slope",
                Type = ZoneType.Slope,
                Width = 4.0f,
                HeightOffset = 1.0f,
                HeightBlendMode = HeightBlendMode.Add,
                HeightStrength = 0.9f,
                TerrainConformance = 0.0f,
                TextureId = TEXTURE_GRASS,
                TextureStrength = 0.5f,
                HeightCurve = CurveUtils.CreateSteepSlopeCurve()
            });

            profile.AddZone(new ProfileZone
            {
                Name = "Base",
                Type = ZoneType.Transition,
                Width = 15.0f,  // Was 3.0f - increased 5x
                HeightOffset = 0.0f,
                HeightBlendMode = HeightBlendMode.Blend,
                HeightStrength = 0.6f,  // Increased for wider zone
                TerrainConformance = 0.0f,
                TextureId = TEXTURE_NONE,
                HeightCurve = CurveUtils.CreateEaseOutCurve()
            });

            profile.GlobalSmoothingRadius = 1.5f;
            return profile;
        }
        #endregion

        #region Canal Profiles
        public static PathProfile CreateCanal()
        {
            var profile = new PathProfile { Name = "Canal" };

            profile.AddZone(new ProfileZone
            {
                Name = "Channel",
                Type = ZoneType.Center,
                Width = 4.0f,
                HeightOffset = -2.0f,
                HeightBlendMode = HeightBlendMode.Replace,
                HeightStrength = 1.0f,
                TerrainConformance = 0.0f,
                TextureId = TEXTURE_RIVERBED,
                TextureStrength = 1.0f,
                HeightCurve = CurveUtils.CreateFlatCurve()
            });

            profile.AddZone(new ProfileZone
            {
                Name = "Wall",
                Type = ZoneType.Wall,
                Width = 0.3f,
                HeightOffset = -1.0f,
                HeightBlendMode = HeightBlendMode.Replace,
                HeightStrength = 1.0f,
                TerrainConformance = 0.0f,
                TextureId = TEXTURE_STONE,
                TextureStrength = 1.0f,
                HeightCurve = CurveUtils.CreateVerticalWallCurve()
            });

            profile.AddZone(new ProfileZone
            {
                Name = "Towpath",
                Type = ZoneType.Shoulder,
                Width = 2.0f,
                HeightOffset = 0.1f,
                HeightBlendMode = HeightBlendMode.Replace,
                HeightStrength = 0.95f,
                TerrainConformance = 0.0f,
                TextureId = TEXTURE_DIRT,
                TextureStrength = 0.8f,
                HeightCurve = CurveUtils.CreateFlatCurve()
            });

            profile.AddZone(new ProfileZone
            {
                Name = "Edge",
                Type = ZoneType.Edge,
                Width = 10.0f,  // Was 2.0f - increased 5x
                HeightOffset = 0.0f,
                HeightBlendMode = HeightBlendMode.Blend,
                HeightStrength = 0.6f,  // Increased for wider zone
                TerrainConformance = 0.0f,
                TextureId = TEXTURE_NONE,
                HeightCurve = CurveUtils.CreateEaseOutCurve()
            });

            profile.GlobalSmoothingRadius = 0.5f;
            return profile;
        }
        #endregion

        #region Custom Profile
        public static PathProfile CreateMinimalCustom()
        {
            var profile = new PathProfile { Name = "Custom" };

            profile.AddZone(new ProfileZone
            {
                Name = "Center",
                Type = ZoneType.Center,
                Width = 2.0f,
                HeightOffset = 0.0f,
                HeightBlendMode = HeightBlendMode.Blend,
                HeightStrength = 0.9f,
                TerrainConformance = 0.0f,
                TextureId = TEXTURE_NONE,
                HeightCurve = CurveUtils.CreateFlatCurve()
            });

            profile.AddZone(new ProfileZone
            {
                Name = "Edge",
                Type = ZoneType.Edge,
                Width = 5.0f,  // Was 1.0f - increased 5x
                HeightOffset = 0.0f,
                HeightBlendMode = HeightBlendMode.Blend,
                HeightStrength = 0.5f,  // Adjusted for wider zone
                TerrainConformance = 0.0f,
                TextureId = TEXTURE_NONE,
                HeightCurve = CurveUtils.CreateEaseOutCurve()
            });

            profile.GlobalSmoothingRadius = 1.0f;
            return profile;
        }
        #endregion

        #region Preset Lookup
        /// <summary>
        /// Get the default profile for a path type.
        /// </summary>
        public static PathProfile GetPresetForType(PathType type)
        {
            return type switch
            {
                PathType.Road => CreateRoad(),
                PathType.River => CreateRiver(),
                PathType.Stream => CreateStream(),
                PathType.Trail => CreateTrail(),
                PathType.Trench => CreateTrench(),
                PathType.Ridge => CreateRidge(),
                PathType.Canal => CreateCanal(),
                PathType.Custom => CreateMinimalCustom(),
                _ => CreateTrail()
            };
        }

        /// <summary>
        /// Get display name for a path type.
        /// </summary>
        public static string GetDisplayName(PathType type)
        {
            return type switch
            {
                PathType.Road => "Road",
                PathType.River => "River",
                PathType.Stream => "Stream",
                PathType.Trail => "Trail / Footpath",
                PathType.Trench => "Trench / Ditch",
                PathType.Ridge => "Ridge / Levee",
                PathType.Canal => "Canal",
                PathType.Custom => "Custom Profile",
                _ => type.ToString()
            };
        }

        /// <summary>
        /// Get description for a path type.
        /// </summary>
        public static string GetDescription(PathType type)
        {
            return type switch
            {
                PathType.Road => "Raised flat surface with shoulders. Good for roads, highways, and paved paths.",
                PathType.River => "Deep carved channel with banks. Creates realistic river beds with raised edges.",
                PathType.Stream => "Narrow carved channel that follows terrain more closely. Good for small waterways.",
                PathType.Trail => "Soft path that conforms to terrain. Minimal height modification, mainly texture.",
                PathType.Trench => "Deep channel with steep walls and flat floor. Good for defensive trenches or drainage.",
                PathType.Ridge => "Raised spine of terrain. Good for levees, walls, or natural ridges.",
                PathType.Canal => "Flat-bottomed carved channel with towpaths. Structured water channel.",
                PathType.Custom => "Start from scratch with a minimal profile you can customize.",
                _ => ""
            };
        }

        /// <summary>
        /// Get the default maximum grade percentage for a path type.
        /// Returns 0 for types that don't use grade constraints.
        /// </summary>
        public static float GetDefaultMaxGrade(PathType type)
        {
            return type switch
            {
                PathType.Road => DEFAULT_ROAD_MAX_GRADE,
                PathType.Trail => DEFAULT_TRAIL_MAX_GRADE,
                _ => 0f  // Other types don't use grade constraints by default
            };
        }

        /// <summary>
        /// Determines if a path type should have grade constraints enabled by default.
        /// </summary>
        public static bool GetDefaultGradeConstraintEnabled(PathType type)
        {
            return type switch
            {
                PathType.Road => true,
                PathType.Trail => true,
                _ => false
            };
        }

        /// <summary>
        /// Determines if a path type should have downhill-only constraint enabled by default.
        /// Used for water features that must flow downhill.
        /// </summary>
        public static bool GetDefaultDownhillConstraintEnabled(PathType type)
        {
            return type switch
            {
                PathType.River => true,
                PathType.Stream => true,
                PathType.Canal => true,
                _ => false
            };
        }
        #endregion

        #region Settings Integration
        /// <summary>
        /// Gets a preset profile with user-defined texture defaults applied.
        /// Use this instead of GetPresetForType when creating new PathLayers.
        /// </summary>
        public static PathProfile GetPresetWithUserDefaults(PathType type)
        {
            var profile = GetPresetForType(type);

            // Apply user texture defaults if settings are available
            if (Settings.PathToolsSettingsManager.IsLoaded)
            {
                Settings.PathToolsSettingsManager.ApplyTextureDefaults(profile, type);
            }

            return profile;
        }
        #endregion
    }
}