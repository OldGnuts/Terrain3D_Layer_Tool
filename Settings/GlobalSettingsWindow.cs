using System;
using System.Collections.Generic;
using Godot;
using Terrain3DTools.Core;
using Terrain3DTools.Editor.Utils;
using Terrain3DTools.Settings;
using static Godot.Control;

namespace Terrain3DTools.Editor
{
    /// <summary>
    /// Global settings window for Terrain3DTools.
    /// Uses a tab-based layout for organizing settings into logical groups.
    /// </summary>
    [Tool]
    public partial class GlobalSettingsWindow : Window
    {
        #region Constants
        private const int WINDOW_WIDTH = 550;
        private const int WINDOW_HEIGHT = 700;
        #endregion

        #region Fields
        private TabContainer _tabContainer;
        private Dictionary<string, VBoxContainer> _tabContents = new();
        private Dictionary<string, bool> _sectionExpanded = new(); // For inline collapsibles within tabs
        private EditorWindowTracker _windowTracker = new();

        // Reference to terrain system for texture access
        private WeakRef _terrainLayerManagerRef;

        // Track if settings have been modified since last save
        private bool _hasUnsavedChanges = false;
        private Button _saveButton;
        #endregion

        #region Properties
        private TerrainLayerManager TerrainLayerManager
        {
            get
            {
                if (_terrainLayerManagerRef?.GetRef().Obj is TerrainLayerManager manager &&
                    GodotObject.IsInstanceValid(manager))
                {
                    return manager;
                }
                return null;
            }
        }

        private GlobalToolSettings Settings => GlobalToolSettingsManager.Current;
        #endregion

        #region Static Factory
        /// <summary>
        /// Creates and shows the global settings window.
        /// </summary>
        public static GlobalSettingsWindow Show(Control parent, TerrainLayerManager terrainManager = null)
        {
            // Check if window already exists
            var existingWindow = parent.GetTree()?.Root?.GetNodeOrNull<GlobalSettingsWindow>("GlobalSettingsWindow");
            if (existingWindow != null && GodotObject.IsInstanceValid(existingWindow))
            {
                existingWindow.GrabFocus();
                return existingWindow;
            }

            var window = new GlobalSettingsWindow();
            window.Name = "GlobalSettingsWindow";
            window._terrainLayerManagerRef = terrainManager != null ? GodotObject.WeakRef(terrainManager) : null;

            parent.AddChild(window);
            window.BuildUI();
            window.PopupCentered();

            return window;
        }
        #endregion

        #region Initialization
        public GlobalSettingsWindow()
        {
            Title = "⚙️ Terrain Tools Settings";
            Size = new Vector2I(WINDOW_WIDTH, WINDOW_HEIGHT);
            Exclusive = false;
            Transient = false;
            MinSize = new Vector2I(450, 500);

            CloseRequested += OnCloseRequested;
        }

