// /Editor/Utils/EditorUIUtils.cs
using System;
using System.Collections.Generic;
using Godot;
using Terrain3DTools.Layers.Path;
using Terrain3DTools.Utils;

namespace Terrain3DTools.Editor.Utils
{
    /// <summary>
    /// Reusable UI components for Terrain3DTools inspectors.
    /// </summary>
    public static class EditorUIUtils
    {
        #region Separators and Labels

        public static void AddSeparator(Control parent, int height = 8)
        {
            var sep = new HSeparator();
            sep.CustomMinimumSize = new Vector2(0, height);
            parent.AddChild(sep);
        }

        public static Label CreateSectionLabel(string text)
        {
            var label = new Label
            {
                Text = text,
                Modulate = new Color(0.8f, 0.8f, 0.8f)
            };
            return label;
        }

        public static void AddSectionLabel(Control parent, string text)
        {
            parent.AddChild(CreateSectionLabel(text));
        }

        #endregion

        #region Section Headers

        /// <summary>
        /// Creates a styled section header with optional help button.
        /// Used within tabs to organize content into logical groups.
        /// </summary>
        /// <param name="parent">Parent container to add the header to</param>
        /// <param name="title">Section title (can include emoji)</param>
        /// <param name="helpTitle">Optional help popup title</param>
        /// <param name="helpContent">Optional help popup content</param>
        /// <returns>The header container (in case additional elements need to be added)</returns>
        public static HBoxContainer AddSectionHeader(
            Control parent,
            string title,
            string helpTitle = null,
            string helpContent = null)
        {
            var headerRow = new HBoxContainer();
            headerRow.AddThemeConstantOverride("separation", 8);

            var label = new Label
            {
                Text = title,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
            };
            label.AddThemeFontSizeOverride("font_size", 14);
            label.AddThemeColorOverride("font_color", new Color(0.9f, 0.9f, 0.9f));
            headerRow.AddChild(label);

            // Add help button if content provided
            if (!string.IsNullOrEmpty(helpTitle) && !string.IsNullOrEmpty(helpContent))
            {
                var helpButton = EditorHelpTooltip.CreateHelpButtonStyled(helpTitle, helpContent);
                headerRow.AddChild(helpButton);
            }

            parent.AddChild(headerRow);

            // Add a subtle separator line below the header
            var separator = new HSeparator();
            separator.AddThemeConstantOverride("separation", 2);
            parent.AddChild(separator);

            return headerRow;
        }

        /// <summary>
        /// Creates a styled subsection header (smaller than main section header).
        /// Used for grouping related controls within a section.
        /// </summary>
        public static void AddSubsectionHeader(Control parent, string title)
        {
            var label = new Label
            {
                Text = title
            };
            label.AddThemeFontSizeOverride("font_size", 12);
            label.AddThemeColorOverride("font_color", new Color(0.75f, 0.75f, 0.75f));
            parent.AddChild(label);
        }

        #endregion

        #region Basic Input Rows

