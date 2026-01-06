// /Visuals/LayerVisualizer.cs
using Godot;
using Terrain3DTools.Utils;
using Terrain3DTools.Layers;
using Terrain3DTools.Core;
using Terrain3DTools.Settings;

namespace Terrain3DTools.Visuals
{
    [GlobalClass, Tool]
    public partial class LayerVisualizer : Node3D
    {
        private TerrainLayerBase _ownerLayer;
        private MeshInstance3D _visualMesh;
        private PlaneMesh _planeMesh;
        private ShaderMaterial _shaderMat;
        private bool _isSelected = false;
        private Vector2I _oldSize = Vector2I.Zero;

        // Rd Textures for visualization
        private Texture2Drd _rdTerrainHeight;
        private Texture2Drd _rdLayerMask;

        private CurveTexture _falloffCurveTexture;

        private Rid _lastTerrainHeightRid;
        private Rid _lastLayerMaskRid;

        public void Initialize(TerrainLayerBase owner)
        {
            _ownerLayer = owner;
            Name = "Visualizer";

            // Select shader based on layer type
            Shader shader = GetShaderForLayerType(_ownerLayer.GetLayerType());

            _shaderMat = new ShaderMaterial { Shader = shader };

            _planeMesh = new PlaneMesh();

            _visualMesh = new MeshInstance3D
            {
                Name = "DebugMesh",
                Mesh = _planeMesh,
                MaterialOverride = _shaderMat,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                Visible = false
            };

            _rdTerrainHeight = new Texture2Drd();
            _rdLayerMask = new Texture2Drd();

            AddChild(_visualMesh);

            // Subscribe to settings changes
            GlobalToolSettingsManager.SettingsChanged += OnSettingsChanged;

            // Apply initial settings
            ApplyVisualizationSettings();
        }

        private Shader GetShaderForLayerType(LayerType layerType)
        {
            return layerType switch
            {
                LayerType.Height => GD.Load<Shader>("res://addons/terrain_3d_tools/Shaders/DebugPreviewShader.gdshader"),
                LayerType.Texture => GD.Load<Shader>("res://addons/terrain_3d_tools/Shaders/DebugPreviewShaderTexture.gdshader"),
                LayerType.Feature => GD.Load<Shader>("res://addons/terrain_3d_tools/Shaders/DebugPreviewShaderFeature.gdshader"),
                _ => GD.Load<Shader>("res://addons/terrain_3d_tools/Shaders/DebugPreviewShader.gdshader")
            };
        }

        public override void _EnterTree()
        {
            if (Engine.IsEditorHint())
            {
                var selection = EditorInterface.Singleton?.GetSelection();
                if (selection != null)
                {
                    selection.SelectionChanged += OnSelectionChanged;
                    OnSelectionChanged();
                }
            }
        }

        public override void _ExitTree()
        {
            if (Engine.IsEditorHint())
            {
                var selection = EditorInterface.Singleton?.GetSelection();
                if (selection != null)
                {
                    selection.SelectionChanged -= OnSelectionChanged;
                }
            }

            // Unsubscribe from settings changes
            GlobalToolSettingsManager.SettingsChanged -= OnSettingsChanged;
        }

        public void Update()
        {
            if (Engine.IsEditorHint() && IsInstanceValid(_visualMesh))
            {
                // Re-check visibility based on current settings
                bool shouldBeVisible = _isSelected && IsVisualizationEnabled();
                _visualMesh.Visible = shouldBeVisible;

                if (_visualMesh.Visible)
                {
                    UpdateVisuals();
                }
            }
        }

        private void OnSettingsChanged()
        {
            ApplyVisualizationSettings();

            // Update visibility based on new settings
            if (_isSelected)
            {
                _visualMesh.Visible = IsVisualizationEnabled();
            }
        }

