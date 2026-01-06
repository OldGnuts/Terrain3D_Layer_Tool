// /Editor/GlobalSettingsWindow_DebugTab.cs
using System;
using System.Linq;
using Godot;
using Terrain3DTools.Core.Debug;
using Terrain3DTools.Editor.Utils;
using Terrain3DTools.Settings;
using static Godot.Control;

namespace Terrain3DTools.Editor
{
    public partial class GlobalSettingsWindow
    {
        #region Debug Tab Fields
        private VBoxContainer _debugClassListContainer;
        #endregion

        #region Debug Settings Changed Helper
        /// <summary>
        /// Marks debug settings as changed and notifies the debug manager.
        /// Use this instead of MarkSettingsChanged() for debug-related settings.
        /// </summary>
        private void MarkDebugSettingsChanged()
        {
            _hasUnsavedChanges = true;
            GlobalToolSettingsManager.NotifyDebugSettingsChanged();
            UpdateSaveButtonState();
        }
        #endregion

        #region Debug Tab
        private void CreateDebugTab()
        {
            var content = CreateTab("🐛 Debug");
            PopulateDebugSettingsTab(content);
        }

        private void PopulateDebugSettingsTab(VBoxContainer content)
        {
            var settings = Settings;
            if (settings == null) return;

            // Global Debug Settings Section
            string globalHelp = EditorHelpTooltip.FormatHelpText(
                "Global debug output settings.",
                new System.Collections.Generic.List<(string, string)>
                {
                    ("Always Report Errors", "Always print errors regardless of class settings"),
                    ("Always Report Warnings", "Always print warnings regardless of class settings"),
                    ("Message Aggregation", "Group repeated messages to reduce console spam"),
                    ("Aggregation Window", "Time window for grouping similar messages")
                },
                null
            );

            EditorUIUtils.AddSectionHeader(content, "⚡ Global Settings", "Global Debug Help", globalHelp);

            EditorUIUtils.AddCheckBoxRow(content, "Always Report Errors", settings.AlwaysReportErrors,
                (v) => { settings.AlwaysReportErrors = v; MarkDebugSettingsChanged(); },
                "Always print errors regardless of class settings");

            EditorUIUtils.AddCheckBoxRow(content, "Always Report Warnings", settings.AlwaysReportWarnings,
                (v) => { settings.AlwaysReportWarnings = v; MarkDebugSettingsChanged(); },
                "Always print warnings regardless of class settings");

            EditorUIUtils.AddSeparator(content, 8);

            EditorUIUtils.AddCheckBoxRow(content, "Enable Message Aggregation", settings.EnableMessageAggregation,
                (v) => { settings.EnableMessageAggregation = v; MarkDebugSettingsChanged(); },
                "Group repeated messages to reduce console spam");

            EditorUIUtils.AddSliderRow(content, "Aggregation Window (s)", settings.AggregationWindowSeconds, 0.1f, 5.0f,
                (v) => { settings.AggregationWindowSeconds = (float)v; MarkDebugSettingsChanged(); }, "F1");

            EditorUIUtils.AddSeparator(content, 16);

            // Debug Classes Section
            string classHelp = EditorHelpTooltip.FormatHelpText(
                "Configure which classes output debug messages.",
                new System.Collections.Generic.List<(string, string)>
                {
                    ("Enable", "Toggle debug output for this class"),
                    ("Categories", "Select which debug categories are active"),
                    ("Quick Add", "Common classes can be added with one click")
                },
                "Tip: Enable specific classes only when debugging to reduce console noise."
            );

            EditorUIUtils.AddSectionHeader(content, "📋 Active Debug Classes", "Debug Classes Help", classHelp);

            _debugClassListContainer = ExpandHorizontal(new VBoxContainer());
            _debugClassListContainer.AddThemeConstantOverride("separation", 4);
            RefreshDebugClassList();
            content.AddChild(_debugClassListContainer);

            EditorUIUtils.AddSeparator(content, 8);

            // Add class row
            var addClassRow = ExpandHorizontal(new HBoxContainer());
            addClassRow.AddThemeConstantOverride("separation", 8);

            var classNameEdit = new LineEdit
            {
                PlaceholderText = "Class name...",
                SizeFlagsHorizontal = SizeFlags.ExpandFill
            };
            addClassRow.AddChild(classNameEdit);

            var addButton = new Button { Text = "+ Add Class" };
            addButton.Pressed += () =>
            {
                string className = classNameEdit.Text.Trim();
                if (!string.IsNullOrEmpty(className))
                {
                    AddDebugClass(className);
                    classNameEdit.Text = "";
                }
            };
            addClassRow.AddChild(addButton);

            content.AddChild(addClassRow);

            // Quick add buttons
            EditorUIUtils.AddSeparator(content, 4);
            EditorUIUtils.AddSubsectionHeader(content, "Quick Add Common Classes");

            var quickAddRow = new HBoxContainer();
            quickAddRow.AddThemeConstantOverride("separation", 4);

            foreach (var className in new[] { "TerrainLayerManager", "PathLayer", "HeightLayer", "TextureLayer" })
            {
                var btn = new Button
                {
                    Text = className,
                    TooltipText = $"Add {className} to debug list"
                };
                btn.AddThemeFontSizeOverride("font_size", 10);
                btn.Pressed += () => AddDebugClass(className);
                quickAddRow.AddChild(btn);
            }

            content.AddChild(quickAddRow);
        }

