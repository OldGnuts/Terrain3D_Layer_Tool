// /Layers/Path/Resources/PathPresets.cs
using Godot;

namespace Terrain3DTools.Layers.Path
{
    /// <summary>
    /// Factory class for creating standard path profiles.
    /// Each preset creates a complete, usable profile with sensible defaults.
    /// </summary>
    public static class PathPresets
    {
        #region Road Profiles
        public static PathProfile CreateRoad()
        {
            var profile = new PathProfile { Name = "Road" };
            
            // Center: Flat road surface
            profile.AddZone(new ProfileZone
            {
                Name = "Surface",
                Type = ZoneType.Center,
                Width = 4.0f,
                HeightOffset = 0.15f,
                HeightBlendMode = HeightBlendMode.Replace,
                HeightStrength = 1.0f,
                TerrainConformance = 0.1f,
                TextureId = 5, // Assuming road texture
                TextureStrength = 1.0f,
                HeightCurve = CreateFlatWithCamberCurve(0.02f)
            });
            
            // Shoulder: Gravel/dirt shoulders
            profile.AddZone(new ProfileZone
            {
                Name = "Shoulder",
                Type = ZoneType.Shoulder,
                Width = 1.5f,
                HeightOffset = 0.05f,
                HeightBlendMode = HeightBlendMode.Blend,
                HeightStrength = 0.8f,
                TerrainConformance = 0.3f,
                TextureId = 2, // Gravel texture
                TextureStrength = 0.9f,
                HeightCurve = CreateSlopeDownCurve()
            });
            
            // Edge: Soft transition
            profile.AddZone(new ProfileZone
            {
                Name = "Edge",
                Type = ZoneType.Edge,
                Width = 2.0f,
                HeightOffset = 0.0f,
                HeightBlendMode = HeightBlendMode.Blend,
                HeightStrength = 0.3f,
                TerrainConformance = 0.8f,
                TextureId = -1, // No texture change
                HeightCurve = CreateEaseOutCurve()
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
                TerrainConformance = 0.05f,
                TextureId = 5,
                TextureStrength = 1.0f,
                HeightCurve = CreateFlatWithCamberCurve(0.015f)
            });
            
            profile.AddZone(new ProfileZone
            {
                Name = "Shoulder",
                Type = ZoneType.Shoulder,
                Width = 2.5f,
                HeightOffset = 0.1f,
                HeightBlendMode = HeightBlendMode.Blend,
                HeightStrength = 0.9f,
                TerrainConformance = 0.2f,
                TextureId = 2,
                TextureStrength = 0.95f,
                HeightCurve = CreateSlopeDownCurve()
            });
            
            profile.AddZone(new ProfileZone
            {
                Name = "Embankment",
                Type = ZoneType.Slope,
                Width = 4.0f,
                HeightOffset = -0.5f,
                HeightBlendMode = HeightBlendMode.Blend,
                HeightStrength = 0.6f,
                TerrainConformance = 0.5f,
                TextureId = 1, // Grass
                TextureStrength = 0.7f,
                HeightCurve = CreateSteepSlopeCurve()
            });

            profile.GlobalSmoothingRadius = 2.0f;
            return profile;
        }
        #endregion

        #region River Profiles
        public static PathProfile CreateRiver()
        {
            var profile = new PathProfile { Name = "River" };
            
            // River bed (deepest)
            profile.AddZone(new ProfileZone
            {
                Name = "Riverbed",
                Type = ZoneType.Center,
                Width = 6.0f,
                HeightOffset = -3.0f,
                HeightBlendMode = HeightBlendMode.Replace,
                HeightStrength = 1.0f,
                TerrainConformance = 0.0f,
                TextureId = 12, // River bed texture
                TextureStrength = 1.0f,
                HeightCurve = CreateRiverBedCurve(),
                HeightNoise = new NoiseConfig
                {
                    Enabled = true,
                    Amplitude = 0.3f,
                    Frequency = 0.15f,
                    Octaves = 2
                }
            });
            
            // Banks
            profile.AddZone(new ProfileZone
            {
                Name = "Bank",
                Type = ZoneType.Wall,
                Width = 3.0f,
                HeightOffset = -1.5f,
                HeightBlendMode = HeightBlendMode.Blend,
                HeightStrength = 0.9f,
                TerrainConformance = 0.2f,
                TextureId = 8, // Wet dirt/mud
                TextureStrength = 0.9f,
                HeightCurve = CreateBankCurve()
            });
            
            // Rim (slightly raised edge)
            profile.AddZone(new ProfileZone
            {
                Name = "Rim",
                Type = ZoneType.Rim,
                Width = 1.5f,
                HeightOffset = 0.3f,
                HeightBlendMode = HeightBlendMode.Add,
                HeightStrength = 0.5f,
                TerrainConformance = 0.6f,
                TextureId = 1, // Grass
                TextureStrength = 0.6f,
                HeightCurve = CreateRimCurve()
            });
            
            // Soft transition
            profile.AddZone(new ProfileZone
            {
                Name = "Transition",
                Type = ZoneType.Transition,
                Width = 3.0f,
                HeightOffset = 0.0f,
                HeightBlendMode = HeightBlendMode.Blend,
                HeightStrength = 0.2f,
                TerrainConformance = 0.9f,
                TextureId = -1,
                HeightCurve = CreateEaseOutCurve()
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
                TerrainConformance = 0.3f,
                TextureId = 12,
                TextureStrength = 1.0f,
                HeightCurve = CreateVShapeCurve(),
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
                HeightStrength = 0.7f,
                TerrainConformance = 0.5f,
                TextureId = 8,
                TextureStrength = 0.7f,
                HeightCurve = CreateSlopeUpCurve()
            });
            
            profile.AddZone(new ProfileZone
            {
                Name = "Edge",
                Type = ZoneType.Edge,
                Width = 1.5f,
                HeightOffset = 0.0f,
                HeightBlendMode = HeightBlendMode.Blend,
                HeightStrength = 0.3f,
                TerrainConformance = 0.9f,
                TextureId = -1,
                HeightCurve = CreateEaseOutCurve()
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
                HeightStrength = 0.6f,
                TerrainConformance = 0.7f,
                TextureId = 3, // Dirt path
                TextureStrength = 0.8f,
                HeightCurve = CreateFlatCurve(),
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
                Width = 1.0f,
                HeightOffset = 0.0f,
                HeightBlendMode = HeightBlendMode.Blend,
                HeightStrength = 0.2f,
                TerrainConformance = 0.95f,
                TextureId = -1,
                HeightCurve = CreateEaseOutCurve()
            });

            profile.GlobalSmoothingRadius = 0.5f;
            return profile;
        }
        #endregion