        public static void AddSliderRow(Control parent, string labelText, float value, float min, float max, Action<double> onChanged, string format = "F2")
        {
            var row = new HBoxContainer();

            var labelNode = new Label
            {
                Text = $"{labelText}:",
                CustomMinimumSize = new Vector2(EditorConstants.LABEL_MIN_WIDTH, 0)
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
                Text = value.ToString(format),
                CustomMinimumSize = new Vector2(50, 0),
                HorizontalAlignment = HorizontalAlignment.Right
            };

            slider.ValueChanged += (v) =>
            {
                valueLabel.Text = v.ToString(format);
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

        public static void AddEnumDropdown<T>(Control parent, string label, T currentValue, Action<T> onChanged) where T : struct, Enum
        {
            var row = new HBoxContainer();
            row.AddChild(new Label
            {
                Text = $"{label}:",
                CustomMinimumSize = new Vector2(EditorConstants.LABEL_MIN_WIDTH, 0)
            });

            var dropdown = new OptionButton { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            foreach (T value in Enum.GetValues(typeof(T)))
                dropdown.AddItem(value.ToString(), Convert.ToInt32(value));

            dropdown.Selected = Convert.ToInt32(currentValue);
            dropdown.ItemSelected += (idx) => onChanged((T)(object)(int)idx);

            row.AddChild(dropdown);
            parent.AddChild(row);
        }

        public static void AddSpinBoxRow(Control parent, string label, int value, int min, int max, Action<int> onChanged, int step = 1)
        {
            var row = new HBoxContainer();
            row.AddChild(new Label
            {
                Text = $"{label}:",
                CustomMinimumSize = new Vector2(EditorConstants.LABEL_MIN_WIDTH, 0)
            });

            var spinBox = new SpinBox
            {
                MinValue = min,
                MaxValue = max,
                Value = value,
                Step = step,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
            };
            spinBox.ValueChanged += (v) => onChanged((int)v);

            row.AddChild(spinBox);
            parent.AddChild(row);
        }

        public static void AddCheckBoxRow(Control parent, string label, bool value, Action<bool> onChanged, string tooltip = null)
        {
            var checkBox = new CheckBox
            {
                Text = label,
                ButtonPressed = value,
                TooltipText = tooltip ?? ""
            };
            checkBox.Toggled += (v) => onChanged(v);
            parent.AddChild(checkBox);
        }

        #endregion

        #region Collapsible Sections

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
            return CreateCollapsibleSectionWithHelp(parent, title, defaultExpanded, stateTracker, null, null);
        }

        /// <summary>
        /// Creates a standardized collapsible section with an optional inline help button.
        /// Returns the container for content.
        /// </summary>
        public static (VBoxContainer container, Action<bool> toggle) CreateCollapsibleSectionWithHelp(
            Control parent,
            string title,
            bool defaultExpanded,
            Dictionary<string, bool> stateTracker,
            string helpTitle,
            string helpContent)
        {
            var outerContainer = new VBoxContainer();
            outerContainer.AddThemeConstantOverride("separation", 4);

            // Header row (HBoxContainer to hold button + help)
            var headerRow = new HBoxContainer();
            headerRow.AddThemeConstantOverride("separation", 0);

            // Header button
            var headerButton = new Button
            {
                Text = (defaultExpanded ? "▼ " : "▶ ") + title,
                Flat = true,
                Alignment = HorizontalAlignment.Left,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
            };
            headerButton.AddThemeColorOverride("font_color", new Color(0.9f, 0.9f, 0.9f));
            headerButton.AddThemeFontSizeOverride("font_size", 14);
            headerRow.AddChild(headerButton);

            // Help button (if help content provided)
            if (!string.IsNullOrEmpty(helpTitle) && !string.IsNullOrEmpty(helpContent))
            {
                var helpButton = EditorHelpTooltip.CreateHelpButtonStyled(helpTitle, helpContent);
                headerRow.AddChild(helpButton);
            }

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

            outerContainer.AddChild(headerRow);
            outerContainer.AddChild(contentContainer);
            parent.AddChild(outerContainer);

            return (contentContainer, setExpanded);
        }

        #endregion

        #region Inline Collapsible Sections

        /// <summary>
        /// Creates a lightweight inline collapsible section suitable for nesting within other UI.
        /// Less visually prominent than main sections.
        /// </summary>
        /// <param name="parent">Parent container to add the collapsible to</param>
        /// <param name="title">Section title</param>
        /// <param name="defaultExpanded">Initial expanded state</param>
        /// <param name="stateKey">Key for state tracking (optional)</param>
        /// <param name="stateTracker">Dictionary to persist state across rebuilds (optional)</param>
        /// <returns>Container for section content</returns>
        public static VBoxContainer CreateInlineCollapsible(
            Control parent,
            string title,
            bool defaultExpanded,
            string stateKey = null,
            Dictionary<string, bool> stateTracker = null)
        {
            // Check for persisted state
            bool isExpanded = defaultExpanded;
            if (stateTracker != null && !string.IsNullOrEmpty(stateKey) && stateTracker.TryGetValue(stateKey, out bool savedState))
            {
                isExpanded = savedState;
            }

            var outerContainer = new VBoxContainer();
            outerContainer.AddThemeConstantOverride("separation", 2);

            // Header button (smaller, less prominent than main sections)
            var headerButton = new Button
            {
                Text = (isExpanded ? "▼ " : "▶ ") + title,
                Flat = true,
                Alignment = HorizontalAlignment.Left,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
            };
            headerButton.AddThemeColorOverride("font_color", new Color(0.75f, 0.75f, 0.75f));
            headerButton.AddThemeFontSizeOverride("font_size", 12);

            // Content container with slight indent
            var contentMargin = new MarginContainer();
            contentMargin.AddThemeConstantOverride("margin_left", 12);

            var contentContainer = new VBoxContainer();
            contentContainer.AddThemeConstantOverride("separation", 4);
            contentContainer.Visible = isExpanded;

            contentMargin.AddChild(contentContainer);

            headerButton.Pressed += () =>
            {
                bool newState = !contentContainer.Visible;
                contentContainer.Visible = newState;
                headerButton.Text = (newState ? "▼ " : "▶ ") + title;

                // Persist state
                if (stateTracker != null && !string.IsNullOrEmpty(stateKey))
                {
                    stateTracker[stateKey] = newState;
                }
            };

            outerContainer.AddChild(headerButton);
            outerContainer.AddChild(contentMargin);
            parent.AddChild(outerContainer);

            return contentContainer;
        }

        /// <summary>
        /// Adds a complete noise configuration editor within a collapsible section.
        /// </summary>
        public static void AddCollapsibleNoiseEditor(
            Control parent,
            NoiseConfig heightNoise,
            NoiseConfig textureNoise,
            string stateKey = null,
            Dictionary<string, bool> stateTracker = null)
        {
            var noiseContent = CreateInlineCollapsible(parent, "Noise Settings", false, stateKey, stateTracker);

            if (heightNoise != null)
            {
                AddSectionLabel(noiseContent, "Height Noise");
                AddNoiseEditorFields(noiseContent, heightNoise, "Height");
                AddSeparator(noiseContent, 8);
            }

            if (textureNoise != null)
            {
                AddSectionLabel(noiseContent, "Texture Noise");
                AddNoiseEditorFields(noiseContent, textureNoise, "Texture");
            }
        }

        /// <summary>
        /// Creates a lightweight inline collapsible section with a help button.
        /// Suitable for nesting within other UI sections.
        /// </summary>
        /// <param name="parent">Parent container to add the collapsible to</param>
        /// <param name="title">Section title</param>
        /// <param name="defaultExpanded">Initial expanded state</param>
        /// <param name="stateKey">Key for state tracking (optional, defaults to title)</param>
        /// <param name="stateTracker">Dictionary to persist state across rebuilds (optional)</param>
        /// <param name="helpTitle">Help popup title</param>
        /// <param name="helpContent">Help popup content</param>
        /// <returns>Container for section content</returns>
        public static VBoxContainer CreateInlineCollapsibleWithHelp(
            Control parent,
            string title,
            bool defaultExpanded,
            string stateKey = null,
            Dictionary<string, bool> stateTracker = null,
            string helpTitle = null,
            string helpContent = null)
        {
            string key = stateKey ?? title;

            // Check for persisted state
            bool isExpanded = defaultExpanded;
            if (stateTracker != null && stateTracker.TryGetValue(key, out bool savedState))
            {
                isExpanded = savedState;
            }

            var outerContainer = new VBoxContainer();
            outerContainer.AddThemeConstantOverride("separation", 2);

            // Header row
            var headerRow = new HBoxContainer();
            headerRow.AddThemeConstantOverride("separation", 0);

            var headerButton = new Button
            {
                Text = (isExpanded ? "▼ " : "▶ ") + title,
                Flat = true,
                Alignment = HorizontalAlignment.Left,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
            };
            headerButton.AddThemeColorOverride("font_color", new Color(0.75f, 0.75f, 0.75f));
            headerButton.AddThemeFontSizeOverride("font_size", 12);
            headerRow.AddChild(headerButton);

            // Help button (if content provided)
            if (!string.IsNullOrEmpty(helpTitle) && !string.IsNullOrEmpty(helpContent))
            {
                var helpBtn = EditorHelpTooltip.CreateHelpButtonStyled(helpTitle, helpContent);
                headerRow.AddChild(helpBtn);
            }

            outerContainer.AddChild(headerRow);

            // Content container with indent
            var contentMargin = new MarginContainer();
            contentMargin.AddThemeConstantOverride("margin_left", 12);

            var contentContainer = new VBoxContainer();
            contentContainer.AddThemeConstantOverride("separation", 4);
            contentContainer.Visible = isExpanded;

            contentMargin.AddChild(contentContainer);
            outerContainer.AddChild(contentMargin);

            // Toggle handler
            headerButton.Pressed += () =>
            {
                bool newState = !contentContainer.Visible;
                contentContainer.Visible = newState;
                headerButton.Text = (newState ? "▼ " : "▶ ") + title;

                // Persist state
                if (stateTracker != null)
                {
                    stateTracker[key] = newState;
                }
            };

            parent.AddChild(outerContainer);
            return contentContainer;
        }

        /// <summary>
        /// Adds noise editor fields without the enable checkbox label prefix.
        /// Used internally by AddCollapsibleNoiseEditor.
        /// </summary>
        private static void AddNoiseEditorFields(Control parent, NoiseConfig noise, string label)
        {
            if (noise == null) return;

            var enableCheck = new CheckBox
            {
                Text = $"Enable {label} Noise",
                ButtonPressed = noise.Enabled
            };
            enableCheck.Toggled += (v) => noise.Enabled = v;
            parent.AddChild(enableCheck);

            AddSliderRow(parent, "Amplitude", noise.Amplitude, 0f, 10f,
                (v) => noise.Amplitude = (float)v);
            AddSliderRow(parent, "Frequency", noise.Frequency, 0.001f, 1f,
                (v) => noise.Frequency = (float)v, "F3");

            AddSpinBoxRow(parent, "Octaves", noise.Octaves, 1, 8,
                (v) => noise.Octaves = v);

            // Seed row with randomize button
            var seedRow = new HBoxContainer();
            seedRow.AddChild(new Label
            {
                Text = "Seed:",
                CustomMinimumSize = new Vector2(EditorConstants.LABEL_MIN_WIDTH, 0)
            });

            var seedSpin = new SpinBox
            {
                MinValue = int.MinValue,
                MaxValue = int.MaxValue,
                Value = noise.Seed,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
            };
            seedSpin.ValueChanged += (v) => noise.Seed = (int)v;
            seedRow.AddChild(seedSpin);

            var randBtn = new Button { Text = "🎲", TooltipText = "Randomize seed" };
            randBtn.Pressed += () =>
            {
                noise.Seed = (int)(GD.Randi() % 100000);
                seedSpin.Value = noise.Seed;
            };
            seedRow.AddChild(randBtn);
            parent.AddChild(seedRow);

            var worldCheck = new CheckBox
            {
                Text = "Use World Coords",
                ButtonPressed = noise.UseWorldCoords,
                TooltipText = "Sample noise in world space vs local UV space"
            };
            worldCheck.Toggled += (v) => noise.UseWorldCoords = v;
            parent.AddChild(worldCheck);
        }

        #endregion

        #region Curve Editing

        /// <summary>
        /// Adds a curve editor row with mini preview and edit button.
        /// </summary>
        public static void AddCurveEditorRow(
            Control parent,
            string label,
            Curve curve,
            Action<Curve> onChanged,
            EditorWindowTracker windowTracker = null)
        {
            var row = new HBoxContainer();
            row.AddChild(new Label
            {
                Text = $"{label}:",
                CustomMinimumSize = new Vector2(EditorConstants.LABEL_MIN_WIDTH, 0)
            });

            var preview = new CurveMiniPreview(curve)
            {
                CustomMinimumSize = new Vector2(100, EditorConstants.CURVE_PREVIEW_HEIGHT),
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
            };
            row.AddChild(preview);

            var editBtn = new Button { Text = "Edit..." };
            editBtn.Pressed += () => ShowCurveEditorWindow(label, curve, onChanged, preview, windowTracker);
            row.AddChild(editBtn);

            parent.AddChild(row);
        }

        /// <summary>
        /// Shows a curve editor popup window.
        /// </summary>
        public static void ShowCurveEditorWindow(
            string title,
            Curve curve,
            Action<Curve> onAccept,
            CurveMiniPreview previewToUpdate = null,
            EditorWindowTracker windowTracker = null)
        {
            var editCurve = curve != null ? (Curve)curve.Duplicate() : CurveUtils.CreateLinearCurve();

            Window window;
            if (windowTracker != null)
            {
                window = windowTracker.CreateTrackedWindow($"Edit Curve: {title}", new Vector2I(500, 400));
            }
            else
            {
                window = new Window
                {
                    Title = $"Edit Curve: {title}",
                    Size = new Vector2I(500, 400),
                    Exclusive = true,
                    Transient = true
                };
                window.CloseRequested += () =>
                {
                    window.Hide();
                    window.QueueFree();
                };
                EditorInterface.Singleton?.GetBaseControl()?.AddChild(window);
            }

            var vbox = new VBoxContainer();
            vbox.SetAnchorsPreset(Control.LayoutPreset.FullRect);

            var margin = new MarginContainer();
            margin.AddThemeConstantOverride("margin_left", 8);
            margin.AddThemeConstantOverride("margin_right", 8);
            margin.AddThemeConstantOverride("margin_top", 8);
            margin.AddThemeConstantOverride("margin_bottom", 8);

            var innerVbox = new VBoxContainer();
            innerVbox.AddThemeConstantOverride("separation", 8);

            var editor = new InspectorCurveEditor(editCurve)
            {
                SizeFlagsVertical = Control.SizeFlags.ExpandFill,
                CustomMinimumSize = new Vector2(0, 250)
            };
            innerVbox.AddChild(editor);

            // Presets
            var presetsLabel = new Label { Text = "Presets:" };
            presetsLabel.AddThemeColorOverride("font_color", new Color(0.7f, 0.7f, 0.7f));
            innerVbox.AddChild(presetsLabel);

            var presets = new HBoxContainer();
            presets.AddThemeConstantOverride("separation", 4);

            foreach (var preset in new[] { "Linear", "EaseOut", "EaseIn", "Bell", "Flat" })
            {
                var btn = new Button { Text = preset };
                btn.Pressed += () => editor.SetCurve(CurveUtils.GetPresetByName(preset));
                presets.AddChild(btn);
            }
            innerVbox.AddChild(presets);

            // Buttons
            var btns = new HBoxContainer();
            btns.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });

            var cancelBtn = new Button { Text = "Cancel" };
            cancelBtn.Pressed += () =>
            {
                window.Hide();
                window.QueueFree();
                windowTracker?.Untrack(window);
            };
            btns.AddChild(cancelBtn);

            var okBtn = new Button { Text = "OK" };
            okBtn.Pressed += () =>
            {
                var resultCurve = editor.GetCurve();
                onAccept?.Invoke(resultCurve);
                previewToUpdate?.SetCurve(resultCurve);
                window.Hide();
                window.QueueFree();
                windowTracker?.Untrack(window);
            };
            btns.AddChild(okBtn);

            innerVbox.AddChild(btns);

            margin.AddChild(innerVbox);
            vbox.AddChild(margin);
            window.AddChild(vbox);

            window.PopupCentered();
        }

