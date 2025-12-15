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
    [Tool, GlobalClass]
    public partial class PathLayerInspector : EditorInspectorPlugin
    {
        #region Constants
        private const int THUMBNAIL_SIZE = 48;
        private const float PROFILE_PREVIEW_HEIGHT = 120f;
        #endregion

        #region State
        private ulong CurrentLayerInstanceId;
        private VBoxContainer _mainContainer;
        
        // Signal connection tracking
        private bool _profileSignalConnected = false;
        private bool _curveSignalConnected = false;
        private bool _transformTrackingActive = false;
        private Vector3 _lastKnownPosition;
        private Timer _refreshTimer;

        // UI State
        private Dictionary<string, bool> _sectionExpanded = new();
        private Dictionary<string, VBoxContainer> _sectionContents = new();
        #endregion

        #region Helper Properties
        private PathLayer CurrentLayer
        {
            get
            {
                if (CurrentLayerInstanceId == 0) return null;
                var obj = GodotObject.InstanceFromId(CurrentLayerInstanceId);
                return obj as PathLayer;
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
            // Properties we want to draw manually in our custom UI
            var hiddenProperties = new HashSet<string> { 
                "PathType", 
                "Profile", 
                "ResetProfileToDefault" 
            };

            if (hiddenProperties.Contains(name))
            {
                return true;
            }

            // Logic to hide specific properties based on PathType (if implemented in future)
            if (@object is PathLayer pathLayer)
            {
                if (ShouldHidePropertyForType(name, pathLayer.PathType))
                {
                    return true;
                }
            }

            return false;
        }

        public override void _ParseBegin(GodotObject obj)
        {
            if (obj is not PathLayer pathLayer) return;

            ulong newInstanceId = pathLayer.GetInstanceId();

            // If switching to a different layer, disconnect from old one first
            if (CurrentLayerInstanceId != 0 && CurrentLayerInstanceId != newInstanceId)
            {
                DisconnectSignals();
            }

            CurrentLayerInstanceId = newInstanceId;
            _selectedZoneIndex = 0;
            _lastKnownPosition = pathLayer.GlobalPosition;

            // Initialize main container
            _mainContainer = new VBoxContainer();
            _mainContainer.AddThemeConstantOverride("separation", 8);

            // Initialize section states if this is the first load
            if (_sectionExpanded.Count == 0)
            {
                _sectionExpanded = new Dictionary<string, bool>
                {
                    { "Path Type", true },
                    { "Path Curve", true },
                    { "Cross-Section Profile", true },
                    { "Zone Editor", true },
                    { "Preview", false },
                    { "Path Points", false }
                };
            }
            _sectionContents.Clear();

            // Build UI Structure using partial methods
            AddPathTypeSelector();
            EditorUIUtils.AddSeparator(_mainContainer);
            
            AddCurveSection();
            AddPathPointsSection();
            EditorUIUtils.AddSeparator(_mainContainer);
            
            AddProfileSection();
            EditorUIUtils.AddSeparator(_mainContainer);
            
            AddZoneEditor();
            AddCustomControl(_mainContainer);

            // Connect to layer changes
            ConnectSignals();
            
            // Start transform tracking
            StartTransformTracking();
        }

        public override void _ParseEnd(GodotObject obj)
        {
            // Note: We don't clear CurrentLayerInstanceId here because we might need it 
            // if the inspector is just refreshing, not deselecting.
        }
        #endregion

        #region Transform Tracking
        private void StartTransformTracking()
        {
            if (_transformTrackingActive) return;
            
            // Create a timer to poll for transform changes
            // This is more reliable than trying to connect to transform signals
            _refreshTimer = new Timer
            {
                WaitTime = 0.1, // Check every 100ms
                OneShot = false,
                Autostart = false
            };
            _refreshTimer.Timeout += OnRefreshTimerTimeout;
            
            // Add timer to the editor base so it persists
            var editorBase = EditorInterface.Singleton.GetBaseControl();
            editorBase.AddChild(_refreshTimer);
            _refreshTimer.Start();
            
            _transformTrackingActive = true;
        }

        private void StopTransformTracking()
        {
            if (!_transformTrackingActive) return;
            
            if (_refreshTimer != null && IsInstanceValid(_refreshTimer))
            {
                _refreshTimer.Stop();
                _refreshTimer.Timeout -= OnRefreshTimerTimeout;
                _refreshTimer.QueueFree();
                _refreshTimer = null;
            }
            
            _transformTrackingActive = false;
        }

        private void OnRefreshTimerTimeout()
        {
            var layer = CurrentLayer;
            if (layer == null || !IsInstanceValid(layer))
            {
                StopTransformTracking();
                return;
            }

            // Check if transform has changed
            if (layer.GlobalPosition != _lastKnownPosition)
            {
                _lastKnownPosition = layer.GlobalPosition;
                RefreshPointList();
            }
        }
        #endregion

        #region Signal Management
        private void ConnectSignals()
        {
            var layer = CurrentLayer;
            if (layer == null) return;

            if (!_profileSignalConnected && layer.Profile != null)
            {
                layer.Profile.Changed += OnProfileChanged;
                _profileSignalConnected = true;
            }

            if (!_curveSignalConnected && layer.Curve != null)
            {
                layer.Curve.Changed += OnCurveChanged;
                _curveSignalConnected = true;
            }
        }

        private void DisconnectSignals()
        {
            StopTransformTracking();
            
            var layer = CurrentLayer;
            if (layer == null) return;

            try
            {
                if (_profileSignalConnected && layer.Profile != null)
                {
                    layer.Profile.Changed -= OnProfileChanged;
                    _profileSignalConnected = false;
                }

                if (_curveSignalConnected && layer.Curve != null)
                {
                    layer.Curve.Changed -= OnCurveChanged;
                    _curveSignalConnected = false;
                }
            }
            catch
            {
                // Ignore disconnect errors during cleanup
                _profileSignalConnected = false;
                _curveSignalConnected = false;
            }
        }

        private void OnProfileChanged()
        {
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
        /// <summary>
        /// Creates a collapsible section in the main container and stores the content reference.
        /// </summary>
        private void CreateCollapsibleSection(string title, bool defaultExpanded)
        {
            // Use existing state if we have it, otherwise default
            bool isExpanded = _sectionExpanded.ContainsKey(title) ? _sectionExpanded[title] : defaultExpanded;
            
            var result = EditorUIUtils.CreateCollapsibleSection(
                _mainContainer, 
                title, 
                isExpanded, 
                _sectionExpanded
            );
            
            _sectionContents[title] = result.container;
        }

        private VBoxContainer GetSectionContent(string title)
        {
            return _sectionContents.TryGetValue(title, out var content) ? content : null;
        }

        private bool ShouldHidePropertyForType(string propertyName, PathType type)
        {
            // List of properties that are managed by Godot's default inspector 
            // and should NOT be hidden.
            var alwaysShow = new HashSet<string>
            {
                "LayerName", "Size", "Masks", "FalloffStrength", "FalloffCurve", "FalloffMode",
                "DebugMode", "ShowProfileCrossSection",
                "SmoothCorners", "CornerSmoothing",
                "Resolution", "AdaptiveResolution", "AdaptiveMinAngle"
            };

            if (alwaysShow.Contains(propertyName))
                return false;

            // In the future, specific properties can be hidden here based on PathType
            // e.g., if (type == PathType.River && propertyName == "Banking") return true;

            return false;
        }
        #endregion
    }
}