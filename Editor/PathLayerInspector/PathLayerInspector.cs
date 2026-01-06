// /Editor/PathLayerInspector.cs
using System;
using System.Collections.Generic;
using Godot;
using Terrain3DTools.Layers.Path;
using Terrain3DTools.Layers;
using Terrain3DTools.Editor.Utils;
using TokisanGames;

namespace Terrain3DTools.Editor
{
    /// <summary>
    /// Custom inspector plugin for PathLayer nodes.
    /// Provides specialized UI for path type selection, curve editing, and profile management.
    /// </summary>
    [Tool, GlobalClass]
    public partial class PathLayerInspector : EditorInspectorPlugin
    {
        #region State
        private ulong _currentLayerInstanceId;
        private VBoxContainer _mainContainer;

        // Signal connection tracking
        private bool _profileSignalConnected = false;
        private bool _curveSignalConnected = false;

        // UI State
        private Dictionary<string, bool> _sectionExpanded = new();
        private Dictionary<string, VBoxContainer> _sectionContents = new();

        // Window tracking
        private EditorWindowTracker _windowTracker = new();
        #endregion

        #region Helper Properties
        private PathLayer CurrentLayer
        {
            get
            {
                if (_currentLayerInstanceId == 0) return null;
                var obj = GodotObject.InstanceFromId(_currentLayerInstanceId);
                if (obj != null && GodotObject.IsInstanceValid(obj) && obj is PathLayer layer)
                    return layer;
                return null;
            }
        }

        private Terrain3DAssets CurrentTerrain3DAssets
            => TerrainAssetSelector.GetAssets(CurrentLayer);
        #endregion

        #region EditorInspectorPlugin Overrides
        public override bool _CanHandle(GodotObject obj)
        {
            return obj is PathLayer;
        }

        public override bool _ParseProperty(GodotObject @object, Variant.Type type, string name,
            PropertyHint hintType, string hintString, PropertyUsageFlags usageFlags, bool wide)
        {
            if (@object is not PathLayer) return false;

            // Properties we explicitly allow Godot to draw (if any)
            var allowedProperties = new HashSet<string>
            {
                // Add any properties you want Godot's default inspector to handle
                // Currently empty - we handle everything custom
            };

            if (allowedProperties.Contains(name))
            {
                return false; // Let Godot draw it
            }

            // Hide everything else - we draw it in our custom UI
            return true;
        }

        public override void _ParseBegin(GodotObject obj)
        {
            if (obj is not PathLayer pathLayer) return;

            ulong newInstanceId = pathLayer.GetInstanceId();

            if (_currentLayerInstanceId != 0 && _currentLayerInstanceId != newInstanceId)
            {
                CleanupConnections();
            }

            _currentLayerInstanceId = newInstanceId;
            _selectedZoneIndex = 0;

            _mainContainer = new VBoxContainer();
            _mainContainer.AddThemeConstantOverride("separation", EditorConstants.SECTION_SPACING);

            // Initialize section expanded states - all collapsed by default to reduce scrolling
            if (_sectionExpanded.Count == 0)
            {
                _sectionExpanded = new Dictionary<string, bool>
        {
            { "Path Type", false },
            { "Path Curve", false },
            { "Cross-Section Profile", false },
            { "Zone Editor", false },
            { "Elevation Constraints", false },
            { "Smoothing", false },
            { "Global Falloff", false },
            { "Debug", false },
            { "Path Points", false },
            { "Noise Settings", false },
            { "Constraint Violations", false }
        };
            }
            _sectionContents.Clear();

            // Build UI Structure
            AddInfoBanner();
            AddPathTypeSelector();
            EditorUIUtils.AddSeparator(_mainContainer);

            AddCurveSection();  // Path Points is now nested inside this
            EditorUIUtils.AddSeparator(_mainContainer);

            AddProfileSection();
            EditorUIUtils.AddSeparator(_mainContainer);

            AddZoneEditor();
            EditorUIUtils.AddSeparator(_mainContainer);

            AddConstraintSection();
            EditorUIUtils.AddSeparator(_mainContainer);

            AddSmoothingSection();
            EditorUIUtils.AddSeparator(_mainContainer);

            AddFalloffSection();
            EditorUIUtils.AddSeparator(_mainContainer);

            AddDebugSection();

            AddCustomControl(_mainContainer);

            ConnectSignals();
        }