        #region Trench Profiles
        public static PathProfile CreateTrench()
        {
            var profile = new PathProfile { Name = "Trench" };
            
            // Floor
            profile.AddZone(new ProfileZone
            {
                Name = "Floor",
                Type = ZoneType.Center,
                Width = 2.0f,
                HeightOffset = -2.5f,
                HeightBlendMode = HeightBlendMode.Replace,
                HeightStrength = 1.0f,
                TerrainConformance = 0.0f,
                TextureId = 4, // Stone/packed dirt
                TextureStrength = 1.0f,
                HeightCurve = CreateFlatCurve()
            });
            
            // Walls
            profile.AddZone(new ProfileZone
            {
                Name = "Wall",
                Type = ZoneType.Wall,
                Width = 0.5f,
                HeightOffset = -1.25f,
                HeightBlendMode = HeightBlendMode.Replace,
                HeightStrength = 1.0f,
                TerrainConformance = 0.0f,
                TextureId = 4,
                TextureStrength = 0.9f,
                HeightCurve = CreateVerticalWallCurve()
            });
            
            // Rim
            profile.AddZone(new ProfileZone
            {
                Name = "Rim",
                Type = ZoneType.Rim,
                Width = 1.0f,
                HeightOffset = 0.5f,
                HeightBlendMode = HeightBlendMode.Add,
                HeightStrength = 0.8f,
                TerrainConformance = 0.3f,
                TextureId = 2,
                TextureStrength = 0.7f,
                HeightCurve = CreateRimCurve()
            });
            
            profile.AddZone(new ProfileZone
            {
                Name = "Transition",
                Type = ZoneType.Transition,
                Width = 2.0f,
                HeightOffset = 0.0f,
                HeightBlendMode = HeightBlendMode.Blend,
                HeightStrength = 0.3f,
                TerrainConformance = 0.8f,
                TextureId = -1,
                HeightCurve = CreateEaseOutCurve()
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
                TerrainConformance = 0.2f,
                TextureId = 6, // Rock
                TextureStrength = 0.8f,
                HeightCurve = CreateRoundedTopCurve()
            });
            
            profile.AddZone(new ProfileZone
            {
                Name = "Slope",
                Type = ZoneType.Slope,
                Width = 4.0f,
                HeightOffset = 1.0f,
                HeightBlendMode = HeightBlendMode.Add,
                HeightStrength = 0.7f,
                TerrainConformance = 0.4f,
                TextureId = 1,
                TextureStrength = 0.5f,
                HeightCurve = CreateSteepSlopeCurve()
            });
            
            profile.AddZone(new ProfileZone
            {
                Name = "Base",
                Type = ZoneType.Transition,
                Width = 3.0f,
                HeightOffset = 0.0f,
                HeightBlendMode = HeightBlendMode.Blend,
                HeightStrength = 0.3f,
                TerrainConformance = 0.9f,
                TextureId = -1,
                HeightCurve = CreateEaseOutCurve()
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
                TextureId = 12,
                TextureStrength = 1.0f,
                HeightCurve = CreateFlatCurve()
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
                TextureId = 4,
                TextureStrength = 1.0f,
                HeightCurve = CreateVerticalWallCurve()
            });
            
            profile.AddZone(new ProfileZone
            {
                Name = "Towpath",
                Type = ZoneType.Shoulder,
                Width = 2.0f,
                HeightOffset = 0.1f,
                HeightBlendMode = HeightBlendMode.Replace,
                HeightStrength = 0.9f,
                TerrainConformance = 0.1f,
                TextureId = 3,
                TextureStrength = 0.8f,
                HeightCurve = CreateFlatCurve()
            });
            
            profile.AddZone(new ProfileZone
            {
                Name = "Edge",
                Type = ZoneType.Edge,
                Width = 2.0f,
                HeightOffset = 0.0f,
                HeightBlendMode = HeightBlendMode.Blend,
                HeightStrength = 0.3f,
                TerrainConformance = 0.8f,
                TextureId = -1,
                HeightCurve = CreateEaseOutCurve()
            });

            profile.GlobalSmoothingRadius = 0.5f;
            return profile;
        }
        #endregion

