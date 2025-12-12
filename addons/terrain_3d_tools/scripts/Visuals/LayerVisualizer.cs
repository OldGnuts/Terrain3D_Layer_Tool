// /Visuals/LayerVisualizer.cs
using Godot;
using Terrain3DTools.Utils;
using Terrain3DTools.Layers;
using Terrain3DTools.Core;

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

        // -----------------------------------------
        // Rd Textures for visualization
        private Texture2Drd _rdTerrainHeight; // The Geometry (Terrain Shape)
        private Texture2Drd _rdLayerMask;     // The Data (Layer Influence/Delta)
        //------------------------------------------

        public void Initialize(TerrainLayerBase owner)
        {
            _ownerLayer = owner;
            Name = "Visualizer";

            // Select shader based on layer type (mostly for different color schemes)
            Shader shader = _ownerLayer.GetLayerType() == LayerType.Texture
                ? GD.Load<Shader>("res://addons/terrain_3d_tools/Shaders/DebugPreviewShaderTexture.gdshader")
                : GD.Load<Shader>("res://addons/terrain_3d_tools/Shaders/DebugPreviewShader.gdshader");

            _shaderMat = new ShaderMaterial { Shader = shader };

            // Start with a reasonably detailed mesh
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
        }

        public void Update()
        {
            if (Engine.IsEditorHint() && IsInstanceValid(_visualMesh) && _visualMesh.Visible)
            {
                UpdateVisuals();
            }
        }

        private void UpdateVisuals()
        {
            if (!IsInstanceValid(_ownerLayer) || !IsInstanceValid(_visualMesh)) return;

            bool sizeChanged = _oldSize != _ownerLayer.Size;
            if (sizeChanged)
            {
                _oldSize = _ownerLayer.Size;
                _planeMesh.Size = new Vector2(_ownerLayer.Size.X, _ownerLayer.Size.Y);

                // High subdivision allows the vertex shader to conform to the terrain shape
                _planeMesh.SubdivideDepth = Mathf.Min(_ownerLayer.Size.Y, 256);
                _planeMesh.SubdivideWidth = Mathf.Min(_ownerLayer.Size.X, 256);
            }

            // 1. UPDATE GEOMETRY TEXTURE (Terrain Height)
            if (_ownerLayer.layerHeightVisualizationTextureRID.IsValid)
            {
                GD.Print("Using height data");
                UpdateSharedTexture(ref _rdTerrainHeight, _ownerLayer.layerHeightVisualizationTextureRID);
                _shaderMat.SetShaderParameter("terrain_height_tex", _rdTerrainHeight);
                _shaderMat.SetShaderParameter("use_terrain_height", true);
            }
            else
            {
                // Fallback: If no terrain context exists (e.g. no overlapping regions), render flat
                _shaderMat.SetShaderParameter("use_terrain_height", false);
            }

            // 2. UPDATE MASK TEXTURE (Layer Data)
            if (_ownerLayer.layerTextureRID.IsValid)
            {
                UpdateSharedTexture(ref _rdLayerMask, _ownerLayer.layerTextureRID);
                _shaderMat.SetShaderParameter("layer_data_tex", _rdLayerMask);
            }

            float visualStrength = 1.0f;

            if (_ownerLayer is HeightLayer heightLayer)
            {
                visualStrength = heightLayer.Strength;
                // Optional: Handle subtract mode if you want red-displacement
                if (heightLayer.Operation == HeightLayer.HeightOperation.Subtract)
                {
                    visualStrength *= -1.0f;
                }
            }
            else if (_ownerLayer is FeatureLayer)
            {
                // Feature layers might handle height differently, usually baked into the mask
                // or specific properties, default to 1.0 for now.
                visualStrength = 1.0f;
            }

            _shaderMat.SetShaderParameter("layer_strength", visualStrength);
        }

        private void UpdateSharedTexture(ref Texture2Drd target, Rid sourceRid)
        {
            if (target == null) target = new Texture2Drd();

            // Only recreate the shared handle if the size changed or it's invalid
            if (!target.TextureRdRid.IsValid || _oldSize != _ownerLayer.Size)
            {
                target.TextureRdRid = TextureUtil.CreateSharedRenderingDeviceTextureRD(
                    sourceRid,
                    (ulong)_ownerLayer.Size.X,
                    (ulong)_ownerLayer.Size.Y);
            }
        }

        private void OnSelectionChanged()
        {
            var selection = EditorInterface.Singleton?.GetSelection();
            if (selection == null || !IsInstanceValid(_ownerLayer) || !IsInstanceValid(_visualMesh)) return;

            _isSelected = selection.GetSelectedNodes().Contains(_ownerLayer);
            _visualMesh.Visible = _isSelected;

            // Notify the manager about selection change
            var manager = TerrainHeightQuery.GetTerrainLayerManager();
            if (manager != null)
            {
                if (_isSelected)
                    manager.SetSelectedLayer(_ownerLayer);
            }

            if (_isSelected)
            {
                UpdateVisuals();
            }
        }
    }
}