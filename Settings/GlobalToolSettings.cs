// /Settings/GlobalToolSettings.cs
using Godot;
using Godot.Collections;
using Terrain3DTools.Core.Debug;

namespace Terrain3DTools.Settings
{
    /// <summary>
    /// Centralized settings for the Terrain3DTools system.
    /// Persisted as a Resource file and accessed via GlobalToolSettingsManager.
    /// </summary>
    [GlobalClass, Tool]
    public partial class GlobalToolSettings : Resource
    {
        #region Constants
        public const string SETTINGS_PATH = "res://addons/terrain_3d_tools/Settings/global_settings.tres";
        #endregion

        #region Update Timing
        [ExportGroup("⏱️ Update Timing")]

        [Export(PropertyHint.Range, "0.016,1.0,0.016")]
        public float UpdateInterval { get; set; } = 0.1f;

        [Export(PropertyHint.Range, "0.1,2.0,0.1")]
        public float InteractionThreshold { get; set; } = 0.5f;

        [Export(PropertyHint.Range, "1,10,1")]
        public int GpuCleanupFrameThreshold { get; set; } = 3;

        [Export]
        public bool AutoPushToTerrain { get; set; } = false;

        [Export(PropertyHint.Range, "1.0,512.0,1.0")]
        public float WorldHeightScale { get; set; } = 128.0f;
        #endregion

        #region Layer Visualization
        [ExportGroup("👁️ Layer Visualization")]

        [Export]
        public bool EnableHeightLayerVisualization { get; set; } = true;

        [Export]
        public bool EnableTextureLayerVisualization { get; set; } = true;

        [Export]
        public bool EnableFeatureLayerVisualization { get; set; } = true;

        [ExportSubgroup("Height Layer")]
        [Export]
        public Color HeightPositiveColor { get; set; } = new Color(0.0f, 1.0f, 0.0f); // Green

        [Export]
        public Color HeightNegativeColor { get; set; } = new Color(1.0f, 0.0f, 0.0f); // Red

        [Export(PropertyHint.Range, "0.0,1.0,0.05")]
        public float HeightVisualizationOpacity { get; set; } = 0.6f;

        [ExportSubgroup("Texture Layer")]
        [Export]
        public Color TextureBaseColor { get; set; } = new Color(0.6f, 0.2f, 1.0f); // Violet

        [Export]
        public Color TextureHighlightColor { get; set; } = new Color(0.8f, 0.4f, 1.0f); // Light violet

        [Export(PropertyHint.Range, "0.0,1.0,0.05")]
        public float TextureVisualizationOpacity { get; set; } = 0.8f;

        [ExportSubgroup("Feature Layer")]
        [Export]
        public Color FeaturePositiveColor { get; set; } = new Color(0.4f, 0.9f, 1.0f); // Cyan

        [Export]
        public Color FeatureNegativeColor { get; set; } = new Color(1.0f, 0.3f, 0.1f); // Orange/Red

        [Export(PropertyHint.Range, "0.0,1.0,0.05")]
        public float FeatureVisualizationOpacity { get; set; } = 0.35f;

        [Export]
        public bool FeatureShowContours { get; set; } = true;

        [Export(PropertyHint.Range, "0.01,0.5,0.01")]
        public float FeatureContourSpacing { get; set; } = 0.1f;

        [ExportSubgroup("Region Preview")]
        [Export]
        public bool EnableRegionPreviews { get; set; } = false; // Default to false
        #endregion

        #region Blend Smoothing
        [ExportGroup("🎨 Blend Smoothing")]

        [Export]
        public bool EnableBlendSmoothing { get; set; } = true;

        [Export(PropertyHint.Range, "1,5,1")]
        public int SmoothingPasses { get; set; } = 1;

        [Export(PropertyHint.Range, "0.0,1.0,0.05")]
        public float SmoothingStrength { get; set; } = 0.5f;

        [Export(PropertyHint.Range, "1,128,1")]
        public int MinBlendForSmoothing { get; set; } = 10;

        [Export]
        public bool ConsiderSwappedPairs { get; set; } = true;

        [ExportSubgroup("Isolation Settings")]

        [Export(PropertyHint.Range, "0.0,1.0,0.05")]
        public float IsolationThreshold { get; set; } = 0.6f;