        #region Curve Helpers
        private static Curve CreateFlatCurve()
        {
            var curve = new Curve();
            curve.AddPoint(new Vector2(0, 1));
            curve.AddPoint(new Vector2(1, 1));
            return curve;
        }

        private static Curve CreateFlatWithCamberCurve(float camber)
        {
            var curve = new Curve();
            curve.AddPoint(new Vector2(0, 1));
            curve.AddPoint(new Vector2(0.5f, 1 + camber));
            curve.AddPoint(new Vector2(1, 1));
            return curve;
        }

        private static Curve CreateSlopeDownCurve()
        {
            var curve = new Curve();
            curve.AddPoint(new Vector2(0, 1));
            curve.AddPoint(new Vector2(1, 0.3f));
            return curve;
        }

        private static Curve CreateSlopeUpCurve()
        {
            var curve = new Curve();
            curve.AddPoint(new Vector2(0, 0));
            curve.AddPoint(new Vector2(1, 1));
            return curve;
        }

        private static Curve CreateEaseOutCurve()
        {
            var curve = new Curve();
            curve.AddPoint(new Vector2(0, 1), 0, -2);
            curve.AddPoint(new Vector2(1, 0), -0.5f, 0);
            return curve;
        }

        private static Curve CreateSteepSlopeCurve()
        {
            var curve = new Curve();
            curve.AddPoint(new Vector2(0, 1));
            curve.AddPoint(new Vector2(0.3f, 0.5f));
            curve.AddPoint(new Vector2(1, 0));
            return curve;
        }

        private static Curve CreateBankCurve()
        {
            var curve = new Curve();
            curve.AddPoint(new Vector2(0, 0), 0, 2);
            curve.AddPoint(new Vector2(0.7f, 0.8f));
            curve.AddPoint(new Vector2(1, 1));
            return curve;
        }

        private static Curve CreateRimCurve()
        {
            var curve = new Curve();
            curve.AddPoint(new Vector2(0, 0));
            curve.AddPoint(new Vector2(0.3f, 1));
            curve.AddPoint(new Vector2(0.7f, 1));
            curve.AddPoint(new Vector2(1, 0));
            return curve;
        }

        private static Curve CreateRiverBedCurve()
        {
            var curve = new Curve();
            curve.AddPoint(new Vector2(0, 1));
            curve.AddPoint(new Vector2(0.5f, 0.95f)); // Slight V shape
            curve.AddPoint(new Vector2(1, 1));
            return curve;
        }

        private static Curve CreateVShapeCurve()
        {
            var curve = new Curve();
            curve.AddPoint(new Vector2(0, 0.5f));
            curve.AddPoint(new Vector2(0.5f, 1));
            curve.AddPoint(new Vector2(1, 0.5f));
            return curve;
        }

        private static Curve CreateVerticalWallCurve()
        {
            var curve = new Curve();
            curve.AddPoint(new Vector2(0, 0));
            curve.AddPoint(new Vector2(0.1f, 0.9f));
            curve.AddPoint(new Vector2(1, 1));
            return curve;
        }

        private static Curve CreateRoundedTopCurve()
        {
            var curve = new Curve();
            curve.AddPoint(new Vector2(0, 1));
            curve.AddPoint(new Vector2(0.5f, 1.1f));
            curve.AddPoint(new Vector2(1, 1));
            return curve;
        }
        #endregion

        // /Layers/Path/Resources/PathPresets.cs (continued)

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
        /// Create a minimal custom profile for users to build upon.
        /// </summary>
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
                HeightStrength = 0.5f,
                TerrainConformance = 0.5f,
                TextureId = -1,
                HeightCurve = CreateFlatCurve()
            });
            
            profile.AddZone(new ProfileZone
            {
                Name = "Edge",
                Type = ZoneType.Edge,
                Width = 1.0f,
                HeightOffset = 0.0f,
                HeightBlendMode = HeightBlendMode.Blend,
                HeightStrength = 0.2f,
                TerrainConformance = 0.9f,
                TextureId = -1,
                HeightCurve = CreateEaseOutCurve()
            });

            profile.GlobalSmoothingRadius = 1.0f;
            return profile;
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
        #endregion
    }
}