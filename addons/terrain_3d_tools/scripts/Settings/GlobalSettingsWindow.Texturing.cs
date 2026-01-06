using Godot;
using Terrain3DTools.Editor.Utils;
using Terrain3DTools.Settings;

namespace Terrain3DTools.Editor
{
    public partial class GlobalSettingsWindow
    {
        #region Texturing Tab
        private void CreateTexturingTab()
        {
            var content = CreateTab("🎨 Texturing");
            PopulateTexturingSettingsTab(content);
        }

        private void PopulateTexturingSettingsTab(VBoxContainer content)
        {
            var settings = Settings;
            if (settings == null) return;

            // Blend Smoothing Section
            string smoothingHelp = EditorHelpTooltip.FormatHelpText(
                "Controls texture blend smoothing to reduce harsh texture transitions.",
                new System.Collections.Generic.List<(string, string)>
                {
                    ("Enable Blend Smoothing", "Master toggle for all blend smoothing effects"),
                    ("Smoothing Passes", "Number of smoothing iterations (more = smoother but slower)"),
                    ("Smoothing Strength", "How aggressively to smooth blend values"),
                    ("Min Blend for Smoothing", "Minimum blend value to consider for smoothing"),
                    ("Consider Swapped Pairs", "Also smooth texture pairs where base/overlay are swapped")
                },
                "Tip: Start with defaults, then adjust if you see 'salt and pepper' texture noise."
            );

            EditorUIUtils.AddSectionHeader(content, "🔀 Blend Smoothing", "Blend Smoothing Help", smoothingHelp);

            // Master Toggle
            EditorUIUtils.AddCheckBoxRow(content, "Enable Blend Smoothing", settings.EnableBlendSmoothing,
                (v) => { settings.EnableBlendSmoothing = v; MarkSettingsChanged(); },
                "Master toggle for all blend smoothing effects");

            EditorUIUtils.AddSeparator(content, 8);

            // Smoothing Parameters
            EditorUIUtils.AddSubsectionHeader(content, "Smoothing Parameters");

            EditorUIUtils.AddSpinBoxRow(content, "Smoothing Passes", settings.SmoothingPasses, 1, 5,
                (v) => { settings.SmoothingPasses = v; MarkSettingsChanged(); });

            EditorUIUtils.AddSliderRow(content, "Smoothing Strength", settings.SmoothingStrength, 0f, 1f,
                (v) => { settings.SmoothingStrength = (float)v; MarkSettingsChanged(); });

            EditorUIUtils.AddSpinBoxRow(content, "Min Blend for Smoothing", settings.MinBlendForSmoothing, 1, 128,
                (v) => { settings.MinBlendForSmoothing = v; MarkSettingsChanged(); });

            EditorUIUtils.AddCheckBoxRow(content, "Consider Swapped Pairs", settings.ConsiderSwappedPairs,
                (v) => { settings.ConsiderSwappedPairs = v; MarkSettingsChanged(); },
                "Also smooth texture pairs where base/overlay textures are swapped");

            EditorUIUtils.AddSeparator(content, 12);

            // Isolation Settings Section
            string isolationHelp = EditorHelpTooltip.FormatHelpText(
                "Handles isolated texture pixels that create visual noise.",
                new System.Collections.Generic.List<(string, string)>
                {
                    ("Isolation Threshold", "How isolated a pixel must be to trigger isolation handling"),
                    ("Isolation Strength", "How strongly to reduce isolated texture blends"),
                    ("Isolation Blend Target", "Target blend value for isolated pixels")
                },
                null
            );

            EditorUIUtils.AddSectionHeader(content, "🎯 Isolation Handling", "Isolation Settings Help", isolationHelp);

            EditorUIUtils.AddSliderRow(content, "Isolation Threshold", settings.IsolationThreshold, 0f, 1f,
                (v) => { settings.IsolationThreshold = (float)v; MarkSettingsChanged(); });

            EditorUIUtils.AddSliderRow(content, "Isolation Strength", settings.IsolationStrength, 0f, 1f,
                (v) => { settings.IsolationStrength = (float)v; MarkSettingsChanged(); });

            EditorUIUtils.AddSpinBoxRow(content, "Isolation Blend Target", settings.IsolationBlendTarget, 0, 255,
                (v) => { settings.IsolationBlendTarget = v; MarkSettingsChanged(); });
        }
        #endregion
    }
}