        private void RefreshDebugClassList()
        {
            if (_debugClassListContainer == null) return;

            foreach (var child in _debugClassListContainer.GetChildren())
                child.QueueFree();

            var settings = Settings;
            if (settings?.ActiveDebugClasses == null) return;

            for (int i = 0; i < settings.ActiveDebugClasses.Count; i++)
            {
                var config = settings.ActiveDebugClasses[i];
                if (config == null) continue;

                var row = CreateDebugClassRow(config, i);
                _debugClassListContainer.AddChild(row);
            }

            if (settings.ActiveDebugClasses.Count == 0)
            {
                var emptyLabel = new Label
                {
                    Text = "No debug classes configured. Add classes above to enable debug output.",
                    AutowrapMode = TextServer.AutowrapMode.Word
                };
                emptyLabel.AddThemeColorOverride("font_color", new Color(0.5f, 0.5f, 0.5f));
                emptyLabel.AddThemeFontSizeOverride("font_size", 11);
                _debugClassListContainer.AddChild(emptyLabel);
            }
        }

        private Control CreateDebugClassRow(ClassDebugConfig config, int index)
        {
            var panel = ExpandHorizontal(new PanelContainer());
            var style = new StyleBoxFlat
            {
                BgColor = new Color(0.15f, 0.15f, 0.18f),
                CornerRadiusBottomLeft = 3,
                CornerRadiusBottomRight = 3,
                CornerRadiusTopLeft = 3,
                CornerRadiusTopRight = 3,
                ContentMarginLeft = 8,
                ContentMarginRight = 8,
                ContentMarginTop = 4,
                ContentMarginBottom = 4
            };
            panel.AddThemeStyleboxOverride("panel", style);

            var vbox = ExpandHorizontal(new VBoxContainer());
            vbox.AddThemeConstantOverride("separation", 4);

            // Header row: Enable checkbox, class name, delete button
            var headerRow = ExpandHorizontal(new HBoxContainer());
            headerRow.AddThemeConstantOverride("separation", 8);

            var enableCheck = new CheckBox
            {
                ButtonPressed = config.Enabled,
                TooltipText = "Enable/disable debug output for this class"
            };
            int capturedIndex = index;
            enableCheck.Toggled += (v) =>
            {
                config.Enabled = v;
                MarkDebugSettingsChanged();
            };
            headerRow.AddChild(enableCheck);

            var nameLabel = ExpandHorizontal(new Label
            {
                Text = config.ClassName
            });
            nameLabel.AddThemeFontSizeOverride("font_size", 13);
            if (!config.Enabled)
            {
                nameLabel.AddThemeColorOverride("font_color", new Color(0.5f, 0.5f, 0.5f));
            }
            headerRow.AddChild(nameLabel);

            var deleteBtn = new Button
            {
                Text = "✕",
                TooltipText = "Remove this class from debug list",
                CustomMinimumSize = new Vector2(24, 24),
                Flat = true
            };
            deleteBtn.Pressed += () =>
            {
                RemoveDebugClass(capturedIndex);
            };
            headerRow.AddChild(deleteBtn);

            vbox.AddChild(headerRow);

            // Category toggles (collapsible inline)
            var categoriesContent = EditorUIUtils.CreateInlineCollapsible(
                vbox,
                "Categories",
                false,
                $"debug_categories_{config.ClassName}",
                _sectionExpanded
            );

            AddCategoryToggles(categoriesContent, config);

            panel.AddChild(vbox);
            return panel;
        }

