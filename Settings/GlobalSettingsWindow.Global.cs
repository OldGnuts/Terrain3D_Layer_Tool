using Godot;
using Terrain3DTools.Editor.Utils;
using Terrain3DTools.Settings;

namespace Terrain3DTools.Editor
{
    public partial class GlobalSettingsWindow
    {
        #region Global Tab
        private void CreateGlobalTab()
        {
            var content = CreateTab("⚙️ Global");
            PopulateGlobalSettingsTab(content);
        }

        private void PopulateGlobalSettingsTab(VBoxContainer content)
        {
            var settings = Settings;
            if (settings == null) return;

            // Update Timing Section
            string timingHelp = EditorHelpTooltip.FormatHelpText(
                "Controls the update pipeline timing.",
                new System.Collections.Generic.List<(string, string)>
                {
            ("Update Interval", "Time between terrain update cycles (lower = more responsive, higher = better performance)"),
            ("Interaction Threshold", "Time to wait after changes before running full update (batches rapid edits)"),
            ("GPU Cleanup Frames", "Frames to wait before cleaning up GPU resources")
                },
                "Tip: Increase Update Interval if you experience lag during editing."
            );

            EditorUIUtils.AddSectionHeader(content, "⏱️ Update Timing", "Update Timing Help", timingHelp);

            EditorUIUtils.AddSliderRow(content, "Update Interval", settings.UpdateInterval, 0.016f, 1.0f,
                (v) => { settings.UpdateInterval = (float)v; MarkSettingsChanged(); }, "F3");

            EditorUIUtils.AddSliderRow(content, "Interaction Threshold", settings.InteractionThreshold, 0.1f, 2.0f,
                (v) => { settings.InteractionThreshold = (float)v; MarkSettingsChanged(); }, "F2");

            EditorUIUtils.AddSpinBoxRow(content, "GPU Cleanup Frames", settings.GpuCleanupFrameThreshold, 1, 10,
                (v) => { settings.GpuCleanupFrameThreshold = v; MarkSettingsChanged(); });

            EditorUIUtils.AddSeparator(content, 12);

            // World Settings Section
            string worldHelp = EditorHelpTooltip.FormatHelpText(
                "Global world and terrain settings.",
                new System.Collections.Generic.List<(string, string)>
                {
            ("World Height Scale", "Vertical scale factor for terrain features"),
            ("Auto Push", "Automatically push changes to Terrain3D (disable for manual control)"),
            ("Region Previews", "Show preview meshes for regions being processed")
                },
                null
            );

            EditorUIUtils.AddSectionHeader(content, "🌍 World Settings", "World Settings Help", worldHelp);

            EditorUIUtils.AddSliderRow(content, "World Height Scale", settings.WorldHeightScale, 1.0f, 512.0f,
                (v) => { settings.WorldHeightScale = (float)v; MarkSettingsChanged(); }, "F1");

            EditorUIUtils.AddSeparator(content, 4);

            EditorUIUtils.AddCheckBoxRow(content, "Auto Push to Terrain", settings.AutoPushToTerrain,
                (v) => { settings.AutoPushToTerrain = v; MarkSettingsChanged(); },
                "Automatically push layer changes to Terrain3D");

            EditorUIUtils.AddCheckBoxRow(content, "Enable Region Previews", settings.EnableRegionPreviews,
                (v) => { settings.EnableRegionPreviews = v; MarkSettingsChanged(); },
                "Show preview meshes for regions being processed");

            EditorUIUtils.AddSeparator(content, 12);

            // Instancer Settings Section
            string instancerHelp = EditorHelpTooltip.FormatHelpText(
                "Settings for mesh instancing on terrain.",
                new System.Collections.Generic.List<(string, string)>
                {
            ("Max Instances Per Region", "Maximum number of instances that can be placed in a single region (higher = more dense foliage, more GPU memory)"),
            ("Default Instance Density", "Default density multiplier for new instancer layers"),
            ("Default Minimum Spacing", "Default minimum distance between instances in world units")
                },
                "Tip: If you see holes in dense foliage, increase Max Instances Per Region."
            );

            EditorUIUtils.AddSectionHeader(content, "🌿 Instancer Settings", "Instancer Settings Help", instancerHelp);

            EditorUIUtils.AddSpinBoxRow(content, "Max Instances Per Region", settings.MaxInstancesPerRegion, 1024, 65536,
                (v) => { settings.MaxInstancesPerRegion = v; MarkSettingsChanged(); }, 1024);

            EditorUIUtils.AddSliderRow(content, "Default Instance Density", settings.DefaultInstanceDensity, 0.1f, 10.0f,
                (v) => { settings.DefaultInstanceDensity = (float)v; MarkSettingsChanged(); }, "F1");

            EditorUIUtils.AddSliderRow(content, "Default Minimum Spacing", settings.DefaultMinimumSpacing, 0.1f, 50.0f,
                (v) => { settings.DefaultMinimumSpacing = (float)v; MarkSettingsChanged(); }, "F1");
        }
        #endregion
    }
}