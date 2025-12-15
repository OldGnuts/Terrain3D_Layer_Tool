// /Editor/Utils/EditorUIUtils.cs
using System;
using System.Collections.Generic;
using Godot;

namespace Terrain3DTools.Editor.Utils
{
    /// <summary>
    /// Reusable UI components for Terrain3DTools inspectors.
    /// </summary>
    public static class EditorUIUtils
    {
        public static void AddSeparator(Control parent, int height = 8)
        {
            var sep = new HSeparator();
            sep.CustomMinimumSize = new Vector2(0, height);
            parent.AddChild(sep);
        }

        public static void AddSliderRow(Control parent, string labelText, float value, float min, float max, Action<double> onChanged)
        {
            var row = new HBoxContainer();

            var labelNode = new Label
            {
                Text = $"{labelText}:",
                CustomMinimumSize = new Vector2(100, 0)
            };
            row.AddChild(labelNode);

            var slider = new HSlider
            {
                MinValue = min,
                MaxValue = max,
                Step = (max - min) / 100f,
                Value = value,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
            };

            var valueLabel = new Label
            {
                Text = value.ToString("F2"),
                CustomMinimumSize = new Vector2(50, 0),
                HorizontalAlignment = HorizontalAlignment.Right
            };

            slider.ValueChanged += (v) =>
            {
                valueLabel.Text = v.ToString("F2");
                onChanged?.Invoke(v);
            };

            row.AddChild(slider);
            row.AddChild(valueLabel);

            parent.AddChild(row);
        }

        public static Label AddStatRow(GridContainer container, string label, string value)
        {
            var labelNode = new Label { Text = label };
            labelNode.AddThemeColorOverride("font_color", new Color(0.7f, 0.7f, 0.7f));
            container.AddChild(labelNode);

            var valueNode = new Label { Text = value };
            container.AddChild(valueNode);

            return valueNode;
        }

        /// <summary>
        /// Creates a standardized collapsible section.
        /// Returns the container for content.
        /// </summary>
        public static (VBoxContainer container, Action<bool> toggle) CreateCollapsibleSection(
            Control parent, 
            string title, 
            bool defaultExpanded, 
            Dictionary<string, bool> stateTracker = null)
        {
            var outerContainer = new VBoxContainer();
            outerContainer.AddThemeConstantOverride("separation", 4);

            // Header button
            var headerButton = new Button
            {
                Text = (defaultExpanded ? "▼ " : "▶ ") + title,
                Flat = true,
                Alignment = HorizontalAlignment.Left
            };
            headerButton.AddThemeColorOverride("font_color", new Color(0.9f, 0.9f, 0.9f));
            headerButton.AddThemeFontSizeOverride("font_size", 14);

            // Content container
            var contentContainer = new VBoxContainer();
            contentContainer.AddThemeConstantOverride("separation", 4);
            contentContainer.Visible = defaultExpanded;

            Action<bool> setExpanded = (expanded) =>
            {
                contentContainer.Visible = expanded;
                headerButton.Text = (expanded ? "▼ " : "▶ ") + title;
                if (stateTracker != null) stateTracker[title] = expanded;
            };

            headerButton.Pressed += () =>
            {
                bool isVisible = contentContainer.Visible;
                setExpanded(!isVisible);
            };

            outerContainer.AddChild(headerButton);
            outerContainer.AddChild(contentContainer);
            parent.AddChild(outerContainer);

            return (contentContainer, setExpanded);
        }
    }
}