        #endregion

        #region Noise Editing

        /// <summary>
        /// Adds a complete noise configuration editor.
        /// </summary>
        public static void AddNoiseEditor(Control parent, NoiseConfig noise, string label)
        {
            if (noise == null) return;

            var enableCheck = new CheckBox
            {
                Text = $"Enable {label} Noise",
                ButtonPressed = noise.Enabled
            };
            enableCheck.Toggled += (v) => noise.Enabled = v;
            parent.AddChild(enableCheck);

            AddSliderRow(parent, "Amplitude", noise.Amplitude, 0f, 10f,
                (v) => noise.Amplitude = (float)v);
            AddSliderRow(parent, "Frequency", noise.Frequency, 0.001f, 1f,
                (v) => noise.Frequency = (float)v, "F3");

            AddSpinBoxRow(parent, "Octaves", noise.Octaves, 1, 8,
                (v) => noise.Octaves = v);

            // Seed row with randomize button
            var seedRow = new HBoxContainer();
            seedRow.AddChild(new Label
            {
                Text = "Seed:",
                CustomMinimumSize = new Vector2(EditorConstants.LABEL_MIN_WIDTH, 0)
            });

            var seedSpin = new SpinBox
            {
                MinValue = int.MinValue,
                MaxValue = int.MaxValue,
                Value = noise.Seed,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
            };
            seedSpin.ValueChanged += (v) => noise.Seed = (int)v;
            seedRow.AddChild(seedSpin);

            var randBtn = new Button { Text = "🎲", TooltipText = "Randomize seed" };
            randBtn.Pressed += () =>
            {
                noise.Seed = (int)(GD.Randi() % 100000);
                seedSpin.Value = noise.Seed;
            };
            seedRow.AddChild(randBtn);
            parent.AddChild(seedRow);

            var worldCheck = new CheckBox
            {
                Text = "Use World Coords",
                ButtonPressed = noise.UseWorldCoords,
                TooltipText = "Sample noise in world space vs local UV space"
            };
            worldCheck.Toggled += (v) => noise.UseWorldCoords = v;
            parent.AddChild(worldCheck);
        }

