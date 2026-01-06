// /Editor/Utils/EditorHelpTooltip.cs
using Godot;
using System.Collections.Generic;

namespace Terrain3DTools.Editor.Utils
{
    /// <summary>
    /// Reusable help tooltip/popup system for editor UI.
    /// </summary>
    public static class EditorHelpTooltip
    {
        // Yellow/gold color for help buttons
        private static readonly Color HelpButtonColor = new Color(1.0f, 0.85f, 0.2f);
        private static readonly Color HelpButtonHoverColor = new Color(1.0f, 0.95f, 0.5f);

        // Track active help windows to prevent duplicates
        private static readonly List<Window> _activeHelpWindows = new();

        /// <summary>
        /// Creates a styled help button (yellow "?" for visibility) that shows a popup with information.
        /// Designed to be placed inline with section headers.
        /// </summary>
        public static Button CreateHelpButtonStyled(string title, string content, Control parent = null)
        {
            var button = new Button
            {
                Text = "?",
                TooltipText = "Click for help",
                CustomMinimumSize = new Vector2(22, 22),
                Flat = true
            };

            button.AddThemeFontSizeOverride("font_size", 13);
            button.AddThemeColorOverride("font_color", HelpButtonColor);
            button.AddThemeColorOverride("font_hover_color", HelpButtonHoverColor);
            button.AddThemeColorOverride("font_pressed_color", HelpButtonHoverColor);

            button.Pressed += () => ShowHelpWindow(title, content, button);

            return button;
        }

        /// <summary>
        /// Creates a help button that shows a popup with information.
        /// </summary>
        public static Button CreateHelpButton(string title, string content, Control parent = null)
        {
            var button = new Button
            {
                Text = "?",
                TooltipText = "Click for help",
                CustomMinimumSize = new Vector2(24, 24),
                Flat = true
            };

            button.AddThemeFontSizeOverride("font_size", 12);

            button.Pressed += () => ShowHelpWindow(title, content, button);

            return button;
        }

        /// <summary>
        /// Creates a help button with a list of keyboard shortcuts.
        /// </summary>
        public static Button CreateShortcutHelpButton(string title, List<(string shortcut, string description)> shortcuts, Control parent = null)
        {
            string content = FormatShortcuts(shortcuts);
            return CreateHelpButton(title, content, parent);
        }