        private void AddCategoryToggles(VBoxContainer container, ClassDebugConfig config)
        {
            var categories = Enum.GetValues(typeof(DebugCategory)).Cast<DebugCategory>()
                .Where(c => c != DebugCategory.None)
                .ToArray();

            // Create a grid of category checkboxes
            var grid = ExpandHorizontal(new GridContainer { Columns = 2 });
            grid.AddThemeConstantOverride("h_separation", 16);
            grid.AddThemeConstantOverride("v_separation", 2);

            foreach (var category in categories)
            {
                bool isEnabled = config.EnabledCategories.HasFlag(category);
                var check = new CheckBox
                {
                    Text = category.ToString(),
                    ButtonPressed = isEnabled
                };
                check.AddThemeFontSizeOverride("font_size", 11);
                check.Toggled += (v) =>
                {
                    if (v)
                        config.EnabledCategories |= category;
                    else
                        config.EnabledCategories &= ~category;
                    MarkDebugSettingsChanged();
                };
                grid.AddChild(check);
            }

            container.AddChild(grid);

            // Quick toggles
            var quickRow = new HBoxContainer();
            quickRow.AddThemeConstantOverride("separation", 8);

            var allBtn = new Button { Text = "All", Flat = true };
            allBtn.AddThemeFontSizeOverride("font_size", 10);
            allBtn.Pressed += () =>
            {
                DebugCategory allCategories = DebugCategory.None;
                foreach (DebugCategory cat in Enum.GetValues(typeof(DebugCategory)))
                {
                    if (cat != DebugCategory.None)
                        allCategories |= cat;
                }
                config.EnabledCategories = allCategories;
                MarkDebugSettingsChanged();
                RefreshDebugClassList();
            };
            quickRow.AddChild(allBtn);

            var noneBtn = new Button { Text = "None", Flat = true };
            noneBtn.AddThemeFontSizeOverride("font_size", 10);
            noneBtn.Pressed += () =>
            {
                config.EnabledCategories = DebugCategory.None;
                MarkDebugSettingsChanged();
                RefreshDebugClassList();
            };
            quickRow.AddChild(noneBtn);

            container.AddChild(quickRow);
        }

        private void AddDebugClass(string className)
        {
            var settings = Settings;
            if (settings == null) return;

            if (settings.ActiveDebugClasses.Any(c => c?.ClassName == className))
            {
                GD.Print($"[GlobalSettingsWindow] Debug class '{className}' already exists");
                return;
            }

            var config = new ClassDebugConfig(className)
            {
                Enabled = true,
                EnabledCategories = DebugCategory.None
            };

            settings.ActiveDebugClasses.Add(config);
            MarkDebugSettingsChanged();
            RefreshDebugClassList();
        }

        private void RemoveDebugClass(int index)
        {
            var settings = Settings;
            if (settings?.ActiveDebugClasses == null) return;
            if (index < 0 || index >= settings.ActiveDebugClasses.Count) return;

            settings.ActiveDebugClasses.RemoveAt(index);
            MarkDebugSettingsChanged();
            RefreshDebugClassList();
        }
        #endregion
    }
}