// /Editor/TextureLayerInspector.cs
using System;
using System.Collections.Generic;
using Godot;
using Terrain3DTools.Layers;
using Terrain3DTools.Editor.Utils;
using TokisanGames;

namespace Terrain3DTools.Editor
{
    /// <summary>
    /// Custom inspector for TextureLayer nodes.
    /// 
    /// Uses InspectorPropertyBinder for clean property binding and
    /// collapsible sections for organized UI.
    /// 
    /// Split across partial classes:
    /// - TextureLayerInspector.cs (this file): Core lifecycle, section management
    /// - TextureLayerInspector.TextureSelection.cs: Texture selection, zone visualization
    /// - TextureLayerInspector.BlendSettings.cs: Blend, noise, smoothing settings
    /// - TextureLayerInspector.Exclusions.cs: Excluded texture management
    /// </summary>
    [Tool, GlobalClass]
    public partial class TextureLayerInspector : EditorInspectorPlugin
    {
        #region Constants

        private const int THUMBNAIL_SIZE = 48;
        private const int LARGE_THUMBNAIL_SIZE = 64;

        #endregion

        #region State

        private ulong _currentLayerInstanceId;
        private VBoxContainer _mainContainer;
        private InspectorPropertyBinder _binder;

        // Section state tracking (persists across rebuilds)
        private Dictionary<string, bool> _sectionExpanded = new();
        private Dictionary<string, VBoxContainer> _sectionContents = new();

        // Signal tracking
        private bool _signalsConnected = false;

        #endregion

        #region Properties

        /// <summary>
        /// Gets the current TextureLayer by instance ID.
        /// Returns null if the layer is no longer valid.
        /// </summary>
        private TextureLayer CurrentLayer
        {
            get
            {
                if (_currentLayerInstanceId == 0) return null;
                var obj = GodotObject.InstanceFromId(_currentLayerInstanceId);
                return obj as TextureLayer;
            }
        }

        /// <summary>
        /// Gets the Terrain3DAssets from the current terrain.
        /// </summary>
        private Terrain3DAssets CurrentTerrain3DAssets
            => TerrainAssetSelector.GetAssets(CurrentLayer);

        #endregion

        #region EditorInspectorPlugin Overrides

        public override bool _CanHandle(GodotObject obj)
        {
            return obj is TextureLayer;
        }

        public override bool _ParseProperty(GodotObject @object, Variant.Type type, string name,
            PropertyHint hintType, string hintString, PropertyUsageFlags usageFlags, bool wide)
        {
            // Hide properties we handle in custom UI
            var hiddenProperties = new HashSet<string>
            {
                // Texture selection
                "TextureIndex", "ExcludedTextureIds",
                
                // Blend settings
                "BlendMode", "BlendStrength",
                
                // Gradient mode
                "GradientModeEnabled", "SecondaryTextureIndex", "TertiaryTextureIndex",
                
                // Zone thresholds (new)
                "TertiaryThreshold", "SecondaryThreshold", "PrimaryThreshold",
                
                // Transition widths (new)
                "TertiaryTransition", "SecondaryTransition", "PrimaryTransition",
                
                // Noise
                "EnableNoise", "NoiseAmount", "NoiseScale", "NoiseSeed",
                "NoiseType", "NoiseTexture", "EdgeAwareNoise", "EdgeNoiseFalloff",
                
                // Smoothing (updated)
                "EnableSmoothing", "BlendSmoothing", "BoundarySmoothing",
                "FalloffEdgeSmoothing", "SmoothingWindowSize"
            };

            return hiddenProperties.Contains(name);
        }

        public override void _ParseBegin(GodotObject obj)
        {
            if (obj is not TextureLayer textureLayer) return;

            ulong newInstanceId = textureLayer.GetInstanceId();

            // Handle layer change
            if (_currentLayerInstanceId != 0 && _currentLayerInstanceId != newInstanceId)
            {
                Cleanup();
            }

            _currentLayerInstanceId = newInstanceId;

            // Initialize state
            InitializeSectionStates();
            _binder = new InspectorPropertyBinder();
            _sectionContents.Clear();

            // Build main container
            _mainContainer = new VBoxContainer();
            _mainContainer.AddThemeConstantOverride("separation", 4);

            // Check terrain connection
            if (CurrentTerrain3DAssets == null)
            {
                AddTerrainWarning();
                AddCustomControl(_mainContainer);
                return;
            }

            // Build UI sections
            BuildTextureSelectionSection();
            _binder.AddSeparator(_mainContainer, 12);

            BuildBlendSettingsSection();
            _binder.AddSeparator(_mainContainer, 12);

            BuildNoiseSection();
            _binder.AddSeparator(_mainContainer, 12);

            BuildSmoothingSection();
            
            // Only show exclusions in non-gradient mode
            if (!textureLayer.GradientModeEnabled)
            {
                _binder.AddSeparator(_mainContainer, 12);
                BuildExclusionsSection();
            }

            AddCustomControl(_mainContainer);
            ConnectSignals();
        }

        public override void _ParseEnd(GodotObject obj)
        {
            // Don't cleanup here - inspector may just be refreshing
        }

        #endregion

        #region Initialization

        private void InitializeSectionStates()
        {
            if (_sectionExpanded.Count > 0) return;  // Already initialized

            _sectionExpanded = new Dictionary<string, bool>
            {
                { "Texture Selection", true },
                { "Secondary Texture", true },
                { "Tertiary Texture", true },
                { "Blend Settings", true },
                { "Noise & Variation", false },
                { "Blend Smoothing", false },
                { "Exclusions", false },
                { "Custom Noise Texture", false }
            };
        }

        #endregion

        #region Signal Management