        /// <summary>
        /// Shows a help window near the specified control.
        /// Uses a proper Window instead of PopupPanel to avoid focus issues.
        /// </summary>
        public static void ShowHelpWindow(string title, string content, Control nearControl)
        {
            // Close any existing help windows first
            CloseAllHelpWindows();

            // Base size + 10% extra for breathing room
            const int BASE_WIDTH = 380;
            const int BASE_HEIGHT = 450;
            int WINDOW_WIDTH = (int)(BASE_WIDTH * 1.10f);   // 418
            int WINDOW_HEIGHT = (int)(BASE_HEIGHT * 1.10f); // 495

            var window = new Window
            {
                Title = $"ℹ️ {title}",
                Size = new Vector2I(WINDOW_WIDTH, WINDOW_HEIGHT),
                Exclusive = false,
                Transient = false,
                Unresizable = false,
                MinSize = new Vector2I(WINDOW_WIDTH, WINDOW_HEIGHT),
                // FIX: Disable content scaling to ensure consistent appearance regardless of parent
                ContentScaleMode = Window.ContentScaleModeEnum.Disabled
            };

            // Background panel
            var panel = new PanelContainer();
            panel.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            var style = new StyleBoxFlat
            {
                BgColor = new Color(0.15f, 0.15f, 0.18f, 0.98f),
                BorderColor = new Color(0.3f, 0.5f, 0.7f, 0.8f),
                BorderWidthBottom = 1,
                BorderWidthTop = 1,
                BorderWidthLeft = 1,
                BorderWidthRight = 1,
                CornerRadiusBottomLeft = 4,
                CornerRadiusBottomRight = 4,
                CornerRadiusTopLeft = 4,
                CornerRadiusTopRight = 4,
                ContentMarginLeft = 16,
                ContentMarginRight = 16,
                ContentMarginTop = 12,
                ContentMarginBottom = 12
            };
            panel.AddThemeStyleboxOverride("panel", style);

            var vbox = new VBoxContainer();
            vbox.AddThemeConstantOverride("separation", 12);

            // Content with scroll
            var scroll = new ScrollContainer
            {
                SizeFlagsVertical = Control.SizeFlags.ExpandFill,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled
            };

            var contentLabel = new RichTextLabel
            {
                Text = content,
                BbcodeEnabled = true,
                FitContent = true,
                ScrollActive = false,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                CustomMinimumSize = new Vector2(WINDOW_WIDTH - 60, 0),
                // FIX: Disable auto-wrap fitting that might affect sizing
                AutowrapMode = TextServer.AutowrapMode.Word
            };

            // FIX: Set all font size overrides explicitly to ensure consistent sizing
            contentLabel.AddThemeFontSizeOverride("normal_font_size", 14);
            contentLabel.AddThemeFontSizeOverride("bold_font_size", 14);
            contentLabel.AddThemeFontSizeOverride("italics_font_size", 14);
            contentLabel.AddThemeFontSizeOverride("bold_italics_font_size", 14);
            contentLabel.AddThemeFontSizeOverride("mono_font_size", 13);

            scroll.AddChild(contentLabel);
            vbox.AddChild(scroll);

            // Close button
            var closeBtn = new Button
            {
                Text = "Close",
                SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
                CustomMinimumSize = new Vector2(80, 28)
            };
            closeBtn.AddThemeFontSizeOverride("font_size", 14);

            closeBtn.Pressed += () =>
            {
                _activeHelpWindows.Remove(window);
                window.Hide();
                window.QueueFree();
            };
            vbox.AddChild(closeBtn);

            panel.AddChild(vbox);
            window.AddChild(panel);

            // Handle window close
            window.CloseRequested += () =>
            {
                _activeHelpWindows.Remove(window);
                window.Hide();
                window.QueueFree();
            };

            // Add to editor
            var editorBase = EditorInterface.Singleton?.GetBaseControl();
            editorBase?.AddChild(window);

            // Position the window
            PositionHelpWindow(window, nearControl);

            _activeHelpWindows.Add(window);
            window.Show();
        }