        private void BuildUI()
        {
            // Initialize inline collapsible states (for subsections within tabs)
            _sectionExpanded = new Dictionary<string, bool>();

            // Background panel to match editor theme
            var backgroundPanel = new PanelContainer();
            backgroundPanel.SetAnchorsPreset(LayoutPreset.FullRect);
            var bgStyle = new StyleBoxFlat
            {
                BgColor = new Color(0.15f, 0.15f, 0.17f)
            };
            backgroundPanel.AddThemeStyleboxOverride("panel", bgStyle);
            AddChild(backgroundPanel);

            // Main margin container
            var margin = new MarginContainer();
            margin.SetAnchorsPreset(LayoutPreset.FullRect);
            margin.AddThemeConstantOverride("margin_left", 12);
            margin.AddThemeConstantOverride("margin_right", 12);
            margin.AddThemeConstantOverride("margin_top", 12);
            margin.AddThemeConstantOverride("margin_bottom", 12);

            var outerVBox = ExpandFill(new VBoxContainer());
            outerVBox.AddThemeConstantOverride("separation", 8);

            // Header
            AddHeader(outerVBox);

            // Tab Container with custom styling
            _tabContainer = ExpandFill(new TabContainer());

            // Style the tab container panel (the content area behind tabs)
            var tabPanelStyle = new StyleBoxFlat
            {
                BgColor = new Color(0.15f, 0.15f, 0.17f),
                ContentMarginLeft = 0,
                ContentMarginRight = 0,
                ContentMarginTop = 0,
                ContentMarginBottom = 0
            };
            _tabContainer.AddThemeStyleboxOverride("panel", tabPanelStyle);

            // Style the tab bar background
            var tabBarBg = new StyleBoxFlat
            {
                BgColor = new Color(0.12f, 0.12f, 0.14f),
                ContentMarginLeft = 4,
                ContentMarginRight = 4,
                ContentMarginTop = 2,
                ContentMarginBottom = 2
            };
            _tabContainer.AddThemeStyleboxOverride("tabbar_background", tabBarBg);

            // Create tabs
            CreateGlobalTab();
            CreateTexturingTab();
            CreatePathsTab();
            CreateVisualizationTab();
            CreateDebugTab();

            outerVBox.AddChild(_tabContainer);

            // Footer with buttons
            AddFooter(outerVBox);

            margin.AddChild(outerVBox);
            backgroundPanel.AddChild(margin);
        }

        private void AddHeader(VBoxContainer parent)
        {
            var headerPanel = new PanelContainer();
            var style = new StyleBoxFlat
            {
                BgColor = new Color(0.15f, 0.2f, 0.3f, 0.8f),
                CornerRadiusBottomLeft = 4,
                CornerRadiusBottomRight = 4,
                CornerRadiusTopLeft = 4,
                CornerRadiusTopRight = 4,
                ContentMarginLeft = 12,
                ContentMarginRight = 12,
                ContentMarginTop = 8,
                ContentMarginBottom = 8
            };
            headerPanel.AddThemeStyleboxOverride("panel", style);

            var headerVBox = new VBoxContainer();
            headerVBox.AddThemeConstantOverride("separation", 4);

            var titleLabel = new Label
            {
                Text = "Terrain3D Tools - Global Settings",
                HorizontalAlignment = HorizontalAlignment.Center
            };
            titleLabel.AddThemeFontSizeOverride("font_size", 16);
            headerVBox.AddChild(titleLabel);

            var descLabel = new Label
            {
                Text = "Configure global settings for all terrain tools. Changes apply immediately.\nClick Save to persist settings to disk.",
                HorizontalAlignment = HorizontalAlignment.Center,
                AutowrapMode = TextServer.AutowrapMode.Word
            };
            descLabel.AddThemeColorOverride("font_color", new Color(0.7f, 0.7f, 0.7f));
            descLabel.AddThemeFontSizeOverride("font_size", 11);
            headerVBox.AddChild(descLabel);

            headerPanel.AddChild(headerVBox);
            parent.AddChild(headerPanel);
        }

        private void AddFooter(VBoxContainer parent)
        {
            var footerContainer = new HBoxContainer();
            footerContainer.AddThemeConstantOverride("separation", 8);

            // Reset to Defaults button
            var resetButton = new Button
            {
                Text = "Reset to Defaults",
                TooltipText = "Reset all settings to their default values"
            };
            resetButton.Pressed += OnResetToDefaults;
            footerContainer.AddChild(resetButton);

            // Spacer
            footerContainer.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });

            // Save button
            _saveButton = new Button
            {
                Text = "💾 Save",
                TooltipText = "Save settings to disk"
            };
            _saveButton.Pressed += OnSavePressed;
            footerContainer.AddChild(_saveButton);

            // Close button
            var closeButton = new Button
            {
                Text = "Close"
            };
            closeButton.Pressed += () => Hide();
            footerContainer.AddChild(closeButton);