        /// <summary>
        /// Converts a Godot Color to a Vector3 for shader use.
        /// </summary>
        private static Vector3 ColorToVector3(Color color)
        {
            return new Vector3(color.R, color.G, color.B);
        }

        /// <summary>
        /// Applies visualization settings from GlobalToolSettings to the shader.
        /// </summary>
        private void ApplyVisualizationSettings()
        {
            if (_shaderMat == null || _ownerLayer == null) return;

            var settings = GlobalToolSettingsManager.Current;
            if (settings == null) return;

            var layerType = _ownerLayer.GetLayerType();

            switch (layerType)
            {
                case LayerType.Height:
                    _shaderMat.SetShaderParameter("positive_color", ColorToVector3(settings.HeightPositiveColor));
                    _shaderMat.SetShaderParameter("negative_color", ColorToVector3(settings.HeightNegativeColor));
                    _shaderMat.SetShaderParameter("base_opacity", settings.HeightVisualizationOpacity);
                    break;

                case LayerType.Texture:
                    _shaderMat.SetShaderParameter("base_color", ColorToVector3(settings.TextureBaseColor));
                    _shaderMat.SetShaderParameter("highlight_color", ColorToVector3(settings.TextureHighlightColor));
                    _shaderMat.SetShaderParameter("base_opacity", settings.TextureVisualizationOpacity);
                    break;

                case LayerType.Feature:
                    _shaderMat.SetShaderParameter("positive_color", ColorToVector3(settings.FeaturePositiveColor));
                    _shaderMat.SetShaderParameter("negative_color", ColorToVector3(settings.FeatureNegativeColor));
                    _shaderMat.SetShaderParameter("preview_opacity", settings.FeatureVisualizationOpacity);
                    _shaderMat.SetShaderParameter("show_contours", settings.FeatureShowContours);
                    _shaderMat.SetShaderParameter("contour_spacing", settings.FeatureContourSpacing);
                    break;
            }
        }

        /// <summary>
        /// Checks if visualization is enabled for this layer's type.
        /// </summary>
        private bool IsVisualizationEnabled()
        {
            if (_ownerLayer == null) return false;

            var settings = GlobalToolSettingsManager.Current;
            if (settings == null) return true; // Default to enabled if no settings

            return _ownerLayer.GetLayerType() switch
            {
                LayerType.Height => settings.EnableHeightLayerVisualization,
                LayerType.Texture => settings.EnableTextureLayerVisualization,
                LayerType.Feature => settings.EnableFeatureLayerVisualization,
                _ => true
            };
        }

        private void UpdateVisuals()
        {
            if (!IsInstanceValid(_ownerLayer) || !IsInstanceValid(_visualMesh)) return;

            // Check if visualization is enabled for this layer type
            if (!IsVisualizationEnabled())
            {
                _visualMesh.Visible = false;
                return;
            }

            var (worldMin, worldMax) = _ownerLayer.GetWorldBounds();
            Vector2 worldCenter = (worldMin + worldMax) * 0.5f;

            bool sizeChanged = _oldSize != _ownerLayer.Size;

            if (sizeChanged)
            {
                _oldSize = _ownerLayer.Size;
                _planeMesh.Size = new Vector2(_ownerLayer.Size.X, _ownerLayer.Size.Y);
                _planeMesh.SubdivideDepth = Mathf.Min(_ownerLayer.Size.Y, 256);
                _planeMesh.SubdivideWidth = Mathf.Min(_ownerLayer.Size.X, 256);
            }

            _visualMesh.GlobalPosition = new Vector3(worldCenter.X, 0, worldCenter.Y);

            // 1. UPDATE GEOMETRY TEXTURE (Terrain Height)
            if (_ownerLayer.layerHeightVisualizationTextureRID.IsValid)
            {
                UpdateSharedTexture(ref _rdTerrainHeight, _ownerLayer.layerHeightVisualizationTextureRID, ref _lastTerrainHeightRid);
                _shaderMat.SetShaderParameter("terrain_height_tex", _rdTerrainHeight);
                _shaderMat.SetShaderParameter("use_terrain_height", true);
            }
            else
            {
                _shaderMat.SetShaderParameter("use_terrain_height", false);
            }

            // 2. UPDATE MASK TEXTURE (Layer Data)
            if (_ownerLayer.layerTextureRID.IsValid)
            {
                UpdateSharedTexture(ref _rdLayerMask, _ownerLayer.layerTextureRID, ref _lastLayerMaskRid);
                _shaderMat.SetShaderParameter("layer_data_tex", _rdLayerMask);
            }

            // 3. SET LAYER-TYPE-SPECIFIC PARAMETERS
            SetLayerTypeParameters();

            // 4. APPLY VISUALIZATION SETTINGS (colors, opacity)
            ApplyVisualizationSettings();
        }