        /// <summary>
        /// Positions the help window to avoid overlapping the source UI.
        /// </summary>
        private static void PositionHelpWindow(Window helpWindow, Control nearControl)
        {
            Vector2I windowSize = helpWindow.Size;
            const int MARGIN = 20;

            Vector2I editorWindowPos = DisplayServer.WindowGetPosition();
            Vector2I editorWindowSize = DisplayServer.WindowGetSize();

            int popupX;
            int popupY;

            // Get the root editor window so we can exclude it from our search
            var editorBase = EditorInterface.Singleton?.GetBaseControl();
            Window editorRootWindow = editorBase?.GetWindow();

            // Find if we're inside a popup/dialog Window (like GlobalSettingsWindow)
            // but NOT the main editor window
            Window parentWindow = null;
            if (nearControl != null)
            {
                Node currentNode = nearControl;
                while (currentNode != null)
                {
                    if (currentNode is Window foundWindow &&
                        foundWindow != helpWindow &&
                        foundWindow != editorRootWindow)
                    {
                        parentWindow = foundWindow;
                        break;
                    }
                    currentNode = currentNode.GetParent();
                }
            }

            if (parentWindow != null)
            {
                // Inside a popup/dialog Window (e.g., GlobalSettingsWindow)
                Vector2I parentPos = parentWindow.Position;
                Vector2I parentSize = parentWindow.Size;

                int spaceOnLeft = parentPos.X - editorWindowPos.X;

                if (spaceOnLeft >= windowSize.X + MARGIN)
                {
                    popupX = parentPos.X - windowSize.X - MARGIN;
                }
                else
                {
                    popupX = parentPos.X + parentSize.X + MARGIN;
                }

                popupY = parentPos.Y + (parentSize.Y - windowSize.Y) / 2;
            }
            else
            {
                // In main editor (inspector, dock, etc.)
                // Get inspector width and position help window to the left of it

                var inspector = EditorInterface.Singleton?.GetInspector();

                if (inspector != null)
                {
                    // Walk up from inspector to find the full dock panel width
                    // The inspector itself might be narrower than the full dock container
                    float dockWidth = inspector.Size.X;

                    Control current = inspector;
                    while (current != null)
                    {
                        Control parent = current.GetParent() as Control;
                        if (parent == null) break;

                        // Look for container that's roughly the same width or slightly wider
                        // This helps find the dock container, not the whole editor
                        if (parent.Size.X > dockWidth && parent.Size.X < editorWindowSize.X * 0.5f)
                        {
                            dockWidth = parent.Size.X;
                        }

                        // Stop if we hit something too wide (main editor area)
                        if (parent.Size.X > editorWindowSize.X * 0.6f)
                            break;

                        current = parent;
                    }

                    // Add some padding to account for dock margins/borders
                    dockWidth += 20;

                    // Position help window to the left of the inspector dock
                    // Right edge of help window should be at (editor right edge - dock width - margin)
                    popupX = editorWindowPos.X + editorWindowSize.X - (int)dockWidth - windowSize.X - MARGIN;
                }
                else
                {
                    // Fallback: center of editor
                    popupX = editorWindowPos.X + (editorWindowSize.X - windowSize.X) / 2;
                }

                // Vertically center in editor
                popupY = editorWindowPos.Y + (editorWindowSize.Y - windowSize.Y) / 2;
            }

            // Clamp to editor window bounds
            popupX = Mathf.Clamp(popupX, editorWindowPos.X + MARGIN, editorWindowPos.X + editorWindowSize.X - windowSize.X - MARGIN);
            popupY = Mathf.Clamp(popupY, editorWindowPos.Y + MARGIN, editorWindowPos.Y + editorWindowSize.Y - windowSize.Y - MARGIN);

            helpWindow.Position = new Vector2I(popupX, popupY);
        }
        
        /// <summary>
        /// Closes all active help windows.
        /// </summary>
        public static void CloseAllHelpWindows()
        {
            foreach (var window in _activeHelpWindows.ToArray())
            {
                if (window != null && GodotObject.IsInstanceValid(window))
                {
                    window.Hide();
                    window.QueueFree();
                }
            }
            _activeHelpWindows.Clear();
        }

        /// <summary>
        /// Formats a list of shortcuts into a readable string with BBCode.
        /// </summary>
        public static string FormatShortcuts(List<(string shortcut, string description)> shortcuts)
        {
            var lines = new List<string>();

            foreach (var (shortcut, description) in shortcuts)
            {
                lines.Add($"[color=#88bbff]{shortcut}[/color]  →  {description}");
            }

            return string.Join("\n", lines);
        }

        /// <summary>
        /// Creates formatted help text with sections.
        /// </summary>
        public static string FormatHelpText(string intro, List<(string shortcut, string description)> shortcuts, string footer = null)
        {
            var parts = new List<string>();

            if (!string.IsNullOrEmpty(intro))
            {
                parts.Add(intro);
                parts.Add("");
            }

            if (shortcuts != null && shortcuts.Count > 0)
            {
                parts.Add("[color=#aaaaaa]Settings:[/color]");
                parts.Add(FormatShortcuts(shortcuts));
            }

            if (!string.IsNullOrEmpty(footer))
            {
                parts.Add("");
                parts.Add($"[color=#88cc88]{footer}[/color]");
            }

            return string.Join("\n", parts);
        }
    }
}