            parent.AddChild(footerContainer);
        }
        #endregion

        #region Layout Helpers
        /// <summary>
        /// Configures a container to expand both horizontally and vertically.
        /// </summary>
        private static T ExpandFill<T>(T control) where T : Control
        {
            control.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            control.SizeFlagsVertical = SizeFlags.ExpandFill;
            return control;
        }

        /// <summary>
        /// Configures a container to expand horizontally only.
        /// </summary>
        private static T ExpandHorizontal<T>(T control) where T : Control
        {
            control.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            return control;
        }
        #endregion

        #region Tab Creation Helpers
        /// <summary>
        /// Creates a tab with a scroll container and returns the content VBoxContainer.
        /// </summary>
        private VBoxContainer CreateTab(string tabName)
        {
            var scrollContainer = ExpandFill(new ScrollContainer
            {
                HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled
            });
            scrollContainer.Name = tabName;

            var contentContainer = ExpandHorizontal(new VBoxContainer());
            contentContainer.AddThemeConstantOverride("separation", EditorConstants.SECTION_SPACING);

            // Add padding inside the scroll container
            var marginContainer = ExpandFill(new MarginContainer());
            marginContainer.AddThemeConstantOverride("margin_left", 4);
            marginContainer.AddThemeConstantOverride("margin_right", 4);
            marginContainer.AddThemeConstantOverride("margin_top", 8);
            marginContainer.AddThemeConstantOverride("margin_bottom", 8);

            marginContainer.AddChild(contentContainer);
            scrollContainer.AddChild(marginContainer);
            _tabContainer.AddChild(scrollContainer);

            _tabContents[tabName] = contentContainer;
            return contentContainer;
        }

        private void MarkSettingsChanged()
        {
            _hasUnsavedChanges = true;
            GlobalToolSettingsManager.NotifySettingsChanged();
            UpdateSaveButtonState();
        }

        private void UpdateSaveButtonState()
        {
            if (_saveButton != null)
            {
                _saveButton.Text = _hasUnsavedChanges ? "💾 Save*" : "💾 Save";
            }
        }
        #endregion

        #region Event Handlers
        private void OnCloseRequested()
        {
            _windowTracker.CloseAll();
            Hide();
            QueueFree();
        }

        private void OnSavePressed()
        {
            if (GlobalToolSettingsManager.Save())
            {
                _hasUnsavedChanges = false;
                UpdateSaveButtonState();
            }
        }

        private void OnResetToDefaults()
        {
            var dialog = new ConfirmationDialog
            {
                Title = "Reset to Defaults",
                DialogText = "Are you sure you want to reset all settings to their default values?\n\nThis cannot be undone.",
                OkButtonText = "Reset",
                CancelButtonText = "Cancel"
            };

            dialog.Confirmed += () =>
            {
                GlobalToolSettingsManager.ResetToDefaults();
                RebuildAllTabs();
                _hasUnsavedChanges = false;
                UpdateSaveButtonState();
                dialog.QueueFree();
            };

            dialog.Canceled += () => dialog.QueueFree();

            AddChild(dialog);
            dialog.PopupCentered();
        }

        private void RebuildAllTabs()
        {
            // Clear and rebuild all tab contents
            foreach (var kvp in _tabContents)
            {
                var container = kvp.Value;
                if (container != null && GodotObject.IsInstanceValid(container))
                {
                    foreach (var child in container.GetChildren())
                    {
                        child.QueueFree();
                    }
                }
            }

            // Reset visualization container references
            _heightVizContainer = null;
            _textureVizContainer = null;
            _featureVizContainer = null;

            // Rebuild each tab's content
            if (_tabContents.TryGetValue("⚙️ Global", out var globalContent))
                PopulateGlobalSettingsTab(globalContent);

            if (_tabContents.TryGetValue("🎨 Texturing", out var texturingContent))
                PopulateTexturingSettingsTab(texturingContent);

            if (_tabContents.TryGetValue("🛤️ Paths", out var pathContent))
                PopulatePathSettingsTab(pathContent);

            if (_tabContents.TryGetValue("👁️ Visualization", out var vizContent))
                PopulateVisualizationTab(vizContent);

            if (_tabContents.TryGetValue("🐛 Debug", out var debugContent))
                PopulateDebugSettingsTab(debugContent);
        }
        #endregion
    }
}