        private void ConnectSignals()
        {
            if (_signalsConnected) return;
            _signalsConnected = true;
        }

        private void DisconnectSignals()
        {
            if (!_signalsConnected) return;
            _signalsConnected = false;
        }

        #endregion

        #region Cleanup

        private void Cleanup()
        {
            DisconnectSignals();
            _binder?.Clear();
            _sectionContents.Clear();
        }

        #endregion

        #region Section Helpers

        /// <summary>
        /// Creates a collapsible section with tracked state.
        /// </summary>
        private VBoxContainer CreateCollapsibleSection(string title, bool defaultExpanded = true)
        {
            bool isExpanded = _sectionExpanded.TryGetValue(title, out var expanded) ? expanded : defaultExpanded;

            var result = EditorUIUtils.CreateCollapsibleSection(
                _mainContainer,
                title,
                isExpanded,
                _sectionExpanded
            );

            _sectionContents[title] = result.container;
            return result.container;
        }

        /// <summary>
        /// Creates a nested collapsible section inside a parent.
        /// </summary>
        private VBoxContainer CreateNestedCollapsible(Control parent, string title, bool defaultExpanded = false)
        {
            bool isExpanded = _sectionExpanded.TryGetValue(title, out var expanded) ? expanded : defaultExpanded;

            return EditorUIUtils.CreateInlineCollapsible(
                parent,
                title,
                isExpanded,
                title,
                _sectionExpanded
            );
        }

        /// <summary>
        /// Gets a section's content container by title.
        /// </summary>
        private VBoxContainer GetSectionContent(string title)
        {
            return _sectionContents.TryGetValue(title, out var content) ? content : null;
        }

        #endregion

        #region Warning Display

        private void AddTerrainWarning()
        {
            var container = new VBoxContainer();
            container.AddThemeConstantOverride("separation", 8);

            var icon = new Label
            {
                Text = "⚠️",
                HorizontalAlignment = HorizontalAlignment.Center
            };
            icon.AddThemeFontSizeOverride("font_size", 24);
            container.AddChild(icon);

            var title = new Label
            {
                Text = "Terrain3D Connection Required",
                HorizontalAlignment = HorizontalAlignment.Center
            };
            title.AddThemeColorOverride("font_color", new Color(1.0f, 0.8f, 0.2f));
            title.AddThemeFontSizeOverride("font_size", 14);
            container.AddChild(title);

            var detail = new Label
            {
                Text = "TerrainLayerManager not found or Terrain3D not connected.\n" +
                       "Texture selection requires a valid Terrain3D connection.",
                AutowrapMode = TextServer.AutowrapMode.Word,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            detail.AddThemeColorOverride("font_color", new Color(0.7f, 0.7f, 0.7f));
            container.AddChild(detail);

            _mainContainer.AddChild(container);
        }

        #endregion

        #region Refresh

        /// <summary>
        /// Refreshes all UI from current property values.
        /// </summary>
        private void RefreshAll()
        {
            _binder?.RefreshAll();
            RefreshTextureDisplays();
            RefreshInfluenceVisualization();
        }

        /// <summary>
        /// Triggers a full inspector rebuild.
        /// </summary>
        private void RequestInspectorRebuild()
        {
            Callable.From(() =>
            {
                var layer = CurrentLayer;
                if (layer == null || !IsInstanceValid(layer)) return;
                layer.NotifyPropertyListChanged();
            }).CallDeferred();
        }

        #endregion

        #region Texture Info Helper

        /// <summary>
        /// Gets texture information from Terrain3DAssets.
        /// </summary>
        protected (int id, string name, ImageTexture thumbnail)? GetTextureInfo(Terrain3DAssets assets, int textureId)
        {
            if (assets == null || textureId < 0 || textureId >= assets.TextureList.Count)
                return null;

            var resource = assets.TextureList[textureId].As<Resource>();
            if (resource == null) return null;

            var asset = Terrain3DTextureAsset.Bind(resource);
            if (asset == null) return null;

            ImageTexture thumbnail = null;
            var thumbSource = asset.GetThumbnail();
            if (thumbSource != null)
            {
                var img = thumbSource.GetImage();
                if (img != null)
                {
                    img.Resize(LARGE_THUMBNAIL_SIZE, LARGE_THUMBNAIL_SIZE);
                    thumbnail = ImageTexture.CreateFromImage(img);
                }
            }

            return ((int)asset.Id, asset.Name, thumbnail);
        }

        /// <summary>
        /// Applies standard styling to texture preview rects.
        /// </summary>
        protected void ApplyTexturePreviewStyle(TextureRect preview)
        {
            var style = new StyleBoxFlat
            {
                BgColor = new Color(0.15f, 0.15f, 0.15f, 1.0f),
                BorderWidthBottom = 1,
                BorderWidthTop = 1,
                BorderWidthLeft = 1,
                BorderWidthRight = 1,
                BorderColor = new Color(0.3f, 0.3f, 0.3f, 1.0f),
                CornerRadiusBottomLeft = 4,
                CornerRadiusBottomRight = 4,
                CornerRadiusTopLeft = 4,
                CornerRadiusTopRight = 4,
                ContentMarginLeft = 4,
                ContentMarginRight = 4,
                ContentMarginTop = 4,
                ContentMarginBottom = 4
            };
        }

        #endregion

        #region Partial Method Declarations

        partial void BuildTextureSelectionSection();
        partial void BuildBlendSettingsSection();
        partial void BuildNoiseSection();
        partial void BuildSmoothingSection();
        partial void BuildExclusionsSection();
        partial void RefreshTextureDisplays();
        partial void RefreshInfluenceVisualization();
        partial void RefreshBlendSettings();
        partial void RefreshExclusions();

        #endregion
    }
}