        private void SetLayerTypeParameters()
        {
            float visualStrength = 1.0f;

            if (_ownerLayer is HeightLayer heightLayer)
            {
                visualStrength = heightLayer.Strength;
                if (heightLayer.Operation == HeightLayer.HeightOperation.Subtract)
                {
                    visualStrength *= -1.0f;
                }
                _shaderMat.SetShaderParameter("layer_strength", visualStrength);
            }
            else if (_ownerLayer is TextureLayer textureLayer)
            {
                if (textureLayer.FalloffApplyMode == FalloffApplication.ApplyToResult)
                {
                    _shaderMat.SetShaderParameter("apply_falloff", true);
                    _shaderMat.SetShaderParameter("falloff_type", (int)textureLayer.FalloffMode);
                    _shaderMat.SetShaderParameter("falloff_strength", textureLayer.FalloffStrength);

                    if (_falloffCurveTexture == null)
                    {
                        _falloffCurveTexture = new CurveTexture();
                    }
                    _falloffCurveTexture.Curve = textureLayer.FalloffCurve;
                    _shaderMat.SetShaderParameter("falloff_curve_tex", _falloffCurveTexture);
                }
                else
                {
                    _shaderMat.SetShaderParameter("apply_falloff", false);
                }
            }
            else if (_ownerLayer is FeatureLayer featureLayer)
            {
                _shaderMat.SetShaderParameter("layer_strength", 1.0f);
            }
            else
            {
                _shaderMat.SetShaderParameter("layer_strength", visualStrength);
            }
        }

        private void UpdateSharedTexture(ref Texture2Drd target, Rid sourceRid, ref Rid lastSourceRid)
        {
            if (target == null) target = new Texture2Drd();

            bool sizeChanged = _oldSize != _ownerLayer.Size;
            bool ridChanged = lastSourceRid != sourceRid;

            if (!target.TextureRdRid.IsValid || sizeChanged || ridChanged)
            {
                target.TextureRdRid = TextureUtil.CreateSharedRenderingDeviceTextureRD(
                    sourceRid,
                    (ulong)_ownerLayer.Size.X,
                    (ulong)_ownerLayer.Size.Y);

                lastSourceRid = sourceRid;
            }
        }

        private void OnSelectionChanged()
        {
            var selection = EditorInterface.Singleton?.GetSelection();
            if (selection == null || !IsInstanceValid(_ownerLayer) || !IsInstanceValid(_visualMesh)) return;

            _isSelected = selection.GetSelectedNodes().Contains(_ownerLayer);

            // Only show if selected AND visualization is enabled for this layer type
            _visualMesh.Visible = _isSelected && IsVisualizationEnabled();

            // Notify the manager about selection change
            var manager = TerrainHeightQuery.GetTerrainLayerManager();
            if (manager != null)
            {
                if (_isSelected)
                    manager.SetSelectedLayer(_ownerLayer);
            }

            if (_isSelected && _visualMesh.Visible)
            {
                UpdateVisuals();
            }
        }
    }
}