        [Export(PropertyHint.Range, "0.0,1.0,0.05")]
        public float IsolationStrength { get; set; } = 0.5f;

        [Export(PropertyHint.Range, "0,255,1")]
        public int IsolationBlendTarget { get; set; } = 128;
        #endregion

        #region Instancer Settings
        [ExportGroup("Instancer Settings")]

        [Export(PropertyHint.Range, "1024,65536,1024")]
        public int MaxInstancesPerRegion { get; set; } = 32768;

        [Export(PropertyHint.Range, "0.1,10.0,0.1")]
        public float DefaultInstanceDensity { get; set; } = 1.0f;

        [Export(PropertyHint.Range, "0.1,50.0,0.1")]
        public float DefaultMinimumSpacing { get; set; } = 1.0f;
        #endregion

        #region Debug Settings
        [ExportGroup("🐛 Debug Settings")]

        [Export]
        public bool AlwaysReportErrors { get; set; } = true;

        [Export]
        public bool AlwaysReportWarnings { get; set; } = true;

        [Export]
        public bool EnableMessageAggregation { get; set; } = true;

        [Export(PropertyHint.Range, "0.1,5.0,0.1")]
        public float AggregationWindowSeconds { get; set; } = 1.0f;

        [Export]
        public Array<ClassDebugConfig> ActiveDebugClasses { get; set; } = new();
        #endregion

        #region Factory
        /// <summary>
        /// Creates a new settings instance with default values.
        /// </summary>
        public static GlobalToolSettings CreateDefault()
        {
            return new GlobalToolSettings();
        }

        /// <summary>
        /// Creates a deep copy of this settings instance.
        /// </summary>
        public GlobalToolSettings Duplicate()
        {
            var copy = new GlobalToolSettings
            {
                // Update Timing
                UpdateInterval = UpdateInterval,
                InteractionThreshold = InteractionThreshold,
                GpuCleanupFrameThreshold = GpuCleanupFrameThreshold,
                AutoPushToTerrain = AutoPushToTerrain,
                WorldHeightScale = WorldHeightScale,

                // Layer Visualization
                EnableHeightLayerVisualization = EnableHeightLayerVisualization,
                EnableTextureLayerVisualization = EnableTextureLayerVisualization,
                EnableFeatureLayerVisualization = EnableFeatureLayerVisualization,

                // Height Layer
                HeightPositiveColor = HeightPositiveColor,
                HeightNegativeColor = HeightNegativeColor,
                HeightVisualizationOpacity = HeightVisualizationOpacity,

                // Texture Layer
                TextureBaseColor = TextureBaseColor,
                TextureHighlightColor = TextureHighlightColor,
                TextureVisualizationOpacity = TextureVisualizationOpacity,

                // Feature Layer
                FeaturePositiveColor = FeaturePositiveColor,
                FeatureNegativeColor = FeatureNegativeColor,
                FeatureVisualizationOpacity = FeatureVisualizationOpacity,
                FeatureShowContours = FeatureShowContours,
                FeatureContourSpacing = FeatureContourSpacing,

                // Region Preview
                EnableRegionPreviews = EnableRegionPreviews,

                // Blend Smoothing
                EnableBlendSmoothing = EnableBlendSmoothing,
                SmoothingPasses = SmoothingPasses,
                SmoothingStrength = SmoothingStrength,
                MinBlendForSmoothing = MinBlendForSmoothing,
                ConsiderSwappedPairs = ConsiderSwappedPairs,
                IsolationThreshold = IsolationThreshold,
                IsolationStrength = IsolationStrength,
                IsolationBlendTarget = IsolationBlendTarget,

                // Debug Settings
                AlwaysReportErrors = AlwaysReportErrors,
                AlwaysReportWarnings = AlwaysReportWarnings,
                EnableMessageAggregation = EnableMessageAggregation,
                AggregationWindowSeconds = AggregationWindowSeconds,
                ActiveDebugClasses = new Array<ClassDebugConfig>()
            };

            // Deep copy debug classes
            foreach (var config in ActiveDebugClasses)
            {
                if (config != null)
                {
                    copy.ActiveDebugClasses.Add(new ClassDebugConfig(config.ClassName)
                    {
                        Enabled = config.Enabled,
                        EnabledCategories = config.EnabledCategories
                    });
                }
            }

            return copy;
        }
        #endregion
    }
}