        #endregion

        #region Window Management

        /// <summary>
        /// Creates a popup window.
        /// </summary>
        public static Window CreatePopupWindow(string title, Vector2I size, Control addToParent = null)
        {
            var window = new Window
            {
                Title = title,
                Size = size,
                Exclusive = true,
                Transient = true
            };

            var parent = addToParent ?? EditorInterface.Singleton?.GetBaseControl();
            parent?.AddChild(window);

            return window;
        }

        #endregion
    }

    /// <summary>
    /// Tracks open windows for an inspector and handles cleanup.
    /// </summary>
    public class EditorWindowTracker
    {
        private readonly List<Window> _openWindows = new();

        /// <summary>
        /// Creates a tracked window that will be cleaned up when CloseAll is called.
        /// </summary>
        public Window CreateTrackedWindow(string title, Vector2I size)
        {
            var window = EditorUIUtils.CreatePopupWindow(title, size);

            window.CloseRequested += () =>
            {
                _openWindows.Remove(window);
                window.Hide();
                window.QueueFree();
            };

            _openWindows.Add(window);
            return window;
        }

        /// <summary>
        /// Closes and frees all tracked windows.
        /// </summary>
        public void CloseAll()
        {
            foreach (var window in _openWindows)
            {
                if (window != null && GodotObject.IsInstanceValid(window))
                {
                    window.Hide();
                    window.QueueFree();
                }
            }
            _openWindows.Clear();
        }

        /// <summary>
        /// Removes a window from tracking (call when window closes itself).
        /// </summary>
        public void Untrack(Window window)
        {
            _openWindows.Remove(window);
        }
    }
}