        public override void _ParseEnd(GodotObject obj)
        {
            // Don't clean up here - the layer might just be refreshing
        }
        #endregion

        #region Connection Management
        private void ConnectSignals()
        {
            var layer = CurrentLayer;
            if (layer == null) return;

            // Connect to profile
            if (!_profileSignalConnected && layer.Profile != null)
            {
                layer.Profile.Changed += OnProfileChanged;
                _profileSignalConnected = true;
            }

            // Connect to curve (from external editor)
            if (!_curveSignalConnected)
            {
                var curve = layer.Curve;
                if (curve != null)
                {
                    curve.Changed += OnCurveChanged;
                    _curveSignalConnected = true;
                }
                else
                {
                    CallDeferred(nameof(DeferredCurveConnect));
                }
            }
        }

        private void DeferredCurveConnect()
        {
            var layer = CurrentLayer;
            if (layer == null || _curveSignalConnected) return;

            var curve = layer.Curve;
            if (curve != null)
            {
                curve.Changed += OnCurveChanged;
                _curveSignalConnected = true;
                RefreshCurveStats();
            }
        }

        private void CleanupConnections()
        {
            _windowTracker.CloseAll();

            // Disconnect from old layer's signals
            var oldObj = GodotObject.InstanceFromId(_currentLayerInstanceId);
            if (oldObj != null && GodotObject.IsInstanceValid(oldObj) && oldObj is PathLayer oldLayer)
            {
                if (_profileSignalConnected && oldLayer.Profile != null)
                {
                    try { oldLayer.Profile.Changed -= OnProfileChanged; }
                    catch { /* Ignore disconnect errors */ }
                }

                if (_curveSignalConnected)
                {
                    var curve = oldLayer.Curve;
                    if (curve != null)
                    {
                        try { curve.Changed -= OnCurveChanged; }
                        catch { /* Ignore disconnect errors */ }
                    }
                }
            }

            _profileSignalConnected = false;
            _curveSignalConnected = false;
        }

        private void OnProfileChanged()
        {
            MarkProfileModified();
            CallDeferred(nameof(RefreshProfileUI));
        }

        private void OnCurveChanged()
        {
            CallDeferred(nameof(RefreshCurveStats));
        }

        private void RefreshProfileUI()
        {
            var layer = CurrentLayer;
            if (layer == null) return;

            RefreshProfilePreview();
            RefreshZoneList();
            RefreshZoneEditor();
            UpdateProfileHeader();
        }
        #endregion

        #region UI Helpers
        private void AddInfoBanner()
        {
            var banner = new PanelContainer();
            var style = new StyleBoxFlat
            {
                BgColor = new Color(0.2f, 0.3f, 0.4f, 0.5f),
                CornerRadiusBottomLeft = 4,
                CornerRadiusBottomRight = 4,
                CornerRadiusTopLeft = 4,
                CornerRadiusTopRight = 4,
                ContentMarginLeft = 8,
                ContentMarginRight = 8,
                ContentMarginTop = 4,
                ContentMarginBottom = 4
            };
            banner.AddThemeStyleboxOverride("panel", style);

            var label = new Label
            {
                Text = "ℹ️ To create or edit paths see the help window in Path Curve or use the Path3D child node.",
                AutowrapMode = TextServer.AutowrapMode.Word
            };
            label.AddThemeFontSizeOverride("font_size", 11);
            banner.AddChild(label);

            _mainContainer.AddChild(banner);
        }

        private void CreateCollapsibleSection(string title, bool defaultExpanded, string helpTitle = null, string helpContent = null)
        {
            bool isExpanded = _sectionExpanded.TryGetValue(title, out bool expanded) ? expanded : defaultExpanded;

            var result = EditorUIUtils.CreateCollapsibleSectionWithHelp(
                _mainContainer,
                title,
                isExpanded,
                _sectionExpanded,
                helpTitle,
                helpContent
            );

            _sectionContents[title] = result.container;
        }

        private VBoxContainer GetSectionContent(string title)
        {
            return _sectionContents.TryGetValue(title, out var content) ? content : null;
        }

        internal Window CreateTrackedWindow(string title, Vector2I size)
        {
            return _windowTracker.CreateTrackedWindow(title, size);
        }

        private bool ShouldHidePropertyForType(string propertyName, PathType type)
        {
            // Currently no type-specific hiding needed
            return false;
        }
        #endregion
    }
}